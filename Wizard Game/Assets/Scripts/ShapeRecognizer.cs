using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems; // Required for the Event System

public class ShapeRecognizer : MonoBehaviour
{
    [Header("Setup")]
    public RectTransform drawAreaRectTransform; // The UI Panel where drawing occurs

    [Tooltip("Prefab available in Assets/ Third Party/ PDollar/ Prefabs")]
    public LineRenderer linePrefab; 

    [Header("Recognition Settings")]
    [Range(0, 1)]
    public float accuracyThreshold = 0.6f; // 60% accuracy required
    public float overlapTolerance = 50.0f; // In canvas units, how close points need to be

    private List<Vector2> templatePoints;
    private LineRenderer currentLine;
    private List<Vector2> drawnPoints = new List<Vector2>();
    private bool isDrawing = false;
    private int score = 0;
     private string feedbackMessage = ""; // Feedback message on accuracy

    void Start()
    {
        // Generate the 'S' template based on the size of the draw area
        templatePoints = CreateStandardTemplatePath(drawAreaRectTransform.rect);
        feedbackMessage = "Draw an 'S' to begin."; 
    }

    void Update()
    {
        // Get the mouse position in the local space of the canvas
        Vector2? localPoint = GetLocalPointInDrawArea();

        if (Input.GetMouseButtonDown(0))
        {
            // Only start drawing if the click is inside the designated draw area
            if (localPoint.HasValue)
            {
                StartDrawing(localPoint.Value);
            }
        }

        // Continue drawing if the mouse button is held down
        if (isDrawing && Input.GetMouseButton(0))
        {
            // We can continue drawing even if the cursor leaves the area, as long as it started inside
            if (localPoint.HasValue)
            {
                ContinueDrawing(localPoint.Value);
            }
        }

        // End drawing when the mouse button is released
        if (isDrawing && Input.GetMouseButtonUp(0))
        {
            EndDrawing();
        }
    }

    void StartDrawing(Vector2 startPoint)
    {
        isDrawing = true;
        drawnPoints.Clear();

         feedbackMessage = "Drawing...";

        if (currentLine != null)
        {
            Destroy(currentLine.gameObject);
        }

        // Instantiate the line renderer and parent it to the draw area
        currentLine = Instantiate(linePrefab, drawAreaRectTransform);
        currentLine.transform.localPosition = Vector3.zero;
        currentLine.transform.localRotation = Quaternion.identity;
        currentLine.positionCount = 0;

        AddPointToDrawing(startPoint);
    }

    void ContinueDrawing(Vector2 point)
    {
        // To avoid having too many points, only add a new one if it's far enough from the last
        if (Vector2.Distance(drawnPoints[drawnPoints.Count - 1], point) > 5f) // 5 units in canvas space
        {
            AddPointToDrawing(point);
        }
    }

    void EndDrawing()
    {
        isDrawing = false;
        if (drawnPoints.Count > 4) // Need a minimum number of points
        {
            ProcessDrawing();
        }
    }

    void AddPointToDrawing(Vector2 point)
    {
        drawnPoints.Add(point);
        currentLine.positionCount = drawnPoints.Count;
        // The points are set in the LineRenderer's local space
        currentLine.SetPosition(drawnPoints.Count - 1, new Vector3(point.x, point.y, 0));
    }

    void ProcessDrawing()
    {
        float accuracy = CalculateOverlapAccuracy(drawnPoints, templatePoints);
        string resultMessage;

        if (accuracy >= accuracyThreshold)
        {
            score++;
            // resultMessage = "Correct! Accuracy: " + (accuracy * 100).ToString("F1") + "%";
            feedbackMessage = "Correct! Accuracy: " + (accuracy * 100).ToString("F1") + "%";
        }
        else
        {
            // resultMessage = "Try again. Accuracy: " + (accuracy * 100).ToString("F1") + "%";
            feedbackMessage = "Try again. Accuracy: " + (accuracy * 100).ToString("F1") + "%";
        }
        // Debug.Log(resultMessage + " | Your score is now: " + score);
         Debug.Log(feedbackMessage + " | Your score is now: " + score);
    }

    // This is the core logic for translating screen coordinates to canvas coordinates
    private Vector2? GetLocalPointInDrawArea()
    {
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            drawAreaRectTransform,
            Input.mousePosition,
            Camera.main, // Or your specific UI camera
            out localPoint))
        {
            return localPoint;
        }
        return null;
    }

    // --- Accuracy calculation and Template generation are the same as before, but adapted ---

    float CalculateOverlapAccuracy(List<Vector2> drawnPath, List<Vector2> templatePath)
    {
        if (templatePath.Count == 0 || drawnPath.Count == 0) return 0;

        int overlappingDrawnPoints = 0;
        foreach (var drawnPoint in drawnPath)
        {
            bool isOverlapping = false;
            foreach (var templatePoint in templatePath)
            {
                if (Vector2.Distance(drawnPoint, templatePoint) <= overlapTolerance)
                {
                    isOverlapping = true;
                    break;
                }
            }
            if (isOverlapping) overlappingDrawnPoints++;
        }
        float drawingAccuracy = (float)overlappingDrawnPoints / drawnPath.Count;

        int coveredTemplatePoints = 0;
        foreach (var templatePoint in templatePath)
        {
            bool isCovered = false;
            foreach (var drawnPoint in drawnPath)
            {
                if (Vector2.Distance(templatePoint, drawnPoint) <= overlapTolerance)
                {
                    isCovered = true;
                    break;
                }
            }
            if (isCovered) coveredTemplatePoints++;
        }
        float templateCoverage = (float)coveredTemplatePoints / templatePath.Count;

        return Mathf.Min(drawingAccuracy, templateCoverage);
    }

    // Creates the 'S' template scaled to the draw area's rectangle
    List<Vector2> CreateStandardTemplatePath(Rect areaRect)
    {
        List<Vector2> path = new List<Vector2>();
        float w = areaRect.width / 2;
        float h = areaRect.height / 2;
        float scale = 0.8f; // Use 80% of the panel size for the 'S'

        // Define key points of an 'S' from top-right to bottom-left
        Vector2[] sPoints = new Vector2[] {
            new Vector2(0.5f, 0.9f), new Vector2(0, 0.9f), new Vector2(-0.5f, 0.7f),
            new Vector2(-0.5f, 0.2f), new Vector2(0, 0f), new Vector2(0.5f, -0.2f),
            new Vector2(0.5f, -0.7f), new Vector2(0, -0.9f), new Vector2(-0.5f, -0.9f)
        };
        
        // Interpolate between the key points to create a smoother path
        for (int i = 0; i < sPoints.Length -1; i++)
        {
            for(float t = 0; t < 1f; t += 0.1f) // 10 points between each key point
            {
                Vector2 point = Vector2.Lerp(sPoints[i], sPoints[i+1], t);
                path.Add(new Vector2(point.x * w * scale, point.y * h * scale));
            }
        }
        return path;
    }

    void OnGUI()
    {
        GUI.Box(new Rect(Screen.width - 260, 10, 250, 120), "Shape Recognition");
        GUI.Label(new Rect(Screen.width - 250, 40, 230, 20), "Score: " + score.ToString());
        GUI.Label(new Rect(Screen.width - 250, 70, 230, 50), "Draw an 'S' in the blue panel. \nAccuracy needed: " + (accuracyThreshold * 100) + "%");
        GUI.Label(new Rect(Screen.width - 250, 100, 230, 50), feedbackMessage);

    }
}
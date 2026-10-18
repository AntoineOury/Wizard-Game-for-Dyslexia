using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A walk-up doorway between scenes: when the player comes within Radius of
/// this object, a short travel overlay plays and Target Scene loads. It only
/// arms once the player has been seen OUTSIDE the radius, so spawning near a
/// door never bounces the player straight back through it.
///
/// Place an empty GameObject at the doorway, add this, type the scene name.
/// The target scene must be enabled in File > Build Settings. No references
/// to any other system — drop it in any scene with a player.
/// </summary>
public class ScenePortal : MonoBehaviour
{
    [Tooltip("Scene to load (must be enabled in Build Settings).")]
    public string targetScene = "PCG World";

    [Tooltip("How close the player must come, in meters.")]
    public float radius = 2.2f;

    [Tooltip("Short line shown while traveling.")]
    public string travelMessage = "Off to the wild!";

    Transform _player;
    bool _armed;
    bool _traveling;
    float _nextFindAt;

    void Update()
    {
        if (_traveling) return;

        if (_player == null)
        {
            if (Time.time < _nextFindAt) return;
            _nextFindAt = Time.time + 1f;
            var controller = FindObjectOfType<CharacterController>();
            if (controller == null) return;
            _player = controller.transform;
        }

        float distance = Vector3.Distance(_player.position, transform.position);
        if (!_armed)
        {
            if (distance > radius + 1f) _armed = true;
            return;
        }
        if (distance <= radius) StartCoroutine(Travel());
    }

    IEnumerator Travel()
    {
        _traveling = true;
        TravelOverlay.Show(travelMessage);
        yield return new WaitForSeconds(0.75f);
        PlayerControlScheme.UiMode = false;   // never carry a stuck UI pause into the next scene
        SceneManager.LoadScene(targetScene);
    }
}

/// <summary>
/// The temporary "Map" travel button: bootstraps itself like the backpack,
/// lives in the top-right corner, and appears only in the PCG World scene —
/// one tap teleports the player back to the Alchemist Store. Swap the two
/// scene names below when the map becomes a real screen later.
/// </summary>
public class TravelUI : MonoBehaviour
{
    const string ShownInScene = "PCG World";
    const string ButtonTarget = "Alchemist Store";

    static TravelUI _instance;
    GameObject _button;
    bool _traveling;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        _instance = new GameObject("Travel UI").AddComponent<TravelUI>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Refresh();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _traveling = false;
        Refresh();
    }

    void Refresh() => _button.SetActive(SceneManager.GetActiveScene().name == ShownInScene);

    void Build()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 44;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        _button = new GameObject("Map Button", typeof(RectTransform));
        _button.transform.SetParent(canvasGo.transform, false);
        var rect = (RectTransform)_button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-10f, -10f);
        rect.sizeDelta = new Vector2(150f, 48f);

        var image = _button.AddComponent<Image>();
        image.color = new Color(0.16f, 0.14f, 0.24f, 0.92f);
        var button = _button.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(TravelToStore);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(_button.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = "Map: Store";
        label.fontSize = 21f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.96f, 0.93f, 0.84f, 1f);
        label.raycastTarget = false;
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
    }

    void TravelToStore()
    {
        if (_traveling) return;
        _traveling = true;
        StartCoroutine(TravelRoutine());
    }

    IEnumerator TravelRoutine()
    {
        TravelOverlay.Show("Back to the Alchemist Store...");
        yield return new WaitForSeconds(0.75f);
        PlayerControlScheme.UiMode = false;
        SceneManager.LoadScene(ButtonTarget);
    }
}

/// <summary>
/// A quick fade-to-dark with a line of text, thrown up just before a scene
/// load. Lives in the dying scene on purpose — the load sweeps it away.
/// </summary>
public static class TravelOverlay
{
    public static void Show(string message)
    {
        var go = new GameObject("Travel Overlay");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var dimGo = new GameObject("Dim", typeof(RectTransform));
        dimGo.transform.SetParent(go.transform, false);
        var dimRect = (RectTransform)dimGo.transform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0.05f, 0.04f, 0.09f, 0f);

        var labelGo = new GameObject("Message", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = message;
        label.fontSize = 34f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.96f, 0.93f, 0.84f, 0f);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(900f, 80f);

        go.AddComponent<TravelOverlayFader>().Bind(dim, label);
    }
}

/// <summary>Fades the travel overlay in over a quarter second.</summary>
public class TravelOverlayFader : MonoBehaviour
{
    Image _dim;
    TMP_Text _label;
    float _t;

    public void Bind(Image dim, TMP_Text label)
    {
        _dim = dim;
        _label = label;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float a = Mathf.Clamp01(_t / 0.25f);
        if (_dim != null) _dim.color = new Color(_dim.color.r, _dim.color.g, _dim.color.b, 0.88f * a);
        if (_label != null)
        {
            Color c = _label.color;
            c.a = a;
            _label.color = c;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On-screen controls for touch mode: a virtual joystick bottom-left for
/// movement and an invisible drag-to-look surface over the right of the
/// screen. Built entirely at runtime — nothing to author in the scene — and
/// spawned automatically by FirstPersonController when the Touch scheme is
/// active.
///
/// Everything runs through the EventSystem's pointer events, which the mouse
/// drives just as well as fingers do — so touch mode is fully testable on a
/// laptop: click-drag the joystick, click-drag the right half to look.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public static TouchControls Instance { get; private set; }

    VirtualJoystick _joystick;
    TouchLookArea _lookArea;
    Canvas _canvas;

    /// <summary>Joystick deflection, each axis -1..1. Zero when hidden or untouched.</summary>
    public static Vector2 Move
        => Instance != null && Instance._joystick != null ? Instance._joystick.Value : Vector2.zero;

    /// <summary>Look drag accumulated since the last call, in percent of screen height.</summary>
    public static Vector2 ConsumeLookDelta()
        => Instance != null && Instance._lookArea != null ? Instance._lookArea.Consume() : Vector2.zero;

    public static void EnsureExists()
    {
        if (Instance != null) return;
        new GameObject("Touch Controls").AddComponent<TouchControls>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUi();
        PlayerControlScheme.Changed += OnSchemeChanged;
        OnSchemeChanged(PlayerControlScheme.Current);
    }

    void OnDestroy()
    {
        PlayerControlScheme.Changed -= OnSchemeChanged;
        if (Instance == this) Instance = null;
    }

    void OnSchemeChanged(ControlSchemeKind scheme)
    {
        if (_canvas != null) _canvas.gameObject.SetActive(scheme == ControlSchemeKind.Touch);
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("Touch Controls Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the game's own UI, so the menu buttons always win the raycast
        // over the look surface.
        _canvas.sortingOrder = -10;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Look surface: right two-thirds of the screen, invisible but raycastable.
        var lookGo = CreateChild(canvasGo.transform, "Look Area");
        var lookImage = lookGo.AddComponent<Image>();
        lookImage.color = Color.clear;
        var lookRect = (RectTransform)lookGo.transform;
        lookRect.anchorMin = new Vector2(0.35f, 0f);
        lookRect.anchorMax = Vector2.one;
        lookRect.offsetMin = Vector2.zero;
        lookRect.offsetMax = Vector2.zero;
        _lookArea = lookGo.AddComponent<TouchLookArea>();

        // Joystick bottom-left.
        Sprite circle = CreateCircleSprite(96);

        var baseGo = CreateChild(canvasGo.transform, "Joystick");
        var baseImage = baseGo.AddComponent<Image>();
        baseImage.sprite = circle;
        baseImage.color = new Color(1f, 1f, 1f, 0.25f);
        var baseRect = (RectTransform)baseGo.transform;
        baseRect.anchorMin = Vector2.zero;
        baseRect.anchorMax = Vector2.zero;
        baseRect.sizeDelta = new Vector2(260f, 260f);
        baseRect.anchoredPosition = new Vector2(230f, 230f);

        var knobGo = CreateChild(baseGo.transform, "Knob");
        var knobImage = knobGo.AddComponent<Image>();
        knobImage.sprite = circle;
        knobImage.color = new Color(1f, 1f, 1f, 0.6f);
        knobImage.raycastTarget = false; // the base handles all pointer events
        var knobRect = (RectTransform)knobGo.transform;
        knobRect.sizeDelta = new Vector2(110f, 110f);

        _joystick = baseGo.AddComponent<VirtualJoystick>();
        _joystick.knob = knobRect;
    }

    static GameObject CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>
    /// Soft-edged filled circle generated in code, so the joystick needs no
    /// sprite asset in the project.
    /// </summary>
    static Sprite CreateCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Touch Joystick Circle";
        float center = (size - 1) * 0.5f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01((radius - distance) / 2f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }
}

/// <summary>
/// Classic UI joystick: knob follows the pointer within the base's radius.
/// Tracks a single pointer id so a second finger (on the look area) can't
/// steal it mid-drag.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform knob;

    public Vector2 Value { get; private set; }

    RectTransform _rect;
    int _pointerId = int.MinValue;

    void Awake()
    {
        _rect = (RectTransform)transform;
    }

    void OnDisable()
    {
        _pointerId = int.MinValue;
        Value = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_pointerId != int.MinValue) return;
        _pointerId = eventData.pointerId;
        UpdateKnob(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        UpdateKnob(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        _pointerId = int.MinValue;
        Value = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }

    void UpdateKnob(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
        float radius = _rect.sizeDelta.x * 0.5f;
        Vector2 clamped = Vector2.ClampMagnitude(local, radius);
        Value = clamped / radius;
        if (knob != null) knob.anchoredPosition = clamped;
    }
}

/// <summary>
/// Invisible surface that turns pointer drags into look deltas. Deltas are
/// normalized by screen height so the same swipe turns the camera the same
/// amount on any resolution or DPI.
/// </summary>
public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    Vector2 _accumulated;
    int _pointerId = int.MinValue;

    public Vector2 Consume()
    {
        Vector2 value = _accumulated;
        _accumulated = Vector2.zero;
        return value;
    }

    void OnDisable()
    {
        _pointerId = int.MinValue;
        _accumulated = Vector2.zero;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_pointerId != int.MinValue) return;
        _pointerId = eventData.pointerId;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        _accumulated += eventData.delta * (100f / Mathf.Max(1, Screen.height));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        _pointerId = int.MinValue;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attach to a UI Button to make it the Laptop/Touch mode switch. Wires its
/// own onClick and keeps its label showing the ACTIVE scheme — no Inspector
/// OnClick setup needed. Works with both TextMeshPro and legacy Text labels.
/// </summary>
[RequireComponent(typeof(Button))]
public class ControlSchemeToggleButton : MonoBehaviour
{
    [Tooltip("Optional. Auto-found in children when empty.")]
    public TMP_Text tmpLabel;

    [Tooltip("Optional. Auto-found in children when empty.")]
    public Text legacyLabel;

    Button _button;

    void Awake()
    {
        _button = GetComponent<Button>();
        if (tmpLabel == null) tmpLabel = GetComponentInChildren<TMP_Text>(true);
        if (legacyLabel == null) legacyLabel = GetComponentInChildren<Text>(true);
    }

    void OnEnable()
    {
        _button.onClick.AddListener(OnClicked);
        PlayerControlScheme.Changed += OnSchemeChanged;
        Refresh();
    }

    void OnDisable()
    {
        _button.onClick.RemoveListener(OnClicked);
        PlayerControlScheme.Changed -= OnSchemeChanged;
    }

    void OnClicked()
    {
        PlayerControlScheme.Toggle();
        // A clicked button stays "selected", and the EventSystem re-presses the
        // selected control on Space/Enter (Submit). Deselect immediately so the
        // jump key can never re-fire this button.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void OnSchemeChanged(ControlSchemeKind scheme) => Refresh();

    void Refresh()
    {
        string text = PlayerControlScheme.Current == ControlSchemeKind.Touch
            ? "Controls: Touch"
            : "Controls: Laptop";
        if (tmpLabel != null) tmpLabel.text = text;
        if (legacyLabel != null) legacyLabel.text = text;
    }
}

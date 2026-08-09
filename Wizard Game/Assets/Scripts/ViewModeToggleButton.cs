using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a UI Button to make it the first/third person view switch. Wires
/// its own onClick and keeps its label showing the ACTIVE view — the same
/// recipe as ControlSchemeToggleButton: duplicate a button, add this, done.
/// </summary>
[RequireComponent(typeof(Button))]
public class ViewModeToggleButton : MonoBehaviour
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
        _button.onClick.AddListener(PlayerViewMode.Toggle);
        PlayerViewMode.Changed += OnViewChanged;
        Refresh();
    }

    void OnDisable()
    {
        _button.onClick.RemoveListener(PlayerViewMode.Toggle);
        PlayerViewMode.Changed -= OnViewChanged;
    }

    void OnViewChanged(ViewModeKind mode) => Refresh();

    void Refresh()
    {
        string text = PlayerViewMode.Current == ViewModeKind.ThirdPerson
            ? "View: 3rd Person"
            : "View: 1st Person";
        if (tmpLabel != null) tmpLabel.text = text;
        if (legacyLabel != null) legacyLabel.text = text;
    }
}

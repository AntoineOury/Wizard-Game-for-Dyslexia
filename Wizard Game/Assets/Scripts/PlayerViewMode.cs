using System;
using UnityEngine;

public enum ViewModeKind
{
    FirstPerson = 0,
    ThirdPerson = 1,
}

/// <summary>
/// The active camera view, shared by both player controllers and the UI
/// toggle button — the same pattern as PlayerControlScheme, and orthogonal to
/// it: view (first/third person) and input (laptop/touch) combine freely.
///
/// Stored in PlayerPrefs so the choice survives scene switches and restarts.
/// Defaults to first person.
/// </summary>
public static class PlayerViewMode
{
    const string PrefKey = "OtherwiseLabs.ViewMode";

    static ViewModeKind? _current;

    public static event Action<ViewModeKind> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _current = null;
        Changed = null;
    }

    public static ViewModeKind Current
    {
        get
        {
            if (_current == null)
                _current = (ViewModeKind)PlayerPrefs.GetInt(PrefKey, (int)ViewModeKind.FirstPerson);
            return _current.Value;
        }
    }

    public static void Set(ViewModeKind mode)
    {
        if (_current == mode) return;
        _current = mode;
        // Switching views is a "back to playing" action too: without this, an
        // Escape pressed before clicking the view toggle leaves the incoming
        // controller stuck in UI mode with gameplay input dead.
        PlayerControlScheme.UiMode = false;
        PlayerPrefs.SetInt(PrefKey, (int)mode);
        PlayerPrefs.Save();
        Changed?.Invoke(mode);
    }

    public static void Toggle()
        => Set(Current == ViewModeKind.FirstPerson ? ViewModeKind.ThirdPerson : ViewModeKind.FirstPerson);
}

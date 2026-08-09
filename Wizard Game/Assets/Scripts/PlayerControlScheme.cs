using System;
using UnityEngine;

public enum ControlSchemeKind
{
    Desktop = 0,  // keyboard + mouse, cursor locked while looking
    Touch = 1,    // on-screen joystick + drag-to-look, cursor always free
}

/// <summary>
/// The active control scheme, shared by the player controller, the on-screen
/// touch controls and the UI toggle button.
///
/// Stored in PlayerPrefs so the choice survives scene switches and restarts.
/// First launch defaults to Touch on phones/tablets and Desktop everywhere
/// else, so a build sent to a tablet needs no toggling to be playable.
/// </summary>
public static class PlayerControlScheme
{
    const string PrefKey = "OtherwiseLabs.ControlScheme";

    static ControlSchemeKind? _current;

    public static event Action<ControlSchemeKind> Changed;

    /// <summary>
    /// Desktop-only UI focus: true while gameplay input is suspended so the
    /// cursor can click buttons (Escape toggles it in the controllers).
    ///
    /// One shared flag, deliberately NOT per-controller: when each controller
    /// kept its own, an Escape pressed in first person left that controller
    /// convinced it was in UI mode forever — switch schemes and views a few
    /// times and you returned to Laptop with the keyboard dead and no clue why.
    /// Shared state cannot diverge, and every toggle press clears it, because
    /// tapping "Controls: Laptop" means "let me play".
    /// </summary>
    public static bool UiMode;

    // Statics survive play sessions when Enter Play Mode's domain reload is
    // off; reset explicitly so every run starts clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        UiMode = false;
        _current = null;
        Changed = null;
    }

    public static ControlSchemeKind Current
    {
        get
        {
            if (_current == null)
            {
                _current = PlayerPrefs.HasKey(PrefKey)
                    ? (ControlSchemeKind)PlayerPrefs.GetInt(PrefKey)
                    : Application.isMobilePlatform ? ControlSchemeKind.Touch : ControlSchemeKind.Desktop;
            }
            return _current.Value;
        }
    }

    public static void Set(ControlSchemeKind scheme)
    {
        if (_current == scheme) return;
        _current = scheme;
        UiMode = false;
        PlayerPrefs.SetInt(PrefKey, (int)scheme);
        PlayerPrefs.Save();
        Changed?.Invoke(scheme);
    }

    public static void Toggle()
        => Set(Current == ControlSchemeKind.Desktop ? ControlSchemeKind.Touch : ControlSchemeKind.Desktop);
}

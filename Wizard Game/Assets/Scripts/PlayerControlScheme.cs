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
        PlayerPrefs.SetInt(PrefKey, (int)scheme);
        PlayerPrefs.Save();
        Changed?.Invoke(scheme);
    }

    public static void Toggle()
        => Set(Current == ControlSchemeKind.Desktop ? ControlSchemeKind.Touch : ControlSchemeKind.Desktop);
}

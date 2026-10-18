using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The trustworthy pointer. In browser (WebGL) builds the position comes
    /// straight from the browser DOM (Plugins/WebGL/WebPointer.jslib) as a
    /// normalized canvas coordinate — immune to the display-scaling bugs that
    /// make Unity's own Input.mousePosition land shrunken or offset on some
    /// browsers. Everywhere else it is Unity's pointer, normalized.
    ///
    /// Use it for precision WORLD aim (tracing ink, trap placement, potion
    /// grabbing). UI code should keep using EventSystem coordinates so it
    /// stays consistent with how UGUI itself raycasts. Callers that need to
    /// correct for a device-specific constant offset (the capture trace's
    /// tap-the-star calibration) read <see cref="Viewport"/> and build their
    /// ray with <see cref="FromViewport"/>.
    /// </summary>
    public static class TruePointer
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void OtherwisePointer_Start();
        [DllImport("__Internal")] static extern int OtherwisePointer_HasData();
        [DllImport("__Internal")] static extern float OtherwisePointer_X();
        [DllImport("__Internal")] static extern float OtherwisePointer_Y();

        static bool _started;

        /// <summary>Pointer position in viewport space (0..1, GL-style y).</summary>
        public static Vector2 Viewport()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return new Vector2(0.5f, 0.5f);
            if (!_started)
            {
                try { OtherwisePointer_Start(); } catch (System.Exception) { }
                _started = true;
            }
            try
            {
                if (OtherwisePointer_HasData() == 1)
                    return new Vector2(OtherwisePointer_X(), OtherwisePointer_Y());
            }
            catch (System.Exception) { }
            return FallbackViewport();
        }
#else
        /// <summary>Pointer position in viewport space (0..1, GL-style y).</summary>
        public static Vector2 Viewport()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return new Vector2(0.5f, 0.5f);
            return FallbackViewport();
        }
#endif

        static Vector2 FallbackViewport()
        {
            Vector3 mouse = Input.mousePosition;
            return new Vector2(
                Screen.width > 0 ? mouse.x / Screen.width : 0.5f,
                Screen.height > 0 ? mouse.y / Screen.height : 0.5f);
        }

        public static Ray Ray(Camera camera) => FromViewport(camera, Viewport());

        public static Ray FromViewport(Camera camera, Vector2 viewport) =>
            camera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
    }
}

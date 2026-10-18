using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The trustworthy pointer ray. In browser (WebGL) builds the ray is built
    /// from the pointer's REAL position on the canvas, read straight from the
    /// browser (Plugins/WebGL/WebPointer.jslib) as a normalized viewport
    /// coordinate — immune to the display-scaling bugs that make Unity's own
    /// Input.mousePosition land shrunken and offset on some browsers (the
    /// letter-tracing bug). Everywhere else it is the ordinary
    /// ScreenPointToRay, unchanged.
    ///
    /// Use it for any precision WORLD aim (tracing ink, trap placement,
    /// potion grabbing). UI code should keep using EventSystem coordinates so
    /// it stays consistent with how UGUI itself raycasts.
    /// </summary>
    public static class TruePointer
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void OtherwisePointer_Start();
        [DllImport("__Internal")] static extern int OtherwisePointer_HasData();
        [DllImport("__Internal")] static extern float OtherwisePointer_X();
        [DllImport("__Internal")] static extern float OtherwisePointer_Y();

        static bool _started;

        public static Ray Ray(Camera camera)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (!_started)
            {
                try { OtherwisePointer_Start(); } catch (System.Exception) { }
                _started = true;
            }
            try
            {
                if (OtherwisePointer_HasData() == 1)
                    return camera.ViewportPointToRay(
                        new Vector3(OtherwisePointer_X(), OtherwisePointer_Y(), 0f));
            }
            catch (System.Exception) { }
            return camera.ScreenPointToRay(Input.mousePosition);
        }
#else
        public static Ray Ray(Camera camera)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return camera.ScreenPointToRay(Input.mousePosition);
        }
#endif
    }
}

using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// Where the pointer really is, as a normalized viewport position
    /// (0..1, y up). In browser builds it reads the pointer straight from the
    /// DOM through Plugins/WebGL/WebPointer.jslib, which stays correct on
    /// scaled displays where Unity's Input.mousePosition drifts. Everywhere
    /// else it is Input.mousePosition divided by the screen size.
    ///
    /// The Iso Puzzle uses this for everything that is not a UI button:
    /// turning the room, tapping potions, and tracing over the photo. Buttons
    /// stay on the EventSystem, the same as UGUI's own raycasts.
    /// </summary>
    public static class IsoPointer
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void OtherwisePointer_Start();
        [DllImport("__Internal")] static extern int OtherwisePointer_HasData();
        [DllImport("__Internal")] static extern float OtherwisePointer_X();
        [DllImport("__Internal")] static extern float OtherwisePointer_Y();

        static bool _started;

        public static Vector2 Viewport
        {
            get
            {
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
                return FromMouse();
            }
        }
#else
        public static Vector2 Viewport => FromMouse();
#endif

        public static Ray Ray(Camera camera)
        {
            Vector2 v = Viewport;
            return camera.ViewportPointToRay(new Vector3(v.x, v.y, 0f));
        }

        static Vector2 FromMouse()
        {
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x / Mathf.Max(1, Screen.width), m.y / Mathf.Max(1, Screen.height));
        }
    }
}

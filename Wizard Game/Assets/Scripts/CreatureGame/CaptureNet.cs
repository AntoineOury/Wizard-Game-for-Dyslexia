using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The butterfly net: an FPS-style tool held at the corner of the view.
    /// Visible whenever the player is in first person (roaming or capturing),
    /// hidden in third person. During the capture trace it leans and points
    /// toward wherever the player is drawing, so the net itself sweeps the
    /// letter's shape through the air — the tracing IS the net swing.
    ///
    /// Attach to the net object under the player (the controller also
    /// auto-attaches to any player child named like a "net" if none exists).
    /// The transform is driven in world space relative to the camera every
    /// frame, so where the net is parented never matters. Runs after the
    /// player controllers (execution order, see .meta) so its visibility
    /// verdict overrides their body show/hide sweeps.
    /// </summary>
    [AddComponentMenu("Otherwise Labs/Capture Net")]
    public class CaptureNet : MonoBehaviour
    {
        [Tooltip("Where the net rests, in camera space — the FPS-tool corner. X right, Y up, Z forward.")]
        public Vector3 restOffset = new Vector3(0.42f, -0.38f, 0.85f);

        [Tooltip("The net's resting tilt, in degrees, relative to the camera.")]
        public Vector3 restEuler = new Vector3(-12f, -8f, 6f);

        [Tooltip("How quickly the net catches up to the trace, per second. Lower = lazier, floatier follow.")]
        [Min(0.5f)] public float followSharpness = 10f;

        [Tooltip("How far the handle drifts sideways/up toward the trace point, in meters.")]
        [Range(0f, 0.6f)] public float traceSway = 0.25f;

        Camera _camera;
        CreatureGameController _game;
        Renderer[] _renderers;
        bool _visible = true;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_game == null) _game = FindObjectOfType<CreatureGameController>();
            if (_camera == null) return;

            bool capturing = _game != null && _game.Capture != null && _game.Capture.IsActive;
            SetVisible(capturing || PlayerViewMode.Current == ViewModeKind.FirstPerson);
            if (!_visible) return;

            Transform cam = _camera.transform;
            Vector3 targetPosition = cam.TransformPoint(restOffset);
            Quaternion targetRotation = cam.rotation * Quaternion.Euler(restEuler);

            if (capturing && _game.Capture.IsTracing)
            {
                Vector3 aim = _game.Capture.CurrentAimWorld;
                Vector3 aimLocal = cam.InverseTransformPoint(aim);
                var sway = new Vector3(
                    Mathf.Clamp(aimLocal.x * 0.12f, -traceSway, traceSway),
                    Mathf.Clamp(aimLocal.y * 0.12f, -traceSway, traceSway),
                    0f);
                targetPosition = cam.TransformPoint(restOffset + sway);
                Vector3 toAim = aim - targetPosition;
                if (toAim.sqrMagnitude > 0.001f)
                    targetRotation = Quaternion.LookRotation(toAim.normalized, cam.up);
            }

            // Exponential smoothing: framerate-independent lag that reads as a
            // hand carrying a real object rather than a rigid attachment.
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
        }

        // Runs after the controllers' SetBodyVisible sweeps (execution order),
        // so the net's own verdict is the one the frame renders with.
        void SetVisible(bool visible)
        {
            if (_visible == visible && Application.isPlaying) { ApplyVisibility(); return; }
            _visible = visible;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            foreach (Renderer renderer in _renderers)
                if (renderer != null && renderer.enabled != _visible) renderer.enabled = _visible;
        }
    }
}

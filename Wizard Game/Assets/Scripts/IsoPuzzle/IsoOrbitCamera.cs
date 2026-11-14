using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// The player's whole "body" in this game: an orthographic, isometric
    /// camera orbiting the room. Drag left/right to turn the room, up/down to
    /// tip it (when the room allows it). Arrow keys / WASD do the same.
    /// A short press without dragging is reported as a tap.
    ///
    /// Orthographic on purpose: it is what makes the hidden letter work (see
    /// IsoRoomBuilder) and gives the flat, Monument Valley diorama look.
    /// </summary>
    public class IsoOrbitCamera : MonoBehaviour
    {
        public const float DefaultPitch = 35f;

        public float distance = 30f;
        public float minPitch = 15f;
        public float maxPitch = 70f;
        public float yawPerScreen = 260f;     // degrees per full-width drag
        public float pitchPerScreen = 110f;   // degrees per full-height drag
        public float keyDegreesPerSecond = 90f;

        public Camera Camera { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; } = DefaultPitch;
        public bool InputEnabled { get; set; }
        public bool PitchLocked { get; set; } = true;
        public bool IsDragging => _dragging && _dragMoved;
        public bool IsEasing => _easing;
        /// <summary>The player is pressing, dragging, or holding a turn key. A room still coasting from a flick counts as let go.</summary>
        public bool IsHeld => _pressed || _keysHeld;
        public Vector3 Focus { get; set; } = new Vector3(0f, 2.4f, 0f);

        /// <summary>A press-and-release that barely moved. Argument: viewport position.</summary>
        public event System.Action<Vector2> Tapped;
        /// <summary>The player let go after turning the room (drag or keys).</summary>
        public event System.Action Released;

        bool _pressed, _dragging, _dragMoved;
        Vector2 _pressViewport, _lastViewport;
        float _pressTime;
        float _yawVelocity;
        bool _keysHeld;

        bool _easing;
        float _easeFromYaw, _easeFromPitch, _easeToYaw, _easeToPitch, _easeT, _easeDuration;
        System.Action _easeDone;

        readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        public static IsoOrbitCamera Create()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Iso Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            var orbit = cam.gameObject.AddComponent<IsoOrbitCamera>();
            orbit.Camera = cam;
            return orbit;
        }

        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);

        /// <summary>Angle in degrees between the current view direction and the given one.</summary>
        public float AngleTo(float yaw, float pitch)
            => Vector3.Angle(Rotation * Vector3.forward, Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);

        public void SetView(float yaw, float pitch)
        {
            _easing = false;
            _yawVelocity = 0f;
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            Apply();
        }

        /// <summary>Glide to a view (shortest way round), then call <paramref name="done"/>.</summary>
        public void EaseTo(float yaw, float pitch, float seconds, System.Action done = null)
        {
            _easing = true;
            _yawVelocity = 0f;
            _easeFromYaw = Yaw;
            _easeFromPitch = Pitch;
            _easeToYaw = Yaw + Mathf.DeltaAngle(Yaw, yaw);
            _easeToPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            _easeT = 0f;
            _easeDuration = Mathf.Max(0.01f, seconds);
            _easeDone = done;
        }

        /// <summary>Drops any half-finished press, e.g. when a panel opens.</summary>
        public void CancelPress()
        {
            _pressed = _dragging = _dragMoved = false;
            _yawVelocity = 0f;
        }

        void Awake()
        {
            if (Camera == null) Camera = GetComponent<Camera>();
        }

        void Update()
        {
            if (_easing)
            {
                _easeT += Time.deltaTime / _easeDuration;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_easeT));
                Yaw = Mathf.Lerp(_easeFromYaw, _easeToYaw, k);
                Pitch = Mathf.Lerp(_easeFromPitch, _easeToPitch, k);
                if (_easeT >= 1f)
                {
                    _easing = false;
                    System.Action done = _easeDone;
                    _easeDone = null;
                    done?.Invoke();
                }
            }
            else if (InputEnabled)
            {
                HandlePointer();
                HandleKeys();
                if (!_dragging && Mathf.Abs(_yawVelocity) > 0.5f)
                {
                    // A flick keeps turning a moment, like spinning a lazy susan.
                    Yaw += _yawVelocity * Time.deltaTime;
                    _yawVelocity = Mathf.Lerp(_yawVelocity, 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));
                    if (Mathf.Abs(_yawVelocity) <= 0.5f) { _yawVelocity = 0f; Released?.Invoke(); }
                }
            }
            else
            {
                CancelPress();
            }
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (Camera == null) return;
            // Keep the whole room in frame whatever the screen shape.
            float aspect = Mathf.Max(0.3f, Camera.aspect);
            Camera.orthographicSize = Mathf.Max(5.6f, 6.2f / aspect);
            Quaternion rotation = Rotation;
            transform.SetPositionAndRotation(Focus - rotation * Vector3.forward * distance, rotation);
        }

        void HandlePointer()
        {
            Vector2 viewport = IsoPointer.Viewport;

            if (Input.GetMouseButtonDown(0) && !PointerOverButton())
            {
                _pressed = true;
                _dragging = true;
                _dragMoved = false;
                _pressViewport = _lastViewport = viewport;
                _pressTime = Time.unscaledTime;
                _yawVelocity = 0f;
            }
            else if (_pressed && Input.GetMouseButton(0))
            {
                Vector2 delta = viewport - _lastViewport;
                _lastViewport = viewport;
                if (!_dragMoved && (viewport - _pressViewport).magnitude > 0.012f) _dragMoved = true;
                if (_dragMoved)
                {
                    float yawStep = delta.x * yawPerScreen;
                    Yaw += yawStep;
                    if (!PitchLocked) Pitch = Mathf.Clamp(Pitch - delta.y * pitchPerScreen, minPitch, maxPitch);
                    float instant = yawStep / Mathf.Max(Time.deltaTime, 0.001f);
                    _yawVelocity = Mathf.Lerp(_yawVelocity, instant, 0.35f);
                }
            }
            else if (_pressed && Input.GetMouseButtonUp(0))
            {
                bool wasTap = !_dragMoved && Time.unscaledTime - _pressTime < 0.5f;
                _pressed = _dragging = false;
                if (wasTap)
                {
                    _yawVelocity = 0f;
                    Tapped?.Invoke(viewport);
                }
                else
                {
                    if (Mathf.Abs(_yawVelocity) < 30f) { _yawVelocity = 0f; Released?.Invoke(); }
                    _yawVelocity = Mathf.Clamp(_yawVelocity, -240f, 240f);
                }
                _dragMoved = false;
            }
        }

        void HandleKeys()
        {
            float turn = 0f, tip = 0f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) turn += 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) turn -= 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) tip += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) tip -= 1f;

            bool held = turn != 0f || (tip != 0f && !PitchLocked);
            if (held)
            {
                Yaw += turn * keyDegreesPerSecond * Time.deltaTime;
                if (!PitchLocked) Pitch = Mathf.Clamp(Pitch + tip * keyDegreesPerSecond * 0.6f * Time.deltaTime, minPitch, maxPitch);
            }
            if (_keysHeld && !held) Released?.Invoke();
            _keysHeld = held;
        }

        /// <summary>True when the pointer is over a UI button (presses there are not room turns).</summary>
        public bool PointerOverButton()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            var data = new PointerEventData(events) { position = Input.mousePosition };
            _uiHits.Clear();
            events.RaycastAll(data, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Selectable>() != null)
                    return true;
            return false;
        }
    }
}

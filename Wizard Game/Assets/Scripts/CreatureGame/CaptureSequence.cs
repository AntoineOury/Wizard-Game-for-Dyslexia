using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The capture moment, played in the world instead of on a UI panel: the
    /// camera glides from wherever the third-person orbit had it down into the
    /// player's eyes, a dotted letter hangs in the air above the stuck
    /// creature, and the player traces it by dragging in the air. Correct →
    /// the creature is caught and the camera glides back out; wrong → the
    /// creature taunts and the ink clears for another try. No canvas, no
    /// buttons — just the world, plus the one-line hint bar. (This is the
    /// motion that will later be skinned as the butterfly-net swing.)
    ///
    /// Camera control without touching the controllers: this script's
    /// execution order runs AFTER them (see its .meta), so each frame it
    /// simply overwrites the camera pose they wrote. Blending from and back to
    /// "whatever the controller wants this frame" is what makes both ends of
    /// the transition seamless in either view mode.
    ///
    /// Gameplay input freezes through the same UiMode flag panels use; the
    /// touch joystick (which UiMode does not gate) is put to sleep by
    /// deactivating the TouchControls object for the duration.
    /// </summary>
    public class CaptureSequence : MonoBehaviour
    {
        enum Phase { Idle, FlyIn, Tracing, FlyOut }

        const float FlyInSeconds = 0.9f;
        const float FlyOutSeconds = 0.7f;
        const float LetterHeight = 2.35f;   // letter center above the creature's feet
        const float LetterScale = 1.6f;     // world height of the letter's unit box
        const float EvaluateDelay = 1.6f;   // idle time after a stroke before grading (multi-stroke letters)

        public bool IsActive => _phase != Phase.Idle;
        public string HintText { get; private set; } = "";

        CreatureGameController _game;
        Camera _camera;
        LetterCreature _creature;
        Phase _phase;
        float _t;

        // The trace plane, its basis, and the drawing state.
        Vector3 _planeCenter;
        Quaternion _planeRotation;
        Vector3 _planeRight, _planeUp;
        Plane _plane;
        Transform _visualRoot;
        Transform _inkRoot;
        Material _guideMaterial;
        Material _inkMaterial;
        readonly List<Vector2> _drawnUnit = new List<Vector2>();
        Vector2 _lastInkUnit;
        bool _stroking;
        float _evaluateAt;

        readonly List<Renderer> _hiddenBody = new List<Renderer>();
        GameObject _sleepingTouchControls;

        public void Init(CreatureGameController game) => _game = game;

        /// <summary>Start the capture moment for a stuck creature.</summary>
        public void Begin(LetterCreature creature)
        {
            if (IsActive || creature == null || creature.CurrentState != LetterCreature.State.Stuck) return;
            _camera = Camera.main;
            if (_camera == null || _game.Player == null) return;

            _creature = creature;
            _phase = Phase.FlyIn;
            _t = 0f;
            _drawnUnit.Clear();
            _evaluateAt = 0f;
            HintText = "";

            // The letter hangs above the creature, facing the player's eyes.
            // "Away-facing" rotation on purpose: a Unity quad shows its -Z
            // face, and this orientation both turns that face to the viewer
            // and keeps the letter un-mirrored.
            Vector3 head = HeadPosition();
            _planeCenter = _creature.transform.position + Vector3.up * LetterHeight;
            Vector3 toPlane = _planeCenter - head;
            toPlane.y *= 0.4f; // keep the plane near-upright rather than tilted flat
            _planeRotation = Quaternion.LookRotation(toPlane.normalized);
            _planeRight = _planeRotation * Vector3.right;
            _planeUp = _planeRotation * Vector3.up;
            _plane = new Plane((head - _planeCenter).normalized, _planeCenter);

            HidePlayerBody();
            SleepTouchControls();
            BuildGuide();
        }

        /// <summary>Back out without capturing (Escape, button toggle, creature lost).</summary>
        public void Cancel()
        {
            if (_phase == Phase.FlyIn || _phase == Phase.Tracing) BeginFlyOut();
        }

        void Update()
        {
            if (!IsActive) return;

            // The creature can vanish mid-moment (escaped paper removal, etc.).
            if (_creature == null && _phase != Phase.FlyOut) { BeginFlyOut(); return; }

            if (_phase == Phase.Tracing)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
                HandleDrawing();

                if (_evaluateAt > 0f && Time.time >= _evaluateAt && !_stroking)
                    Evaluate();
            }
        }

        // Runs AFTER the player controllers wrote their camera pose (script
        // execution order), so assignment here always wins the frame.
        void LateUpdate()
        {
            if (!IsActive || _camera == null) return;

            Transform cam = _camera.transform;
            Vector3 headPos = HeadPosition();
            Quaternion headRot = Quaternion.LookRotation((_planeCenter - headPos).normalized);

            switch (_phase)
            {
                case Phase.FlyIn:
                {
                    _t += Time.deltaTime / FlyInSeconds;
                    float blend = Mathf.SmoothStep(0f, 1f, _t);
                    // Blend FROM the pose the controller just wrote this frame.
                    cam.position = Vector3.Lerp(cam.position, headPos, blend);
                    cam.rotation = Quaternion.Slerp(cam.rotation, headRot, blend);
                    if (_t >= 1f)
                    {
                        _phase = Phase.Tracing;
                        HintText = $"Wave and trace the {_creature.Letter} in the air!  (Esc to step back)";
                    }
                    break;
                }

                case Phase.Tracing:
                    cam.position = headPos;
                    cam.rotation = headRot;
                    break;

                case Phase.FlyOut:
                {
                    _t += Time.deltaTime / FlyOutSeconds;
                    float blend = Mathf.SmoothStep(0f, 1f, _t);
                    // Blend TOWARD the live controller pose, then let go — the
                    // handoff is seamless because the target IS their output.
                    Vector3 controllerPos = cam.position;
                    Quaternion controllerRot = cam.rotation;
                    cam.position = Vector3.Lerp(headPos, controllerPos, blend);
                    cam.rotation = Quaternion.Slerp(headRot, controllerRot, blend);
                    if (_t >= 1f) Finish();
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Drawing in the air
        // ------------------------------------------------------------------

        void HandleDrawing()
        {
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                _stroking = true;
                _evaluateAt = 0f; // a new stroke pauses the grading countdown
                TryInk(force: true);
            }
            else if (Input.GetMouseButton(0) && _stroking)
            {
                TryInk(force: false);
            }
            else if (Input.GetMouseButtonUp(0) && _stroking)
            {
                _stroking = false;
                if (_drawnUnit.Count >= 8)
                    _evaluateAt = Time.time + EvaluateDelay; // wait for possible next stroke
            }
        }

        void TryInk(bool force)
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            if (!_plane.Raycast(ray, out float distance)) return;

            Vector3 world = ray.GetPoint(distance);
            Vector3 offset = world - _planeCenter;
            var unit = new Vector2(Vector3.Dot(offset, _planeRight), Vector3.Dot(offset, _planeUp)) / LetterScale;
            if (Mathf.Abs(unit.x) > 0.75f || Mathf.Abs(unit.y) > 0.75f) return; // off the letter's slate

            if (!force && (unit - _lastInkUnit).sqrMagnitude < 0.0009f) return; // ~3 cm between ink dots
            _lastInkUnit = unit;
            _drawnUnit.Add(unit);
            MakeDot(_inkRoot, unit, 0.055f, _inkMaterial);
        }

        void Evaluate()
        {
            _evaluateAt = 0f;
            if (_creature == null) { BeginFlyOut(); return; }

            float accuracy = LetterShapes.ScoreTrace(_drawnUnit, _creature.Letter);
            if (accuracy >= _game.traceAccuracyThreshold)
            {
                LetterCreature caught = _creature;
                ClearInk();
                _game.CompleteCapture(caught);
                BeginFlyOut();
            }
            else
            {
                _creature.TauntWhileStuck();
                HintText = $"So close ({Mathf.RoundToInt(accuracy * 100f)}%)! Trace the {_creature.Letter} again — stay on the dots.";
                ClearInk();
            }
        }

        // ------------------------------------------------------------------
        // World-space letter visuals
        // ------------------------------------------------------------------

        void BuildGuide()
        {
            _visualRoot = new GameObject("Capture Trace").transform;
            _visualRoot.SetPositionAndRotation(_planeCenter, _planeRotation);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _guideMaterial = new Material(shader) { color = new Color(0.55f, 0.75f, 1f, 0.95f) };
            _inkMaterial = new Material(shader) { color = new Color(0.15f, 0.15f, 0.3f, 1f) };

            foreach (Vector2 point in LetterShapes.SamplePath(_creature.Letter, 0.075f))
                MakeDot(_visualRoot, point, 0.07f, _guideMaterial);

            var inkGo = new GameObject("Ink");
            inkGo.transform.SetParent(_visualRoot, false);
            _inkRoot = inkGo.transform;
        }

        void MakeDot(Transform parent, Vector2 unit, float size, Material material)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "Dot";
            dot.transform.SetParent(parent, false);
            dot.transform.localPosition = new Vector3(unit.x * LetterScale, unit.y * LetterScale, 0f);
            dot.transform.localRotation = Quaternion.identity;
            dot.transform.localScale = new Vector3(size, size, 1f);
            dot.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void ClearInk()
        {
            _drawnUnit.Clear();
            if (_inkRoot == null) return;
            for (int i = _inkRoot.childCount - 1; i >= 0; i--)
                Destroy(_inkRoot.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------
        // Sequence plumbing
        // ------------------------------------------------------------------

        void BeginFlyOut()
        {
            _phase = Phase.FlyOut;
            _t = 0f;
            _stroking = false;
            _evaluateAt = 0f;
            HintText = "";
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
        }

        void Finish()
        {
            _phase = Phase.Idle;
            _creature = null;
            RestorePlayerBody();
            WakeTouchControls();
            if (_guideMaterial != null) Destroy(_guideMaterial);
            if (_inkMaterial != null) Destroy(_inkMaterial);
        }

        Vector3 HeadPosition()
            => (_game.Player != null ? _game.Player.position : transform.position) + Vector3.up * 1.7f;

        /// <summary>
        /// First person from inside the body means hiding the body, exactly as
        /// the first-person controller does. Local copy of that idea so the
        /// mini-game keeps its single touch-point with the control scripts.
        /// </summary>
        void HidePlayerBody()
        {
            _hiddenBody.Clear();
            if (_game.Player == null) return;
            foreach (Renderer renderer in _game.Player.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (renderer is CanvasRenderer || renderer.GetComponentInParent<Canvas>() != null) continue;
                renderer.enabled = false;
                _hiddenBody.Add(renderer);
            }
        }

        void RestorePlayerBody()
        {
            foreach (Renderer renderer in _hiddenBody)
                if (renderer != null) renderer.enabled = true;
            _hiddenBody.Clear();
        }

        /// <summary>
        /// UiMode freezes keyboard movement but not the on-screen joystick, so
        /// on touch the whole TouchControls object naps during the capture —
        /// its own OnDisable zeroes the stick, and reactivating restores it.
        /// </summary>
        void SleepTouchControls()
        {
            if (TouchControls.Instance == null || !TouchControls.Instance.gameObject.activeSelf) return;
            _sleepingTouchControls = TouchControls.Instance.gameObject;
            _sleepingTouchControls.SetActive(false);
        }

        void WakeTouchControls()
        {
            if (_sleepingTouchControls != null) _sleepingTouchControls.SetActive(true);
            _sleepingTouchControls = null;
        }
    }
}

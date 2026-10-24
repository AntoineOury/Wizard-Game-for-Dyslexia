using System;
using System.Collections;
using System.Collections.Generic;
using OtherwiseLabs.CreatureGame;
using OtherwiseLabs.TerrainTools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// Letter Lens — a Viewfinder-style letter hunt. Letters are hidden in the
    /// world by forced perspective (see HiddenLetter): their props only line
    /// up into a letter from one standpoint. The player walks around, notices
    /// a shape coming together, presses C to LOCK IN the view, then traces
    /// the letter they see right on the screen. A trace that matches a letter
    /// that really lines up from that view finds it.
    ///
    /// Standalone on purpose: it does not feed the vial / paper economy yet.
    /// Finding a letter raises <see cref="LetterFound"/>; what "receiving" a
    /// letter means is a later step.
    ///
    /// What you see is what counts: alignment is measured from the live
    /// camera, so it works in third person too (first person is easier — the
    /// vantage ring on the ground is where the EYES must be).
    ///
    /// Bootstraps itself in the PCG World scene; a scene-placed copy wins and
    /// can be tuned in the Inspector. Runs after the player controllers (see
    /// .meta execution order) so the locked camera pose wins every frame.
    /// </summary>
    public class LetterLensGame : MonoBehaviour
    {
        public const string HostSceneName = "PCG World";

        /// <summary>Raised with the letter each time one is found.</summary>
        public static event Action<char> LetterFound;

        public static LetterLensGame Instance { get; private set; }

        [Header("Hidden letters")]
        [Min(0)] public int bushLetters = 3;
        [Min(0)] public int houseLetters = 3;
        [Min(0)] public int buildingLetters = 2;
        [Tooltip("Letters to hide, picked without repeats until the list runs out.")]
        public string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        [Tooltip("0 = a new layout every play.")]
        public int seed;
        [Tooltip("Keep vantage points this far inside the terrain's edge.")]
        [Min(0f)] public float edgeMargin = 30f;
        [Tooltip("The first letter is a small one near where the player starts, so the idea is discovered quickly.")]
        public bool firstLetterNearPlayer = true;

        [Header("Lock in and trace")]
        public KeyCode lockKey = KeyCode.C;
        [Tooltip("How well the dots must line up from the locked view, in letter units. Smaller = stand more exactly on the spot.")]
        [Range(0.02f, 0.2f)] public float alignTolerance = 0.07f;
        [Tooltip("Trace score needed (same grader as the creature capture).")]
        [Range(0.2f, 0.95f)] public float traceAccuracyThreshold = 0.55f;
        [Tooltip("Idle time after a stroke before the trace is checked, so letters with several strokes can be finished.")]
        [Min(0.3f)] public float evaluateDelay = 1.4f;
        [Tooltip("Hint line when the player is close to lining a letter up.")]
        public bool alignmentHints = true;

        public bool IsLockedIn => _phase != Phase.Walking;
        public IReadOnlyList<char> FoundLetters => _found;

        enum Phase { Walking, Tracing, Celebrating }

        readonly List<HiddenLetter> _letters = new List<HiddenLetter>();
        readonly List<char> _found = new List<char>();
        readonly List<Vector3> _traceDirections = new List<Vector3>();
        Phase _phase;
        Transform _player;
        CharacterController _playerController;
        Camera _camera;
        LetterLensUI _ui;
        CaptureSequence _capture;
        Vector3 _eyeLocal = new Vector3(0f, 1.6f, 0f);
        float _eyeAboveFeet = 1.6f;

        // Lock-in state
        Vector3 _lockPosition;
        Quaternion _lockRotation;
        bool _uiModeBefore;
        GameObject _sleepingTouchControls;
        bool _stroking;
        float _evaluateAt;
        Vector3 _lastInkDirection;
        float _celebrateUntil;
        float _nextHintCheck;

        // ------------------------------------------------------------------
        // Bootstrap
        // ------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            LetterFound = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureInScene(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInScene(scene);

        static void EnsureInScene(Scene scene)
        {
            if (scene.name != HostSceneName) return;
            if (FindObjectOfType<LetterLensGame>() != null) return;
            var go = new GameObject("Letter Lens Mini-Game");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<LetterLensGame>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            HiddenLetter.ForgetTerrain();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsLockedIn) ReleaseControls();
        }

        IEnumerator Start()
        {
            _ui = gameObject.AddComponent<LetterLensUI>();
            _ui.Init(this);

            var characterController = FindObjectOfType<CharacterController>();
            if (characterController != null)
            {
                _playerController = characterController;
                _player = characterController.transform;
                MeasureEye();
            }

            // Let the player land and the terrain collider settle first.
            while (PlayerTerrainSpawner.SpawnPending) yield return null;
            yield return null;
            yield return null;

            foreach (HiddenLetter placed in FindObjectsOfType<HiddenLetter>())
            {
                if (!placed.IsBuilt) placed.Build();
                _letters.Add(placed);
            }
            SpawnLetters();
            _ui.SetFound(_found);
        }

        /// <summary>
        /// Where the first-person camera sits on the body: vantage points are
        /// built for exactly that eye height, so standing on the ring and
        /// looking out lines the letter up.
        /// </summary>
        void MeasureEye()
        {
            var firstPerson = _player.GetComponent<FirstPersonController>();
            Transform eye = firstPerson != null ? firstPerson.cameraTransform : null;
            if (eye != null && eye.parent == _player) _eyeLocal = eye.localPosition;

            float scale = _player.lossyScale.y;
            float feetLocal = _playerController.center.y - _playerController.height * 0.5f;
            _eyeAboveFeet = Mathf.Max(0.5f, (_eyeLocal.y - feetLocal) * scale + _playerController.skinWidth);
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        void SpawnLetters()
        {
            var rng = new System.Random(seed != 0 ? seed : Environment.TickCount);
            var generator = FindObjectOfType<ProceduralTerrainGenerator>();

            Rect area;
            if (generator != null)
            {
                Rect footprint = generator.WorldFootprint;
                float margin = Mathf.Min(edgeMargin, footprint.width * 0.3f, footprint.height * 0.3f);
                area = Rect.MinMaxRect(footprint.xMin + margin, footprint.yMin + margin, footprint.xMax - margin, footprint.yMax - margin);
            }
            else
            {
                Vector3 origin = _player != null ? _player.position : Vector3.zero;
                area = new Rect(origin.x - 150f, origin.z - 150f, 300f, 300f);
            }

            var sizes = new List<LetterSize>();
            for (int i = 0; i < bushLetters; i++) sizes.Add(LetterSize.Bush);
            for (int i = 0; i < houseLetters; i++) sizes.Add(LetterSize.House);
            for (int i = 0; i < buildingLetters; i++) sizes.Add(LetterSize.Building);

            var letterPool = new List<char>();
            for (int i = 0; i < sizes.Count; i++)
            {
                if (letterPool.Count == 0) RefillPool(letterPool);
                if (letterPool.Count == 0) break;
                int pick = rng.Next(letterPool.Count);
                char letter = letterPool[pick];
                letterPool.RemoveAt(pick);

                bool nearPlayer = i == 0 && firstLetterNearPlayer && _player != null;
                if (!TryPlace(sizes[i], letter, area, generator, rng, nearPlayer) && nearPlayer)
                    TryPlace(sizes[i], letter, area, generator, rng, false);
            }
        }

        void RefillPool(List<char> pool)
        {
            foreach (char c in alphabet.ToUpperInvariant())
                if (c >= 'A' && c <= 'Z' && !pool.Contains(c)) pool.Add(c);
        }

        bool TryPlace(LetterSize size, char letter, Rect area, ProceduralTerrainGenerator generator, System.Random rng, bool nearPlayer)
        {
            float distance = size == LetterSize.Bush ? 5.5f : size == LetterSize.House ? 17f : 42f;
            float spacing = Mathf.Max(20f, distance * 1.2f);

            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector2 xz;
                if (nearPlayer)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float radius = Mathf.Lerp(9f, 22f, (float)rng.NextDouble());
                    xz = new Vector2(_player.position.x + Mathf.Cos(angle) * radius, _player.position.z + Mathf.Sin(angle) * radius);
                }
                else
                {
                    xz = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()),
                                     Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
                }

                float yaw = (float)rng.NextDouble() * 360f;
                Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector2 footXZ = xz + new Vector2(flat.x, flat.z) * distance;
                if (!area.Contains(footXZ) && !nearPlayer) continue;

                if (!DryGround(xz, generator, out float groundY)) continue;
                if (!DryGround(footXZ, generator, out _)) continue;
                if (!Gentle(xz, groundY)) continue;

                var eye = new Vector3(xz.x, groundY + _eyeAboveFeet, xz.y);
                var foot = new Vector3(footXZ.x, 0f, footXZ.y);
                if (TooClose(eye, foot, spacing)) continue;
                if (!ClearView(eye, foot, distance)) continue;

                var go = new GameObject($"Hidden Letter {letter}");
                go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flat));
                go.transform.SetParent(transform, true);
                var hidden = go.AddComponent<HiddenLetter>();
                hidden.letter = letter;
                hidden.size = size;
                hidden.buildOnStart = false;
                hidden.seed = rng.Next(1, int.MaxValue);
                hidden.Build();
                _letters.Add(hidden);
                return true;
            }
            return false;
        }

        static bool DryGround(Vector2 xz, ProceduralTerrainGenerator generator, out float groundY)
        {
            if (!HiddenLetter.TryGroundY(xz.x, xz.y, out groundY)) return false;
            if (generator == null) return true;
            return groundY > generator.WaterSurfaceAt(new Vector3(xz.x, groundY, xz.y)) + 0.4f;
        }

        /// <summary>No vantage on a cliff: the ring must be somewhere a child can stand still.</summary>
        static bool Gentle(Vector2 xz, float groundY)
        {
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                if (!HiddenLetter.TryGroundY(xz.x + Mathf.Cos(angle) * 1.5f, xz.y + Mathf.Sin(angle) * 1.5f, out float y)) return false;
                if (Mathf.Abs(y - groundY) > 0.9f) return false;
            }
            return true;
        }

        bool TooClose(Vector3 eye, Vector3 foot, float spacing)
        {
            float sqr = spacing * spacing;
            foreach (HiddenLetter other in _letters)
            {
                if (other == null) continue;
                if (FlatSqr(other.EyePoint, eye) < sqr || FlatSqr(other.Center, foot) < sqr || FlatSqr(other.Center, eye) < sqr * 0.5f)
                    return true;
            }
            if (_player != null && FlatSqr(_player.position, eye) < 9f) return true; // never spawn on top of the player
            return false;
        }

        static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Hills, trees or houses between the vantage and the letter would hide it.</summary>
        bool ClearView(Vector3 eye, Vector3 foot, float distance)
        {
            if (!HiddenLetter.TryGroundY(foot.x, foot.z, out float footY)) return false;
            var center = new Vector3(foot.x, footY + 0.5f * (distance / 4f), foot.z);
            Vector3 toCenter = center - eye;
            float length = toCenter.magnitude;
            foreach (RaycastHit hit in Physics.RaycastAll(eye, toCenter / length, length * 0.95f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_player != null && PlayerRig.IsSelfCollider(hit.collider, _player)) continue;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Lock in, trace, check
        // ------------------------------------------------------------------

        void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_capture == null) _capture = FindObjectOfType<CaptureSequence>();

            switch (_phase)
            {
                case Phase.Walking:
                    if (Input.GetKeyDown(lockKey)) TryLockIn();
                    else UpdateWalkingHint();
                    break;

                case Phase.Tracing:
                    if (Input.GetKeyDown(lockKey) || Input.GetKeyDown(KeyCode.Escape)) { StepOut(); return; }
                    HandleDrawing();
                    if (_evaluateAt > 0f && Time.time >= _evaluateAt && !_stroking) Evaluate();
                    break;

                case Phase.Celebrating:
                    if (Time.time >= _celebrateUntil) StepOut();
                    break;
            }
        }

        void LateUpdate()
        {
            // Hold the locked view still: the controllers are input-frozen, but
            // the third-person camera's collision easing could still nudge it.
            if (!IsLockedIn || _camera == null) return;
            _camera.transform.SetPositionAndRotation(_lockPosition, _lockRotation);
        }

        public bool CanLockIn
            => _phase == Phase.Walking && _camera != null && _player != null
               && (_capture == null || !_capture.IsActive);

        /// <summary>What the C key and the Lock in button call.</summary>
        public void ToggleLockIn()
        {
            if (_phase == Phase.Walking) TryLockIn();
            else if (_phase == Phase.Tracing) StepOut();
        }

        void TryLockIn()
        {
            if (!CanLockIn) return;
            _phase = Phase.Tracing;
            _lockPosition = _camera.transform.position;
            _lockRotation = _camera.transform.rotation;
            _traceDirections.Clear();
            _stroking = false;
            _evaluateAt = 0f;

            _uiModeBefore = PlayerControlScheme.UiMode;
            PlayerControlScheme.UiMode = true; // freezes walking and looking, frees the cursor for drawing
            SleepTouchControls();

            _ui.ClearInk();
            _ui.ShowLockedIn(true);
            _ui.SetHint(Touch ? "Locked in! Trace the letter you see." : "Locked in! Trace the letter you see.  (C to step out)");
        }

        void StepOut()
        {
            _phase = Phase.Walking;
            _stroking = false;
            _evaluateAt = 0f;
            _traceDirections.Clear();
            ReleaseControls();
            _ui.ClearInk();
            _ui.ShowLockedIn(false);
            _ui.SetHint("");
        }

        void ReleaseControls()
        {
            PlayerControlScheme.UiMode = _uiModeBefore;
            WakeTouchControls();
        }

        void HandleDrawing()
        {
            bool overUi = PointerOverUi();
            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                _stroking = true;
                _evaluateAt = 0f; // a new stroke pauses the check countdown
                AddInk(force: true);
            }
            else if (Input.GetMouseButton(0) && _stroking)
            {
                AddInk(force: false);
            }
            else if (Input.GetMouseButtonUp(0) && _stroking)
            {
                _stroking = false;
                if (_traceDirections.Count >= 8) _evaluateAt = Time.time + evaluateDelay;
            }
        }

        void AddInk(bool force)
        {
            // TruePointer: in browser builds the ray comes from the pointer's
            // real canvas position, so ink lands under the finger at any
            // display scale.
            Ray ray = TruePointer.Ray(_camera);
            Vector3 direction = ray.direction.normalized;
            if (!force && Vector3.Angle(direction, _lastInkDirection) < 0.25f) return;
            _lastInkDirection = direction;
            _traceDirections.Add(direction);

            Vector3 viewport = _camera.WorldToViewportPoint(_lockPosition + direction);
            _ui.AddInk(new Vector2(viewport.x, viewport.y));
        }

        void Evaluate()
        {
            _evaluateAt = 0f;
            Vector3 forward = _lockRotation * Vector3.forward;
            Vector3 right = _lockRotation * Vector3.right;
            Vector3 up = _lockRotation * Vector3.up;

            HiddenLetter best = null;
            float bestScore = 0f;
            bool anyAligned = false, anyNear = false, alreadyFound = false;

            foreach (HiddenLetter hidden in _letters)
            {
                if (hidden == null || !hidden.IsBuilt) continue;
                Vector3 viewport = _camera.WorldToViewportPoint(hidden.Center);
                if (viewport.z <= 0f || viewport.x < -0.05f || viewport.x > 1.05f || viewport.y < -0.05f || viewport.y > 1.05f) continue;
                if (!hidden.Fit(_lockPosition, forward, right, up, out Vector2 offset, out float scale, out float residual)) continue;

                if (residual > alignTolerance)
                {
                    if (residual < alignTolerance * 3f && !hidden.IsFound) anyNear = true;
                    continue;
                }

                float score = ScoreAgainst(hidden.letter, offset, scale, forward, right, up);
                if (score < traceAccuracyThreshold) { if (!hidden.IsFound) anyAligned = true; continue; }
                if (hidden.IsFound) { alreadyFound = true; continue; }
                if (score > bestScore) { bestScore = score; best = hidden; }
            }

            if (best != null) { Found(best); return; }

            _traceDirections.Clear();
            _ui.ClearInk();
            if (alreadyFound)
                _ui.SetHint($"You already found that one! {StepOutWords} and look for another.");
            else if (anyAligned)
                _ui.SetHint("Close! Trace the letter you see again, right over its shape.");
            else if (anyNear)
                _ui.SetHint($"Almost lined up! {StepOutWords}, take a tiny step, and look again.");
            else
                _ui.SetHint($"No letter lines up from here. {StepOutWords} and keep looking!");
        }

        /// <summary>
        /// The trace, mapped into the letter's own units through the same fit
        /// that measured the alignment, then graded by the shared letter
        /// grader. It must also sit ON the letter: a well-formed trace drawn
        /// somewhere else on the screen does not count.
        /// </summary>
        float ScoreAgainst(char letter, Vector2 offset, float scale, Vector3 forward, Vector3 right, Vector3 up)
        {
            var units = new List<Vector2>(_traceDirections.Count);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            foreach (Vector3 direction in _traceDirections)
            {
                float z = Vector3.Dot(direction, forward);
                if (z <= 0.01f) continue;
                var tangent = new Vector2(Vector3.Dot(direction, right) / z, Vector3.Dot(direction, up) / z);
                Vector2 unit = (tangent - offset) / scale;
                units.Add(unit);
                min = Vector2.Min(min, unit);
                max = Vector2.Max(max, unit);
            }
            if (units.Count < 8) return 0f;

            Vector2 center = (min + max) * 0.5f;
            Vector2 extent = max - min;
            if (center.magnitude > 0.45f) return 0f;                 // traced somewhere else
            if (Mathf.Max(extent.x, extent.y) < 0.35f) return 0f;    // a scribble, not a letter

            return LetterShapes.ScoreTrace(units, letter);
        }

        void Found(HiddenLetter hidden)
        {
            hidden.MarkFound();
            _found.Add(hidden.letter);
            _ui.SetFound(_found);
            _ui.Celebrate(hidden.letter);
            _ui.SetHint($"You found the letter {hidden.letter}!");
            _phase = Phase.Celebrating;
            _celebrateUntil = Time.time + 2f;
            _stroking = false;
            LetterFound?.Invoke(hidden.letter);
        }

        static bool Touch => PlayerControlScheme.Current == ControlSchemeKind.Touch;
        static string StepOutWords => Touch ? "Step out" : "Step out (C)";

        void UpdateWalkingHint()
        {
            if (!alignmentHints || _camera == null || Time.time < _nextHintCheck) return;
            _nextHintCheck = Time.time + 0.25f;

            Vector3 eye = _camera.transform.position;
            float best = float.PositiveInfinity;
            foreach (HiddenLetter hidden in _letters)
            {
                if (hidden == null || hidden.IsFound || !hidden.IsBuilt) continue;
                if ((hidden.EyePoint - eye).sqrMagnitude > hidden.CenterDistance * hidden.CenterDistance) continue;
                Vector3 viewport = _camera.WorldToViewportPoint(hidden.Center);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
                best = Mathf.Min(best, hidden.Misalignment(eye));
            }

            string key = Touch ? "Lock in" : "C";
            if (best <= alignTolerance)
                _ui.SetHint($"Something lines up here... press {key} to lock in!");
            else if (best <= alignTolerance * 3f)
                _ui.SetHint("Something is starting to line up...");
            else
                _ui.SetHint("");
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        bool PointerOverUi()
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            foreach (RaycastResult hit in hits)
                if (hit.gameObject.GetComponentInParent<TouchControls>() == null) return true;
            return false;
        }

        /// <summary>
        /// UiMode freezes keyboard and mouse look, not the on-screen joystick
        /// and look pad, so on touch those nap while the view is locked (the
        /// same trick the creature capture uses).
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

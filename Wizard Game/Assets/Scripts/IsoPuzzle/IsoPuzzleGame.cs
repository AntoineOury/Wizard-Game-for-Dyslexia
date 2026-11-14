using System.Collections;
using System.Collections.Generic;
using OtherwiseLabs.CreatureGame;
using UnityEngine;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// Iso Puzzle: a standalone roguelike perspective puzzle. One floating
    /// room at a time; a letter is hidden in each room as scattered
    /// ProBuilder shapes that only line up from one angle. Turn the room
    /// until the letter appears; the moment the player lets go close enough,
    /// the view locks in, the photo is taken, and the player traces the
    /// letter on it. Clear a room, a new random one rises in its place.
    ///
    /// Three hearts. A failed trace costs one. Clearing a room sometimes
    /// drops a health potion as loot, used on the spot (no inventory, so with
    /// full hearts it fizzles). Out of hearts ends the run; the score is how
    /// many rooms were cleared.
    ///
    /// Everything (camera, light, UI, rooms) is created at run time, so the
    /// scene only needs one GameObject with this component.
    /// </summary>
    public class IsoPuzzleGame : MonoBehaviour
    {
        enum State { Title, Exploring, LockingIn, Tracing, Cleared, Transition, GameOver }

        [Header("Run")]
        [Tooltip("Fixed seed for repeatable runs while testing; 0 = a new run every time.")]
        public int seed = 0;

        [Header("Alignment")]
        [Tooltip("Degrees off the letter's view that still counts as lined up, in the first room.")]
        public float startTolerance = 7f;
        [Tooltip("Tolerance never gets tighter than this, however deep the run.")]
        public float minTolerance = 4f;
        [Tooltip("Letting go within this many times the tolerance locks the view onto the letter.")]
        public float lockInFactor = 1.6f;
        [Tooltip("Rooms before the letter can also hide above or below (tipping the view up/down).")]
        public int flatRooms = 3;

        [Header("Tracing")]
        [Range(0.2f, 0.95f)] public float traceAccuracyThreshold = 0.6f;
        [Tooltip("Idle seconds after the last stroke before the trace is graded (lets multi-stroke letters finish).")]
        public float evaluateDelay = 1.4f;

        [Header("Mistakes")]
        public bool failedTraceCostsHeart = true;

        [Header("Potion loot (rolled after each cleared room)")]
        [Range(0f, 1f)] public float potionChance = 0.22f;
        [Range(0f, 1f)] public float potionChanceWhenHurt = 0.45f;

        static readonly Color InkColor = new Color(0.12f, 0.1f, 0.3f, 0.95f);
        static readonly Color GuideColor = new Color(0.45f, 0.7f, 1f, 0.9f);
        const string BestKey = "IsoPuzzle.BestRooms";

        State _state;
        System.Random _rng;
        IsoOrbitCamera _orbit;
        IsoHud _hud;
        Light _sun;
        IsoRoom _room;
        int _hearts;
        int _roomsCleared;
        int _failedTraces;
        bool _lockArmed;   // false right after "Turn again", until the view leaves the letter
        readonly Queue<char> _recentLetters = new Queue<char>();

        // Tracing.
        RenderTexture _photo;
        Vector2 _letterViewport;       // where the letter's centre sits in the photo (0..1)
        Vector2 _unitPerViewport;      // viewport delta -> letter unit-box delta
        readonly List<Vector2> _drawn = new List<Vector2>();
        Vector2 _lastInk;
        bool _stroking;
        float _evaluateAt;

        float Tolerance => Mathf.Max(minTolerance, startTolerance - _roomsCleared * 0.4f);

        void Start()
        {
            _orbit = IsoOrbitCamera.Create();

            CreateLight();
            _hud = IsoHud.Create(transform);
            _hud.RetakeButton.onClick.AddListener(OnRetakePressed);
            _hud.PlayButton.onClick.AddListener(StartRun);
            _hud.AgainButton.onClick.AddListener(StartRun);

            // A slowly turning sample room behind the title card.
            _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            _room = BuildRoom();
            _orbit.SetView(_room.TargetYaw + 120f, IsoOrbitCamera.DefaultPitch);
            _orbit.Camera.backgroundColor = _room.Background;

            _hearts = IsoHud.MaxHearts;
            _hud.SetHearts(_hearts);
            _hud.SetScore(0);
            _hud.SetHint("");
            _hud.SetPlayingChrome(false);
            _hud.SetAlignment(0f, false, false);
            _hud.ShowTitle(true);
            _state = State.Title;
        }

        void CreateLight()
        {
            var go = new GameObject("Iso Sun");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1.15f;
            _sun.color = new Color(1f, 0.97f, 0.92f);
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.55f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.8f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.4f, 0.36f, 0.4f);
            RenderSettings.fog = false;
        }

        // ------------------------------------------------------------------
        // Run flow
        // ------------------------------------------------------------------

        void StartRun()
        {
            if (_state != State.Title && _state != State.GameOver) return;
            _hud.ShowTitle(false);
            _hud.HideGameOver();
            _hearts = IsoHud.MaxHearts;
            _roomsCleared = 0;
            _recentLetters.Clear();
            _hud.SetHearts(_hearts);
            _hud.SetScore(0);
            _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            StartCoroutine(SwapRoom());
        }

        IEnumerator SwapRoom()
        {
            _state = State.Transition;
            _orbit.InputEnabled = false;
            _hud.SetPlayingChrome(false);
            _hud.SetAlignment(0f, false, false);
            _hud.ShowPhoto(null);
            _hud.SetHint("");

            // Old room sinks away.
            if (_room != null && _room.Root != null)
            {
                Transform old = _room.Root;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
                {
                    float k = t * t;
                    old.localPosition = Vector3.down * (k * 6f);
                    old.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, k);
                    yield return null;
                }
                Destroy(old.gameObject);
            }

            _room = BuildRoom();
            _failedTraces = 0;
            _lockArmed = true;

            // Start well away from the answer.
            float offset = Mathf.Lerp(80f, 170f, (float)_rng.NextDouble()) * (_rng.NextDouble() < 0.5 ? -1f : 1f);
            float startPitch = _orbit.PitchLocked ? IsoOrbitCamera.DefaultPitch : Mathf.Lerp(20f, 60f, (float)_rng.NextDouble());
            _orbit.SetView(_room.TargetYaw + offset, startPitch);

            // New room rises in, background colour crossfades.
            Color fromBg = _orbit.Camera.backgroundColor;
            Transform root = _room.Root;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.55f)
            {
                float k = 1f - (1f - t) * (1f - t);
                root.localPosition = Vector3.down * ((1f - k) * 5f);
                root.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, k);
                _orbit.Camera.backgroundColor = Color.Lerp(fromBg, _room.Background, k);
                yield return null;
            }
            root.localPosition = Vector3.zero;
            root.localScale = Vector3.one;
            _orbit.Camera.backgroundColor = _room.Background;

            _state = State.Exploring;
            _orbit.InputEnabled = true;
            _hud.SetHint(_roomsCleared == 0
                ? "Drag to turn the room. Find the letter hiding in the shapes!"
                : (_orbit.PitchLocked ? "A new room. Find its letter!" : "A new room. Drag up and down too, this letter may hide above or below."));
        }

        IsoRoom BuildRoom()
        {
            bool flat = _roomsCleared < flatRooms;
            _orbit.PitchLocked = flat;
            var recipe = new IsoRoomRecipe
            {
                Letter = PickLetter(),
                TargetYaw = (float)_rng.NextDouble() * 360f,
                TargetPitch = flat ? IsoOrbitCamera.DefaultPitch : Mathf.Lerp(22f, 58f, (float)_rng.NextDouble()),
                PropCount = Mathf.Min(5 + _roomsCleared * 2, 20),
            };
            return IsoRoomBuilder.Build(recipe, _rng, transform);
        }

        char PickLetter()
        {
            for (int tries = 0; tries < 30; tries++)
            {
                char letter = (char)('A' + _rng.Next(26));
                if (_recentLetters.Contains(letter)) continue;
                _recentLetters.Enqueue(letter);
                while (_recentLetters.Count > 8) _recentLetters.Dequeue();
                return letter;
            }
            return (char)('A' + _rng.Next(26));
        }

        void Update()
        {
            switch (_state)
            {
                case State.Title:
                    // Idle spin behind the title card.
                    _orbit.SetView(_orbit.Yaw + Time.deltaTime * 12f, IsoOrbitCamera.DefaultPitch);
                    break;
                case State.Exploring:
                    UpdateExploring();
                    break;
                case State.Tracing:
                    UpdateTracing();
                    break;
            }
        }

        float LockRadius => Tolerance * lockInFactor;

        void UpdateExploring()
        {
            float error = _orbit.AngleTo(_room.TargetYaw, _room.TargetPitch);
            float closeness = Mathf.Clamp01(1f - error / 35f);
            _hud.SetAlignment(closeness, error <= LockRadius, true);

            if (!_lockArmed)
            {
                if (error > LockRadius * 1.5f) _lockArmed = true;
                return;
            }
            // Light lock-in: once the player lets go close enough, the room
            // clicks onto the exact view and the photo is taken by itself.
            if (!_orbit.IsHeld && !_orbit.IsEasing && error <= LockRadius)
                LockIn();
        }

        // ------------------------------------------------------------------
        // Lock in and take the photo
        // ------------------------------------------------------------------

        void LockIn()
        {
            _state = State.LockingIn;
            _orbit.InputEnabled = false;
            _orbit.CancelPress();
            _hud.SetAlignment(1f, true, true);
            _hud.SetHint("It lines up!");
            _orbit.EaseTo(_room.TargetYaw, _room.TargetPitch, 0.3f, () => StartCoroutine(TakePhoto()));
        }

        IEnumerator TakePhoto()
        {
            yield return new WaitForSeconds(0.15f); // a beat on the locked view, then click
            Camera cam = _orbit.Camera;

            if (_photo == null || _photo.width != Screen.width || _photo.height != Screen.height)
            {
                if (_photo != null) _photo.Release();
                _photo = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 24);
            }
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = _photo;
            cam.Render();
            cam.targetTexture = previous;

            // Map viewport positions on the photo to the letter's unit box.
            Vector3 c = cam.WorldToViewportPoint(_room.LetterCenter);
            _letterViewport = new Vector2(c.x, c.y);
            float viewHeight = cam.orthographicSize * 2f;
            _unitPerViewport = new Vector2(viewHeight * cam.aspect / _room.LetterHeight, viewHeight / _room.LetterHeight);

            _hud.Flash(Color.white, 1f);
            _hud.ShowPhoto(_photo);
            _hud.SetAlignment(0f, false, false);
            _hud.SetPlayingChrome(true);
            _hud.SetHint("Got it! Now trace the letter on the photo.");
            if (_failedTraces > 0) ShowGuide();
            _drawn.Clear();
            _stroking = false;
            _evaluateAt = 0f;
            _state = State.Tracing;
        }

        void OnRetakePressed()
        {
            if (_state != State.Tracing) return;
            _hud.ShowPhoto(null);
            _hud.SetPlayingChrome(false);
            _hud.SetHint("Turn the room away, then line it up again when you're ready.");
            _lockArmed = false;
            _state = State.Exploring;
            _orbit.InputEnabled = true;
        }

        // ------------------------------------------------------------------
        // Tracing on the photo
        // ------------------------------------------------------------------

        void UpdateTracing()
        {
            Vector2 viewport = IsoPointer.Viewport;

            if (Input.GetMouseButtonDown(0) && !_orbit.PointerOverButton())
            {
                _stroking = true;
                _evaluateAt = 0f; // a new stroke pauses grading
                Ink(viewport, true);
            }
            else if (_stroking && Input.GetMouseButton(0))
            {
                Ink(viewport, false);
            }
            else if (_stroking && Input.GetMouseButtonUp(0))
            {
                _stroking = false;
                if (_drawn.Count >= 8) _evaluateAt = Time.time + evaluateDelay;
            }

            if (_evaluateAt > 0f && !_stroking && Time.time >= _evaluateAt)
                Evaluate();
        }

        void Ink(Vector2 viewport, bool force)
        {
            Vector2 unit = Vector2.Scale(viewport - _letterViewport, _unitPerViewport);
            if (Mathf.Abs(unit.x) > 1.2f || Mathf.Abs(unit.y) > 1.2f) return; // far off the letter
            if (!force && (unit - _lastInk).sqrMagnitude < 0.0008f) return;
            _lastInk = unit;
            _drawn.Add(unit);
            _hud.Dot(viewport, 22f, InkColor);
        }

        void Evaluate()
        {
            _evaluateAt = 0f;
            float accuracy = LetterShapes.ScoreTrace(_drawn, _room.Letter);
            if (accuracy >= traceAccuracyThreshold)
            {
                StartCoroutine(RoomCleared());
                return;
            }

            _failedTraces++;
            _drawn.Clear();
            _hud.ClearTrace();
            ShowGuide();
            if (failedTraceCostsHeart)
            {
                _hud.SetHint($"Not quite ({Mathf.RoundToInt(accuracy * 100f)}%). Follow the blue dots and try again.");
                LoseHeart();
            }
            else
            {
                _hud.SetHint($"Not quite ({Mathf.RoundToInt(accuracy * 100f)}%). Follow the blue dots and try again.");
            }
        }

        /// <summary>After a miss, dots show how the letter is formed, right over the shapes.</summary>
        void ShowGuide()
        {
            foreach (Vector2 p in LetterShapes.SamplePath(_room.Letter, 0.07f))
            {
                Vector2 viewport = _letterViewport + new Vector2(p.x / _unitPerViewport.x, p.y / _unitPerViewport.y);
                _hud.Dot(viewport, 16f, GuideColor);
            }
        }

        IEnumerator RoomCleared()
        {
            _state = State.Cleared;
            _roomsCleared++;
            _hud.SetScore(_roomsCleared);
            _hud.Flash(new Color(1f, 0.85f, 0.35f), 0.6f);
            _hud.SetPlayingChrome(false);
            _hud.SetAlignment(0f, false, false);
            _hud.SetHint($"Yes! That's the letter {_room.Letter}!");
            yield return new WaitForSeconds(1.3f);

            // Back to the live room (same view) so the loot can pop out of it.
            _hud.ShowPhoto(null);
            float chance = _hearts < IsoHud.MaxHearts ? potionChanceWhenHurt : potionChance;
            if (_rng.NextDouble() < chance)
                yield return LootDrop();

            yield return new WaitForSeconds(0.3f);
            StartCoroutine(SwapRoom());
        }

        /// <summary>
        /// The potion pops out of the solved letter, hangs for a moment, then
        /// flies into the hearts (+1). Potions can't be kept, so with full
        /// hearts it fizzles away instead.
        /// </summary>
        IEnumerator LootDrop()
        {
            Camera cam = _orbit.Camera;
            IsoPotion potion = IsoPotion.Create(transform, Vector3.zero);
            potion.Idle = false;
            Transform t = potion.transform;
            Vector3 start = _room.LetterCenter;
            Vector3 top = start + Vector3.up * 0.9f;
            const float size = 1.6f;
            _hud.SetHint("Loot drop! A health potion!");

            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.5f)
            {
                float pop = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f; // overshoot
                t.position = Vector3.Lerp(start, top, Mathf.SmoothStep(0f, 1f, k));
                t.localScale = Vector3.one * (size * Mathf.Clamp01(k * 1.6f) * pop);
                t.rotation = Quaternion.Euler(0f, k * 360f, 0f);
                yield return null;
            }
            t.position = top;
            t.localScale = Vector3.one * size;

            for (float k = 0f; k < 0.6f; k += Time.deltaTime)
            {
                t.position = top + Vector3.up * (Mathf.Sin(k * 6f) * 0.12f);
                t.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                yield return null;
            }

            if (_hearts >= IsoHud.MaxHearts)
            {
                _hud.SetHint("Your hearts are full, and potions can't be saved. It fizzles away.");
                potion.Drink();
                yield return new WaitForSeconds(1.2f);
                yield break;
            }

            // Fly to the heart row in the top-left corner.
            Vector2 hv = _hud.HeartsViewport;
            Vector3 target = cam.ViewportToWorldPoint(new Vector3(hv.x, hv.y, cam.nearClipPlane + 5f));
            Vector3 from = t.position;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.55f)
            {
                float e = k * k;
                t.position = Vector3.Lerp(from, target, e);
                t.localScale = Vector3.one * Mathf.Lerp(size, size * 0.35f, e);
                yield return null;
            }
            Destroy(potion.gameObject);
            _hearts = Mathf.Min(IsoHud.MaxHearts, _hearts + 1);
            _hud.SetHearts(_hearts);
            _hud.Flash(new Color(1f, 0.55f, 0.7f), 0.45f);
            _hud.SetHint("Glug! One heart back.");
            yield return new WaitForSeconds(0.8f);
        }

        void LoseHeart()
        {
            _hearts = Mathf.Max(0, _hearts - 1);
            _hud.SetHearts(_hearts);
            _hud.ShakeHearts();
            _hud.Flash(new Color(0.9f, 0.2f, 0.25f), 0.35f);
            if (_hearts == 0) StartCoroutine(GameOver());
        }

        IEnumerator GameOver()
        {
            _state = State.GameOver;
            _orbit.InputEnabled = false;
            _hud.SetPlayingChrome(false);
            _hud.SetAlignment(0f, false, false);
            int best = Mathf.Max(_roomsCleared, PlayerPrefs.GetInt(BestKey, 0));
            PlayerPrefs.SetInt(BestKey, best);
            PlayerPrefs.Save();
            yield return new WaitForSeconds(1.1f);
            _hud.ShowPhoto(null);
            _hud.SetHint("");
            _hud.ShowGameOver(_roomsCleared, best);
        }

        void OnDestroy()
        {
            if (_photo != null) _photo.Release();
        }
    }
}

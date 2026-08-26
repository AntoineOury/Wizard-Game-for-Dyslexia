using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The letter-creature mini-game, in one scene component: spawns roaming
    /// creatures, runs the call / trap / trace capture loop, and builds its own
    /// UI at runtime (the TouchControls recipe — nothing else to author).
    ///
    /// Deliberately self-contained: the world is read only through physics
    /// raycasts and one look-up of the water surface, the player is found by
    /// their CharacterController, and the only touch-point with the control
    /// scripts is the public PlayerControlScheme.UiMode flag — the hook those
    /// scripts already expose for "a UI wants the cursor". Terrain, water,
    /// controllers and toggles are never referenced, so this can be dropped
    /// into any walkable scene.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Otherwise Labs/Creature Mini-Game")]
    public class CreatureGameController : MonoBehaviour
    {
        [Header("Creatures")]
        [Tooltip("The letter-creatures roaming this world.")]
        public List<CreatureDefinition> creatures = new List<CreatureDefinition>();

        [Tooltip("Creatures live within this radius of this object. Keep it inside the playable terrain.")]
        [Min(20f)] public float spawnRadius = 130f;

        [Tooltip("Seconds before a captured creature's replacement wanders in.")]
        [Min(1f)] public float respawnDelay = 25f;

        [Header("Calling")]
        [Tooltip("How far a called letter carries, in meters. Creatures of that letter within range come to the player. Each creature type can override this with its own Call Response Radius.")]
        [Min(5f)] public float callRadius = 60f;

        [Tooltip("Seconds a called creature keeps approaching before losing interest.")]
        [Min(2f)] public float callDuration = 12f;

        [Header("Traps")]
        [Tooltip("Creatures notice a word paper bearing their letter from this far away.")]
        [Min(5f)] public float trapAttractRadius = 30f;

        [Tooltip("Creatures are shy: while the player stands within this range of a paper, they won't approach it. Lay the trap, then step back.")]
        [Min(0f)] public float playerShyRadius = 7f;

        [Tooltip("Papers on the ground at once. Placing more removes the oldest.")]
        [Range(1, 8)] public int maxActivePapers = 3;

        [Tooltip("How far from the player a paper can be placed.")]
        [Min(3f)] public float maxPlaceDistance = 25f;

        [Header("Capture")]
        [Tooltip("How close the player must stand to a stuck creature to trace it.")]
        [Min(1f)] public float interactRange = 4.5f;

        [Tooltip("Tracing accuracy (0-1) needed to capture. 0.6 is forgiving on purpose for young players.")]
        [Range(0.2f, 0.95f)] public float traceAccuracyThreshold = 0.6f;

        [Header("World")]
        [Tooltip("World Y of the water surface. Left at -10000 = auto-detected from a '-- Water --' object; scenes without water treat every habitat as dry land.")]
        public float waterSurfaceY = -10000f;

        public float WaterSurfaceY => waterSurfaceY;
        public bool HasWater => waterSurfaceY > -9999f;
        public Transform Player { get; private set; }

        /// <summary>The ear behind "call the creature's name" — see VoiceLetterListener.</summary>
        public VoiceLetterListener Voice { get; private set; }

        /// <summary>The in-world capture moment; the butterfly net reads its aim from here.</summary>
        public CaptureSequence Capture => _capture;

        CreatureGameUI _ui;
        CaptureSequence _capture;
        readonly List<LetterCreature> _alive = new List<LetterCreature>();
        readonly List<PendingSpawn> _pending = new List<PendingSpawn>();
        System.Random _rng;

        // Trap placement in progress: the chosen word waits for a ground tap.
        char _placingLetter;
        string _placingWord;
        bool _placing;
        bool _uiModeClaimed;

        struct PendingSpawn
        {
            public CreatureDefinition definition;
            public float time;
        }

        void Awake()
        {
            _rng = new System.Random();

            // The player rig is whatever carries the CharacterController — a
            // built-in component, so no dependency on the controller scripts.
            var characterController = FindObjectOfType<CharacterController>();
            if (characterController != null) Player = characterController.transform;

            if (!HasWater) waterSurfaceY = DetectWaterSurface();

            Voice = gameObject.AddComponent<VoiceLetterListener>();
            _capture = gameObject.AddComponent<CaptureSequence>();
            _capture.Init(this);
            _ui = CreatureGameUI.Create(this);
            AttachNetBrain();
        }

        /// <summary>
        /// The butterfly net is authored in the scene as any player child named
        /// like a net (e.g. "SM_Net"). If none carries a CaptureNet yet, give
        /// it the follow/visibility brain here so the art setup stays a pure
        /// drag-and-drop. Adding the component by hand instead exposes its
        /// tuning fields in the Inspector.
        /// </summary>
        void AttachNetBrain()
        {
            if (Player == null || FindObjectOfType<CaptureNet>(true) != null) return;
            foreach (Transform child in Player.GetComponentsInChildren<Transform>(true))
            {
                if (child == Player) continue;
                if (!child.name.ToLowerInvariant().Contains("net")) continue;
                child.gameObject.AddComponent<CaptureNet>();
                return;
            }
        }

        // Public entry points for scene-authored buttons (CreatureGameButton)
        // and the keyboard alike — the UI is an implementation detail.
        public void ToggleBooklet() => _ui.ToggleBooklet();
        public void OpenTrapFlow() => _ui.OpenTrapFlow();
        public void OpenCallFlow() => _ui.OpenCallFlow();

        /// <summary>
        /// Start the in-world capture on the nearest stuck creature — or, if
        /// the capture moment is already running, back out of it (the button
        /// works as a toggle for touch players).
        /// </summary>
        public void TryCaptureNearby()
        {
            if (_capture.IsActive) { _capture.Cancel(); return; }

            LetterCreature creature = NearestStuckInRange();
            if (creature != null) BeginCapture(creature);
            else _ui.Toast("No stuck creature close by — trap one first, then walk up to it!");
        }

        /// <summary>The first-person air-trace; the 2D panel remains only as a no-camera fallback.</summary>
        public void BeginCapture(LetterCreature creature)
        {
            if (Camera.main == null) { _ui.OpenTrace(creature); return; }
            _capture.Begin(creature);
        }

        void Start()
        {
            foreach (CreatureDefinition definition in creatures)
            {
                if (definition == null || definition.prefab == null) continue;
                for (int i = 0; i < definition.maxAlive; i++) Spawn(definition);
            }
        }

        void Update()
        {
            _alive.RemoveAll(c => c == null);
            RunPendingSpawns();
            HandleUiModeClaim();
            HandleKeys();
            HandlePointer();
            UpdateHint();
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        void HandleKeys()
        {
            // During the capture moment the sequence owns input (Esc backs out).
            if (_capture.IsActive) return;

            if (Input.GetKeyDown(KeyCode.B)) ToggleBooklet();
            if (_ui.AnyPanelOpen) return;

            if (Input.GetKeyDown(KeyCode.T)) OpenTrapFlow();
            if (Input.GetKeyDown(KeyCode.Q)) OpenCallFlow();
            if (Input.GetKeyDown(KeyCode.E))
            {
                LetterCreature creature = NearestStuckInRange();
                if (creature != null) BeginCapture(creature);
            }
        }

        void HandlePointer()
        {
            if (_capture.IsActive) return; // the sequence reads the pointer itself
            if (!Input.GetMouseButtonDown(0) || _ui.AnyPanelOpen) return;
            if (PointerOverBlockingUi()) return;
            Camera camera = Camera.main;
            if (camera == null) return;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);

            if (_placing)
            {
                // Ground only: solid geometry, triggers (creatures, papers) skipped.
                if (Physics.Raycast(ray, out RaycastHit ground, maxPlaceDistance * 2f, ~0, QueryTriggerInteraction.Ignore)
                    && Vector3.Distance(ground.point, Player != null ? Player.position : ground.point) <= maxPlaceDistance)
                {
                    PlacePaper(ground.point);
                }
                else
                {
                    _ui.Toast("Too far! Tap the ground closer to you.");
                }
                return;
            }

            // A tap on a stuck creature opens tracing — the touch-mode way in.
            if (Physics.Raycast(ray, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Collide))
            {
                var creature = hit.collider.GetComponentInParent<LetterCreature>();
                if (creature != null && creature.CurrentState == LetterCreature.State.Stuck
                    && WithinInteractRange(creature))
                {
                    BeginCapture(creature);
                }
            }
        }

        /// <summary>
        /// While any mini-game panel is open, gameplay input should pause and
        /// the cursor be free. UiMode is the flag the player controllers
        /// already honor for exactly that, so claim it — and release it only if
        /// we were the ones who set it.
        /// </summary>
        void HandleUiModeClaim()
        {
            if (_ui.AnyPanelOpen || _capture.IsActive)
            {
                PlayerControlScheme.UiMode = true;
                _uiModeClaimed = true;
            }
            else if (_uiModeClaimed)
            {
                PlayerControlScheme.UiMode = false;
                _uiModeClaimed = false;
            }
        }

        void UpdateHint()
        {
            if (_capture.IsActive) { _ui.SetHint(_capture.HintText); return; }
            if (_ui.AnyPanelOpen) { _ui.SetHint(""); return; }
            if (_placing)
            {
                _ui.SetHint($"Tap the ground to lay your \"{_placingWord}\" paper  •  Esc to cancel");
                if (Input.GetKeyDown(KeyCode.Escape)) CancelPlacement();
                return;
            }

            LetterCreature nearby = NearestStuckInRange();
            _ui.SetHint(nearby != null
                ? $"{nearby.Definition.DisplayName} is stuck! Press E (or Capture) to trace its letter"
                : "");
        }

        // ------------------------------------------------------------------
        // Capture loop, called by the UI
        // ------------------------------------------------------------------

        /// <summary>Word challenge passed — wait for the player to point at the ground.</summary>
        public void BeginTrapPlacement(char letter, string word)
        {
            _placingLetter = letter;
            _placingWord = word;
            _placing = true;
        }

        public void CancelPlacement()
        {
            _placing = false;
            _ui.SetHint("");
        }

        void PlacePaper(Vector3 point)
        {
            _placing = false;
            if (WordTrapPaper.ActiveCount >= maxActivePapers)
                WordTrapPaper.Oldest.Remove(freeCreature: true);

            WordTrapPaper paper = WordTrapPaper.Place(point, _placingLetter, _placingWord);

            // Teach the strength rule out loud: more of the letter = stickier.
            int hold = paper.HoldCount(_placingLetter);
            if (hold >= 2)
            {
                _ui.Toast($"Great bait! \"{paper.Word}\" is EXTRA sticky — Creature {_placingLetter} won't wriggle free.");
            }
            else if (hold == 1)
            {
                _ui.Toast($"\"{paper.Word}\" can hold Creature {_placingLetter}... loosely. Words with more {_placingLetter}'s grip tighter!");
            }
            else
            {
                var others = new List<string>();
                for (int i = 0; i < paper.EligibleLetters.Count && i < 3; i++)
                    others.Add(paper.EligibleLetters[i].ToString());
                _ui.Toast($"\"{paper.Word}\" has no {_placingLetter} at all! It can still catch Creature {string.Join(" or ", others)}.");
            }
        }

        /// <summary>Player picked a letter on the Call panel: shout it into the world.</summary>
        public void CallLetter(char letter)
        {
            if (Player == null) return;
            StartCoroutine(FloatingLetter(letter));

            int called = 0;
            foreach (LetterCreature creature in _alive)
            {
                if (creature == null || creature.Letter != letter) continue;
                float radius = creature.Definition.callResponseRadius > 0f
                    ? creature.Definition.callResponseRadius
                    : callRadius;
                if ((creature.transform.position - Player.position).sqrMagnitude > radius * radius) continue;

                Vector3 offset = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                creature.Lure(Player.position + offset, callDuration);
                called++;
            }

            _ui.Toast(called > 0
                ? $"Creature {letter} heard you! Here it comes..."
                : $"No Creature {letter} close enough to hear. Try nearer its home!");
        }

        /// <summary>Tracing succeeded: celebrate, record, respawn later.</summary>
        public void CompleteCapture(LetterCreature creature)
        {
            WordTrapPaper paper = creature.StuckOn;
            creature.BeginCapture();
            if (paper != null) paper.Remove(freeCreature: false);

            CaptureJournal.RecordCapture(creature.Letter);
            _ui.Toast($"You caught {creature.Definition.DisplayName}! It's in your book now.");

            _pending.Add(new PendingSpawn { definition = creature.Definition, time = Time.time + respawnDelay });
            StartCoroutine(FinishCapture(creature));
        }

        IEnumerator FinishCapture(LetterCreature creature)
        {
            yield return new WaitForSeconds(1.8f);
            if (creature != null) Destroy(creature.gameObject);
        }

        IEnumerator FloatingLetter(char letter)
        {
            var go = new GameObject($"Called {letter}");
            var text = go.AddComponent<TextMeshPro>();
            text.text = letter.ToString();
            text.fontSize = 14f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.95f, 0.4f);

            float life = 2f;
            for (float t = 0f; t < life; t += Time.deltaTime)
            {
                if (Player == null) break;
                go.transform.position = Player.position + Vector3.up * (2.4f + t * 0.9f);
                Camera camera = Camera.main;
                if (camera != null)
                    go.transform.rotation = Quaternion.LookRotation(go.transform.position - camera.transform.position);
                text.alpha = 1f - t / life;
                yield return null;
            }
            Destroy(go);
        }

        // ------------------------------------------------------------------
        // World queries for creatures
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the pointer sits over UI that should block a world tap.
        /// The touch scheme's own control surfaces (the invisible look area,
        /// the joystick) cover most of the screen but are NOT blocking UI —
        /// filtering them out is what lets taps place papers and pick
        /// creatures in touch mode. EventSystem.IsPointerOverGameObject was
        /// wrong twice here: with a mouse it counted the look surface (so the
        /// editor blocked placement), and on device its parameterless form
        /// misses touches entirely (so builds allowed it) — this raycast
        /// treats every pointer the same.
        /// </summary>
        public bool PointerOverBlockingUi()
        {
            if (EventSystem.current == null) return false;

            var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            foreach (RaycastResult hit in hits)
                if (hit.gameObject.GetComponentInParent<TouchControls>() == null) return true;
            return false;
        }

        /// <summary>True when the player stands within the given range of a point — the trap-shyness test.</summary>
        public bool PlayerIsNear(Vector3 point, float radius)
        {
            if (Player == null || radius <= 0f) return false;
            Vector3 flat = Player.position - point;
            flat.y = 0f;
            return flat.sqrMagnitude < radius * radius;
        }

        /// <summary>Ground height by raycast — solid geometry only, so any scene with colliders works.</summary>
        public bool SampleGround(float x, float z, out float groundY)
        {
            var origin = new Vector3(x, transform.position.y + 120f, z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }

        /// <summary>A habitat-appropriate wander destination near the creature.</summary>
        public Vector3 PickWanderPoint(CreatureDefinition definition, Vector3 from)
        {
            // Water dwellers take an occasional stroll up the beach — that shore
            // excursion is the window for catching them on dry land.
            bool shoreTrip = definition.habitat == CreatureHabitat.Water && Random.value < 0.3f;

            for (int attempt = 0; attempt < 14; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized * Random.Range(6f, 22f);
                var candidate = new Vector3(from.x + direction.x, 0f, from.z + direction.y);

                // Stay inside the play area.
                Vector3 fromCenter = candidate - transform.position;
                fromCenter.y = 0f;
                if (fromCenter.magnitude > spawnRadius)
                    candidate = transform.position + fromCenter.normalized * (spawnRadius * 0.95f);

                if (!SampleGround(candidate.x, candidate.z, out float groundY)) continue;
                if (!MatchesHabitat(definition.habitat, groundY, shoreTrip)) continue;

                candidate.y = groundY;
                return candidate;
            }
            return from;
        }

        bool MatchesHabitat(CreatureHabitat habitat, float groundY, bool shoreTrip)
        {
            if (!HasWater) return true; // dry scene: everywhere counts as land

            switch (habitat)
            {
                case CreatureHabitat.Water:
                    return shoreTrip
                        ? groundY > waterSurfaceY - 0.4f && groundY < waterSurfaceY + 1.4f
                        : groundY < waterSurfaceY - 0.3f;
                case CreatureHabitat.Shore:
                    return groundY > waterSurfaceY - 0.3f && groundY < waterSurfaceY + 2f;
                default:
                    return groundY > waterSurfaceY + 1f;
            }
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        void Spawn(CreatureDefinition definition)
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle * spawnRadius;
                var candidate = new Vector3(transform.position.x + direction.x, 0f, transform.position.z + direction.y);
                if (!SampleGround(candidate.x, candidate.z, out float groundY)) continue;
                if (!MatchesHabitat(definition.habitat, groundY, shoreTrip: false) && attempt < 20) continue;

                candidate.y = groundY;
                GameObject instance = Instantiate(definition.prefab, candidate, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                instance.name = definition.DisplayName;
                instance.transform.localScale *= definition.modelScale;
                instance.transform.SetParent(transform, true);

                var creature = instance.AddComponent<LetterCreature>();
                creature.Setup(definition, this);
                _alive.Add(creature);
                return;
            }
        }

        void RunPendingSpawns()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].time) continue;
                Spawn(_pending[i].definition);
                _pending.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------

        LetterCreature NearestStuckInRange()
        {
            if (Player == null) return null;
            LetterCreature best = null;
            float bestSqr = interactRange * interactRange;
            foreach (LetterCreature creature in _alive)
            {
                if (creature == null || creature.CurrentState != LetterCreature.State.Stuck) continue;
                float sqr = (creature.transform.position - Player.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = creature;
                }
            }
            return best;
        }

        bool WithinInteractRange(LetterCreature creature)
        {
            if (Player == null) return false;
            return (creature.transform.position - Player.position).sqrMagnitude
                   <= interactRange * interactRange * 2.25f; // a little generous for taps
        }

        /// <summary>
        /// Finds the finite terrain's water sheet by its well-known object name.
        /// A name lookup keeps this module free of any terrain-code reference;
        /// scenes without water simply report none.
        /// </summary>
        float DetectWaterSurface()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != "-- Water --") continue;
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    if (!child.gameObject.activeInHierarchy) continue;
                    return child.position.y + filter.sharedMesh.bounds.center.y;
                }
            }
            return -10000f;
        }

        public System.Random Rng => _rng;
    }
}

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using OtherwiseLabs.BrewingGame;
using OtherwiseLabs.CreatureGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// The gem-stencil mini-game in the alchemist store. A scroll unrolls on
    /// the gem table with a letter inked on it and an empty ring on each of
    /// the letter's vertices (W has five). The player sets a gem of the
    /// round's color in every ring; when the last one lands the gems glow and
    /// say the letter's sound, then fuse into a GLOWING LETTER. The player
    /// carries it to the mortar on the other table and drops it in, then SAYS
    /// the letter's name ("double u"). Said right, the mortar grinds it into a
    /// letter vial that flies onto the potion shelf (<see cref="VialInventory"/>)
    /// — the cauldron's ingredient for brewing trap words.
    ///
    /// The chain: gem stencil makes VIALS (letters) -> the cauldron brews vials
    /// into PAPERS (words) -> papers are spent as traps on hunting trips.
    ///
    /// Everything binds to the placed store pieces at runtime (table, mortar,
    /// shelf); the scroll is built from primitives and no prefab is modified.
    /// </summary>
    public class GemStencilController : MonoBehaviour
    {
        [Header("Store pieces (scene objects)")]
        [Tooltip("The table the stencil scroll unrolls on.")]
        public Transform gemTable;
        [Tooltip("The mortar on the other table: drop the glowing letter in here.")]
        public Transform mortar;
        [Tooltip("Shelf the finished vial flies to. Empty = the brewing game's shelf, or any object named \"shelf\".")]
        public Transform vialShelf;
        [Tooltip("The voice that says the letter (shared speaker component).")]
        public LetterSpeaker speaker;
        [Tooltip("The ear that hears the player say the letter. Added automatically when empty.")]
        public VoiceLetterListener voice;

        [Header("Pieces to spawn")]
        [Tooltip("Crystal models used as gems; one model per round, so all gems in a round share a color.")]
        public GameObject[] gemPrefabs;
        [Tooltip("Potion model for the finished vial.")]
        public GameObject vialPrefab;

        [Header("Layout")]
        [Tooltip("World direction the letter's TOP points along the tabletop.")]
        public Vector3 letterUpWorld = new Vector3(1f, 0f, 0f);
        [Tooltip("How much of the table's short side the letter uses.")]
        [Range(0.3f, 0.9f)] public float tableAreaScale = 0.62f;
        [Tooltip("Gem size after normalization, in meters (its largest side).")]
        [Range(0.03f, 0.25f)] public float gemSize = 0.09f;

        [Header("Rules & feel")]
        [Tooltip("How close (in letter widths) a dropped gem must land to a ring to snap in.")]
        [Range(0.05f, 0.3f)] public float snapRadius = 0.15f;
        [Tooltip("Max distance for touching gems, the letter and the mortar.")]
        public float interactRange = 6f;
        [Tooltip("Letters offered first while the player has no vials of them (the creature letters).")]
        public string priorityLetters = "SW";
        [Tooltip("Offer tap-the-letter buttons at the mortar when there is no microphone, or after a wait.")]
        public bool allowTapFallback = true;
        [Tooltip("Seconds of listening before the tap-the-letter buttons appear anyway (loud rooms, shy speakers).")]
        [Range(2f, 30f)] public float tapFallbackDelay = 8f;

        // Table basis, shared with the scroll
        public float Side { get; private set; }
        public Vector3 UpAxis { get; private set; }
        Vector3 _center;
        Vector3 _right;
        float _topY;
        float _halfTableAlongUp;

        enum Phase { Starting, Placing, Fusing, LetterReady, Carrying, Saying, Grinding }

        Phase _phase = Phase.Starting;
        char _letter = 'W';
        int _round;
        Color _accent = Color.white;
        System.Random _rng;
        Transform _player;

        StencilScroll _scroll;
        readonly List<StencilGem> _gems = new List<StencilGem>();
        StencilGem[] _occupant = new StencilGem[0];
        GlowingLetter _glowing;
        Vector3 _mortarMouth;

        // Input
        StencilGem _held;
        StencilGem _selected;
        Vector2 _pressScreen;
        float _pressTime;
        bool _pressOnLetter;
        bool _pressOnMortar;
        bool _pressOnTable;

        // Overlay
        CanvasGroup _hintGroup;
        TMP_Text _hint;
        TMP_Text _status;
        TMP_Text _toast;
        Coroutine _toastRoutine;
        RectTransform _choiceRow;
        Coroutine _fallbackRoutine;
        bool _claimedUiMode;

        void Start()
        {
            _rng = new System.Random();
            var cc = FindObjectOfType<CharacterController>();
            _player = cc != null ? cc.transform : null;

            if (gemTable == null) gemTable = FindByName("table (1)");
            if (mortar == null) mortar = FindByName("mortar");
            if (gemTable == null || mortar == null)
            {
                Debug.LogWarning("[GemGame] Gem table or mortar missing — the stencil game stays asleep.");
                enabled = false;
                return;
            }
            if (speaker == null) speaker = GetComponent<LetterSpeaker>();
            if (speaker == null) speaker = gameObject.AddComponent<LetterSpeaker>();
            if (voice == null) voice = GetComponent<VoiceLetterListener>();
            if (voice == null) voice = gameObject.AddComponent<VoiceLetterListener>();
            voice.LetterHeard += OnLetterHeard;

            BindTable();
            EnsureSolidCollider(gemTable);
            EnsureSolidCollider(mortar);
            Bounds mortarBounds = BoundsOf(mortar);
            _mortarMouth = new Vector3(mortarBounds.center.x, mortarBounds.max.y, mortarBounds.center.z);

            _scroll = new GameObject("Stencil Scroll").AddComponent<StencilScroll>();
            _scroll.transform.SetParent(transform, true);
            _scroll.Setup(this);

            BuildOverlay();
            StartCoroutine(NextRound(1f));
        }

        void OnDestroy()
        {
            if (voice != null)
            {
                voice.LetterHeard -= OnLetterHeard;
                voice.StopListening();
            }
            ReleaseUiMode();
        }

        Transform FindByName(string wanted)
        {
            wanted = wanted.ToLowerInvariant();
            foreach (Transform t in FindObjectsOfType<Transform>())
                if (t.name.ToLowerInvariant().Contains(wanted)) return t;
            return null;
        }

        void BindTable()
        {
            Bounds bounds = BoundsOf(gemTable);
            _topY = bounds.max.y;
            _center = new Vector3(bounds.center.x, _topY, bounds.center.z);
            Vector3 up = letterUpWorld;
            up.y = 0f;
            UpAxis = up.sqrMagnitude < 0.01f ? Vector3.right : up.normalized;
            _right = Vector3.Cross(Vector3.up, UpAxis).normalized;
            Side = Mathf.Min(bounds.size.x, bounds.size.z) * tableAreaScale;
            _halfTableAlongUp = Mathf.Abs(UpAxis.x) * bounds.extents.x + Mathf.Abs(UpAxis.z) * bounds.extents.z;
        }

        static Bounds BoundsOf(Transform piece)
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(piece.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void EnsureSolidCollider(Transform piece)
        {
            if (piece == null || piece.GetComponentInChildren<Collider>() != null) return;
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var box = piece.gameObject.AddComponent<BoxCollider>();
            box.center = piece.InverseTransformPoint(bounds.center);
            Vector3 size = piece.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        /// <summary>Letter-unit space (-0.5..0.5, y = the letter's up) to a point on the tabletop.</summary>
        public Vector3 UnitToWorld(Vector2 unit, float lift) =>
            _center + _right * (unit.x * Side) + UpAxis * (unit.y * Side) + Vector3.up * lift;

        Vector2 WorldToUnit(Vector3 world)
        {
            Vector3 flat = world - _center;
            return new Vector2(Vector3.Dot(flat, _right), Vector3.Dot(flat, UpAxis)) / Side;
        }

        // ------------------------------------------------------------------
        // Rounds

        IEnumerator NextRound(float delay)
        {
            _phase = Phase.Starting;
            yield return new WaitForSeconds(delay);

            _round++;
            _letter = PickLetter();
            _accent = Color.HSVToRGB(((_letter - 'A') * 7 % 26) / 26f, 0.55f, 1f);
            _scroll.SetLetter(_letter);
            SetHint("A new stencil is unrolling...");
            yield return _scroll.Unroll(0.9f);

            _occupant = new StencilGem[_scroll.Vertices.Count];
            SpawnGems(_scroll.Vertices.Count);
            _phase = Phase.Placing;
            SetHint($"Put a gem on every dot of the {_letter}!  Drag a gem onto a dot, or tap a dot.");
        }

        char PickLetter()
        {
            // Creature letters the player has no vials of yet come first:
            // the loop the store exists for.
            foreach (char priority in priorityLetters.ToUpperInvariant())
                if (priority >= 'A' && priority <= 'Z' && VialInventory.Count(priority) == 0 && priority != _letter)
                    return priority;

            char pick = _letter;
            for (int attempt = 0; attempt < 10 && pick == _letter; attempt++)
                pick = (char)('A' + _rng.Next(26));
            return pick;
        }

        void SpawnGems(int count)
        {
            foreach (StencilGem gem in _gems)
                if (gem != null) Destroy(gem.gameObject);
            _gems.Clear();
            _selected = null;

            GameObject prefab = (gemPrefabs != null && gemPrefabs.Length > 0)
                ? gemPrefabs[_round % gemPrefabs.Length]
                : null;

            float spacing = Mathf.Min(gemSize * 1.6f, Side * 1.1f / Mathf.Max(1, count));
            float spread = (count - 1) * 0.5f;
            // The gem row waits between the scroll's bottom roller and the table's edge.
            float scrollHalf = Side * 0.625f + gemSize * 0.5f;
            float rowDistance = Mathf.Max(scrollHalf + gemSize * 0.4f, (scrollHalf + _halfTableAlongUp) * 0.5f);
            for (int i = 0; i < count; i++)
            {
                GameObject go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Gem {i + 1}";
                Vector3 home = _center
                    - UpAxis * rowDistance
                    + _right * ((i - spread) * spacing)
                    + Vector3.up * (gemSize * 0.5f + 0.01f);
                var gem = go.AddComponent<StencilGem>();
                gem.Setup(home, _accent, gemSize);
                _gems.Add(gem);
            }
        }

        // ------------------------------------------------------------------
        // Placing gems on the vertices

        int NearestEmptyVertex(Vector2 unit, StencilGem allowOccupiedBy)
        {
            int best = -1;
            float bestDistance = snapRadius;
            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] != null && _occupant[i] != allowOccupiedBy) continue;
                float distance = Vector2.Distance(unit, _scroll.Vertices[i]);
                if (distance <= bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        void Place(StencilGem gem, int vertex)
        {
            if (gem.Vertex >= 0 && gem.Vertex < _occupant.Length && _occupant[gem.Vertex] == gem)
                _occupant[gem.Vertex] = null;
            _occupant[vertex] = gem;
            gem.PlaceAt(vertex, UnitToWorld(_scroll.Vertices[vertex], gemSize * 0.45f + 0.01f));
            if (_selected == gem) _selected = null;
            CheckComplete();
        }

        void Unplace(StencilGem gem)
        {
            if (gem.Vertex >= 0 && gem.Vertex < _occupant.Length && _occupant[gem.Vertex] == gem)
                _occupant[gem.Vertex] = null;
            gem.ReturnToPile();
        }

        StencilGem FirstPileGem()
        {
            if (_selected != null && _selected.CurrentState == StencilGem.State.Pile) return _selected;
            foreach (StencilGem gem in _gems)
                if (gem != null && gem.CurrentState == StencilGem.State.Pile) return gem;
            return null;
        }

        void Select(StencilGem gem)
        {
            if (_selected != null) _selected.SetSelected(false);
            _selected = gem;
            if (_selected != null) _selected.SetSelected(true);
        }

        void CheckComplete()
        {
            int filled = 0;
            foreach (StencilGem gem in _occupant) if (gem != null) filled++;
            if (filled < _occupant.Length)
            {
                SetHint($"{filled} of {_occupant.Length} dots have a gem.  Keep going!");
                return;
            }
            StartCoroutine(Fuse());
        }

        /// <summary>Every dot holds a gem: glow, say the letter's sound, fuse into the glowing letter.</summary>
        IEnumerator Fuse()
        {
            _phase = Phase.Fusing;
            foreach (StencilGem gem in _occupant) gem.SetGlowing(true);
            _scroll.SetGlow(true, _accent);
            SetHint($"The gems make a {_letter}!");
            yield return new WaitForSeconds(0.35f);

            Vector3 voiceAt = _center + Vector3.up * 0.4f;
            float duration = speaker.Speak(_letter, voiceAt, out _);
            yield return new WaitForSeconds(Mathf.Max(duration, 0.5f));
            float waited = 0f;
            while (speaker.IsBusy && waited < 2.5f) { waited += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.6f);

            Vector3 fusePoint = _center + Vector3.up * 0.45f;
            int arrived = 0;
            for (int i = 0; i < _occupant.Length; i++)
                _occupant[i].FlyTo(fusePoint, i * 0.08f, () => arrived++);
            int needed = _occupant.Length;
            _gems.Clear();
            while (arrived < needed) yield return null;

            _glowing = GlowingLetter.Create(_letter, fusePoint, _accent);
            _scroll.SetGlow(false, _accent);
            _phase = Phase.LetterReady;
            SetHint($"You made a glowing {_letter}!  Tap it to pick it up.");
        }

        // ------------------------------------------------------------------
        // The mortar: drop the letter in, say its name

        void PickUpLetter()
        {
            if (_glowing == null) return;
            _glowing.PickUp();
            _phase = Phase.Carrying;
            SetHint($"Carry the {_letter} to the mortar on the other table, and tap the mortar to drop it in.");
        }

        void DropLetter()
        {
            if (_glowing == null) return;
            _phase = Phase.Saying;
            // Start the ear right away, while the tap is fresh (browsers may ask
            // for the microphone here), so it is ready by the time the letter lands.
            if (voice.IsSupported) voice.StartListening(new[] { _letter });
            _glowing.DropInto(_mortarMouth, () =>
            {
                _glowing = null;
                if (_phase != Phase.Saying) return;   // heard already, mid-drop
                StartCoroutine(MortarPulse(0.4f, 0.06f));
                SetHint($"Now say the letter's name out loud: \"{NameOf(_letter)}\"!");
                if (allowTapFallback)
                    _fallbackRoutine = StartCoroutine(ShowChoicesAfter(voice.IsSupported ? tapFallbackDelay : 0f));
            });
        }

        void OnLetterHeard(char heard)
        {
            if (_phase != Phase.Saying || char.ToUpperInvariant(heard) != _letter) return;
            StartCoroutine(Grind());
        }

        IEnumerator ShowChoicesAfter(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (_phase != Phase.Saying) yield break;
            ShowChoices();
        }

        /// <summary>Tap-the-letter fallback: the right letter plus two look-alikes, shuffled.</summary>
        void ShowChoices()
        {
            foreach (Transform child in _choiceRow) Destroy(child.gameObject);
            var options = new List<char> { _letter };
            foreach (char lookalike in LookalikesOf(_letter))
                if (options.Count < 3 && !options.Contains(lookalike)) options.Add(lookalike);
            while (options.Count < 3)
            {
                char random = (char)('A' + _rng.Next(26));
                if (!options.Contains(random)) options.Add(random);
            }
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }

            for (int i = 0; i < options.Count; i++)
            {
                char option = options[i];
                var buttonGo = new GameObject($"Choice {option}", typeof(RectTransform));
                buttonGo.transform.SetParent(_choiceRow, false);
                var rect = (RectTransform)buttonGo.transform;
                Rect(rect, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 110f, 0f), new Vector2(90f, 90f));
                var image = buttonGo.AddComponent<Image>();
                image.color = new Color(0.16f, 0.12f, 0.24f, 0.88f);
                var button = buttonGo.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => OnChoice(option));

                TMP_Text glyph = MakeLabel(rect, 52f, new Color(1f, 0.92f, 0.6f, 1f));
                glyph.text = option.ToString();
                glyph.fontStyle = FontStyles.Bold;
                var glyphRect = glyph.rectTransform;
                glyphRect.anchorMin = Vector2.zero;
                glyphRect.anchorMax = Vector2.one;
                glyphRect.offsetMin = glyphRect.offsetMax = Vector2.zero;
            }
            _choiceRow.gameObject.SetActive(true);
            SetHint(voice.IsSupported
                ? $"Say \"{NameOf(_letter)}\" — or tap the letter you made."
                : "Which letter did you make? Tap it, and say its name as you do!");

            // Laptop mode locks the cursor; free it so the buttons can be clicked.
            if (!PlayerControlScheme.UiMode)
            {
                PlayerControlScheme.UiMode = true;
                _claimedUiMode = true;
            }
        }

        void OnChoice(char option)
        {
            if (_phase != Phase.Saying) return;
            if (option == _letter)
            {
                StartCoroutine(Grind());
                return;
            }
            Toast($"That one is {option}. Look at your letter again!");
        }

        static IEnumerable<char> LookalikesOf(char letter)
        {
            switch (letter)
            {
                case 'B': return "DPR";
                case 'D': return "BPO";
                case 'P': return "BRD";
                case 'Q': return "OGP";
                case 'M': return "WNH";
                case 'W': return "MVN";
                case 'N': return "MZH";
                case 'Z': return "NSK";
                case 'E': return "FB";
                case 'F': return "EP";
                case 'C': return "GO";
                case 'G': return "CQ";
                case 'O': return "QC";
                case 'U': return "VJ";
                case 'V': return "UY";
                case 'I': return "LT";
                case 'L': return "IJ";
                case 'J': return "LI";
                case 'S': return "ZC";
                default: return "";
            }
        }

        static string NameOf(char letter)
        {
            var forms = VoiceLetterListener.SpokenFormsFor(letter);
            return forms.Count > 1 ? forms[1] : letter.ToString();
        }

        /// <summary>Said right: the mortar grinds the letter into a vial that flies onto the shelf.</summary>
        IEnumerator Grind()
        {
            _phase = Phase.Grinding;
            if (_fallbackRoutine != null) StopCoroutine(_fallbackRoutine);
            voice.StopListening();
            _choiceRow.gameObject.SetActive(false);
            ReleaseUiMode();

            SetHint("Grinding...");
            StartCoroutine(MortarPulse(0.9f, 0.1f));
            speaker.Speak(_letter, _mortarMouth, out _);
            yield return LetterPuff();

            yield return VialToShelf();
            VialInventory.Add(_letter);
            Toast($"A {_letter} vial is on the shelf!  ({_letter} vials: {VialInventory.Count(_letter)})  Brew with it at the cauldron.");

            yield return _scroll.RollUp(0.6f);
            StartCoroutine(NextRound(0.8f));
        }

        IEnumerator MortarPulse(float seconds, float amount)
        {
            Vector3 baseScale = mortar.localScale;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                mortar.localScale = baseScale * (1f + amount * Mathf.Sin(Mathf.PI * (t / seconds)));
                yield return null;
            }
            mortar.localScale = baseScale;
        }

        IEnumerator LetterPuff()
        {
            var go = new GameObject($"Grind Puff {_letter}");
            go.transform.position = _mortarMouth + Vector3.up * 0.1f;
            var label = go.AddComponent<TextMeshPro>();
            label.text = _letter.ToString();
            label.fontSize = 3.4f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.Lerp(_accent, new Color(1f, 0.9f, 0.45f, 1f), 0.5f);
            label.rectTransform.sizeDelta = new Vector2(1.6f, 1.2f);
            go.AddComponent<BillboardLabel>();
            Vector3 start = go.transform.position;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float u = t / 1.2f;
                go.transform.position = start + Vector3.up * (0.6f * u);
                Color c = label.color;
                c.a = u < 0.12f ? u / 0.12f : Mathf.Clamp01(1f - (u - 0.55f) / 0.45f);
                label.color = c;
                yield return null;
            }
            Destroy(go);
        }

        IEnumerator VialToShelf()
        {
            GameObject vial = vialPrefab != null ? Instantiate(vialPrefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vial.name = $"Vial {_letter}";
            foreach (Collider collider in vial.GetComponentsInChildren<Collider>())
                Destroy(collider);

            // Normalize and tint like the brewing shelves do.
            var renderers = vial.GetComponentsInChildren<Renderer>();
            float height = 0.28f;
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.y > 0.001f) vial.transform.localScale *= height / bounds.size.y;
                Color soft = Color.Lerp(Color.white, _accent, 0.45f);
                var block = new MaterialPropertyBlock();
                foreach (Renderer renderer in renderers)
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", soft);
                    block.SetColor("_Color", soft);
                    renderer.SetPropertyBlock(block);
                }
            }
            float scale = vial.transform.localScale.x;

            var labelGo = new GameObject("Letter");
            labelGo.transform.SetParent(vial.transform, false);
            labelGo.transform.position = vial.transform.position + Vector3.up * (height + 0.12f);
            labelGo.transform.localScale = Vector3.one / Mathf.Max(scale, 0.0001f);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.text = _letter.ToString();
            label.fontSize = 1.6f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.Lerp(_accent, Color.white, 0.2f);
            label.outlineWidth = 0.22f;
            label.rectTransform.sizeDelta = new Vector2(1f, 0.6f);
            labelGo.AddComponent<BillboardLabel>();

            Vector3 start = _mortarMouth;
            Vector3 end = ShelfLandingPoint();
            Vector3 apex = Vector3.Lerp(start, end, 0.5f) + Vector3.up * Mathf.Max(1f, Vector3.Distance(start, end) * 0.35f);
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / 1.4f);
                vial.transform.position = Vector3.Lerp(Vector3.Lerp(start, apex, u), Vector3.Lerp(apex, end, u), u);
                vial.transform.Rotate(0f, 240f * Time.deltaTime, 0f, Space.World);
                yield return null;
            }
            vial.transform.position = end;
            vial.transform.rotation = Quaternion.identity;

            // Linger on the shelf so the player sees where it went, then join
            // the shelf's stock (the brewing game draws from VialInventory).
            yield return new WaitForSeconds(2.4f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                vial.transform.localScale = Vector3.one * (scale * (1f - t / 0.35f));
                yield return null;
            }
            Destroy(vial);
        }

        Vector3 ShelfLandingPoint()
        {
            if (vialShelf == null)
            {
                // Resolved late: the brewing game finds its shelves in its own Start.
                var brewing = FindObjectOfType<BrewingGameController>();
                if (brewing != null) vialShelf = brewing.shelfB != null ? brewing.shelfB : brewing.shelfA;
                if (vialShelf == null) vialShelf = FindByName("shelf");
            }
            if (vialShelf == null) return _mortarMouth + Vector3.up * 0.4f;
            Bounds bounds = BoundsOf(vialShelf);
            bool alongX = bounds.size.x >= bounds.size.z;
            float u = Mathf.Lerp(-0.6f, 0.6f, (float)_rng.NextDouble());
            Vector3 pos = bounds.center;
            if (alongX) pos.x += bounds.extents.x * u;
            else pos.z += bounds.extents.z * u;
            pos.y = bounds.max.y + 0.02f;
            return pos;
        }

        // ------------------------------------------------------------------
        // Input (same raycast recipe as the brewing game, TruePointer for the browser)

        void Update()
        {
            UpdateHintVisibility();
            if (_phase == Phase.Saying && _status != null)
                _status.text = voice.IsSupported ? voice.StatusReport : "";
            else if (_status != null && _status.text.Length > 0)
                _status.text = "";

            bool worldInput = _phase == Phase.Placing || _phase == Phase.LetterReady || _phase == Phase.Carrying;
            if (!worldInput)
            {
                if (_held != null) { Unplace(_held); _held = null; }
                return;
            }

            if (Input.GetMouseButtonDown(0) && !PointerOverBlockingUi())
                BeginPress();

            if (_held != null)
            {
                if (Input.GetMouseButton(0))
                {
                    var plane = new Plane(Vector3.up, new Vector3(0f, _topY + gemSize * 0.6f, 0f));
                    Ray ray = PointerRay();
                    if (plane.Raycast(ray, out float distance))
                        _held.UpdateHold(ray.GetPoint(distance));
                }
                if (Input.GetMouseButtonUp(0))
                    ReleaseHeld();
            }
            else if (Input.GetMouseButtonUp(0))
            {
                bool tap = IsTap();
                if (tap && _pressOnLetter) PickUpLetter();
                else if (tap && _pressOnMortar) DropLetter();
                else if (tap && _pressOnTable) TapTable();
                _pressOnLetter = _pressOnMortar = _pressOnTable = false;
            }
        }

        void BeginPress()
        {
            _pressScreen = PointerScreen();
            _pressTime = Time.unscaledTime;
            _pressOnLetter = _pressOnMortar = _pressOnTable = false;

            Ray ray = PointerRay();
            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 80f);

            switch (_phase)
            {
                case Phase.Placing:
                    StencilGem gem = hitSomething ? hit.collider.GetComponentInParent<StencilGem>() : null;
                    if (gem != null && PlayerWithin(gem.transform.position, interactRange)
                        && (gem.CurrentState == StencilGem.State.Pile || gem.CurrentState == StencilGem.State.Placed))
                    {
                        _held = gem;
                        gem.BeginHold();
                        return;
                    }
                    // Anything else on or over the table counts as a tap on the scroll.
                    if (!hitSomething || hit.transform.IsChildOf(gemTable))
                        _pressOnTable = PlayerWithin(_center, interactRange);
                    break;

                case Phase.LetterReady:
                    if (hitSomething && _glowing != null && hit.transform.IsChildOf(_glowing.transform))
                    {
                        if (PlayerWithin(_glowing.transform.position, interactRange)) _pressOnLetter = true;
                        else SetHint($"Step closer to the glowing {_letter} to pick it up.");
                    }
                    break;

                case Phase.Carrying:
                    if (hitSomething && hit.transform.IsChildOf(mortar))
                    {
                        if (PlayerWithin(mortar.position, interactRange)) _pressOnMortar = true;
                        else SetHint($"Walk closer to the mortar to drop the {_letter} in.");
                    }
                    break;
            }
        }

        void ReleaseHeld()
        {
            StencilGem gem = _held;
            _held = null;
            bool wasPlaced = gem.Vertex >= 0;

            if (IsTap())
            {
                if (wasPlaced)
                {
                    Unplace(gem);                      // tap a placed gem: back to the row
                    CheckComplete();
                }
                else
                {
                    gem.ReturnToPile();
                    Select(_selected == gem ? null : gem);   // tap a row gem: pick it, then tap a dot
                }
                return;
            }

            int vertex = NearestEmptyVertex(WorldToUnit(gem.transform.position), gem);
            if (vertex >= 0)
            {
                Place(gem, vertex);
            }
            else
            {
                Unplace(gem);
                if (wasPlaced) CheckComplete();
                else SetHint($"Drop the gem right on one of the dots of the {_letter}.");
            }
        }

        /// <summary>Tapped the scroll: the picked gem (or the next one in the row) hops onto the nearest empty dot.</summary>
        void TapTable()
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, _topY, 0f));
            Ray ray = PointerRay();
            if (!plane.Raycast(ray, out float distance)) return;
            int vertex = NearestEmptyVertex(WorldToUnit(ray.GetPoint(distance)), null);
            if (vertex < 0) return;
            StencilGem gem = FirstPileGem();
            if (gem != null) Place(gem, vertex);
        }

        bool IsTap() =>
            Time.unscaledTime - _pressTime < 0.32f &&
            (PointerScreen() - _pressScreen).magnitude < 22f;

        Vector2 PointerScreen() =>
            Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width / 2f, Screen.height / 2f)
                : (Vector2)Input.mousePosition;

        Ray PointerRay()
        {
            Camera cam = Camera.main;
            return cam != null ? TruePointer.Ray(cam) : new Ray(Vector3.zero, Vector3.forward);
        }

        bool PlayerWithin(Vector3 position, float range) =>
            _player == null || Vector3.Distance(_player.position, position) <= range;

        bool PointerOverBlockingUi()
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = PointerScreen() };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            foreach (RaycastResult hit in hits)
                if (hit.gameObject.GetComponentInParent<TouchControls>() == null) return true;
            return false;
        }

        void ReleaseUiMode()
        {
            if (!_claimedUiMode) return;
            PlayerControlScheme.UiMode = false;
            _claimedUiMode = false;
        }

        // ------------------------------------------------------------------
        // Overlay: one hint line (shown near the gem table or mortar), a voice
        // status line, a toast, and the tap-the-letter row when needed.

        void BuildOverlay()
        {
            var canvasGo = new GameObject("Gem Overlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 34;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();   // the choice buttons need it

            var hintRoot = new GameObject("Hint", typeof(RectTransform));
            hintRoot.transform.SetParent(canvasGo.transform, false);
            Rect((RectTransform)hintRoot.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1060f, 90f));
            _hintGroup = hintRoot.AddComponent<CanvasGroup>();
            _hintGroup.blocksRaycasts = false;
            _hint = MakeLabel(hintRoot.transform, 21f, new Color(1f, 1f, 1f, 0.95f));
            Rect(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(1060f, 60f));
            _status = MakeLabel(hintRoot.transform, 15f, new Color(0.8f, 0.9f, 1f, 0.9f));
            Rect(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -28f), new Vector2(1060f, 30f));

            _toast = MakeLabel(canvasGo.transform, 26f, new Color(1f, 0.96f, 0.75f, 0f));
            Rect(_toast.rectTransform, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1000f, 90f));

            var rowGo = new GameObject("Letter Choices", typeof(RectTransform));
            rowGo.transform.SetParent(canvasGo.transform, false);
            _choiceRow = (RectTransform)rowGo.transform;
            Rect(_choiceRow, new Vector2(0.5f, 0.42f), Vector2.zero, new Vector2(360f, 100f));
            rowGo.SetActive(false);
        }

        void UpdateHintVisibility()
        {
            if (_hintGroup == null) return;
            bool near = _phase == Phase.Carrying || _phase == Phase.Saying || _phase == Phase.Grinding
                        || PlayerWithin(_center, interactRange + 3f);
            _hintGroup.alpha = Mathf.MoveTowards(_hintGroup.alpha, near ? 1f : 0f, Time.deltaTime * 4f);
        }

        static TMP_Text MakeLabel(Transform parent, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.raycastTarget = false;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(20, 16, 30, 220);
            return text;
        }

        static void Rect(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        void SetHint(string text)
        {
            if (_hint != null) _hint.text = text;
        }

        void Toast(string message)
        {
            if (_toast == null) return;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine(message));
        }

        IEnumerator ToastRoutine(string message)
        {
            _toast.text = message;
            for (float t = 0f; t < 3.4f; t += Time.deltaTime)
            {
                float a = t < 0.25f ? t / 0.25f : Mathf.Clamp01(1f - (t - 2.6f) / 0.8f);
                Color c = _toast.color;
                c.a = a;
                _toast.color = c;
                yield return null;
            }
            _toast.text = "";
        }
    }
}

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
    /// The gem-constellation mini-game on the store's second table: small
    /// crystals of one color are laid out so that, connected by lines in
    /// placement order, they draw the target letter — like a constellation.
    /// Each placed stone lights one channel of the learning loop (READ the
    /// glyph, HEAR its sound, SAY it aloud, WRITE it with the lines). When
    /// the shape reads as the letter, the whole constellation glows; tap the
    /// mortar and the stones grind into a LETTER-POTION VIAL
    /// (<see cref="VialInventory"/>) — the ingredient the cauldron spends
    /// when brewing trap words. Stones -> vials -> words -> traps.
    ///
    /// Single-stroke letters only (one unbroken chain of lines can draw
    /// them); faint numbered sockets on the tabletop show one good layout,
    /// but any placement that draws the letter counts — the same best-fit,
    /// shape-based grader the tracing game uses. Everything binds to the
    /// placed store pieces at runtime; no prefab was modified.
    /// </summary>
    public class GemConstellationController : MonoBehaviour
    {
        [Header("Store pieces (scene objects)")]
        [Tooltip("The gem table the constellation is built on.")]
        public Transform gemTable;
        [Tooltip("The mortar that grinds a finished constellation into a vial.")]
        public Transform mortar;
        [Tooltip("The voice that says the letter (shared speaker component).")]
        public LetterSpeaker speaker;

        [Header("Pieces to spawn")]
        [Tooltip("Crystal models used as stones; one model per round (same color = one constellation).")]
        public GameObject[] stonePrefabs;
        [Tooltip("Potion model for the produced vial.")]
        public GameObject vialPrefab;

        [Header("Rules & feel")]
        [Tooltip("How close the constellation shape must match the letter (0..1).")]
        [Range(0.2f, 0.9f)] public float accuracyThreshold = 0.5f;
        [Tooltip("Show faint numbered spots suggesting one good layout.")]
        public bool showGuideSockets = true;
        [Tooltip("World direction the letter's TOP points along the tabletop.")]
        public Vector3 letterUpWorld = new Vector3(1f, 0f, 0f);
        [Tooltip("How much of the table's short side the letter uses.")]
        [Range(0.4f, 1f)] public float tableAreaScale = 0.8f;
        [Tooltip("Stone height after normalization, in meters.")]
        [Range(0.05f, 0.3f)] public float stoneHeight = 0.12f;
        [Tooltip("Max distance for grabbing stones / tapping the mortar.")]
        public float interactRange = 6f;
        [Tooltip("Letters offered first while the player has no vials of them (the creature letters).")]
        public string priorityLetters = "SW";

        // Table basis
        Vector3 _center;
        Vector3 _right, _up;
        float _side;
        float _topY;

        char _letter = 'S';
        int _round;
        bool _complete;
        bool _grinding;
        System.Random _rng;
        Transform _player;

        readonly List<ConstellationStone> _stones = new List<ConstellationStone>();
        readonly List<ConstellationStone> _placed = new List<ConstellationStone>();
        List<Vector2> _sockets = new List<Vector2>();
        Transform _guideRoot;
        Transform _targetDisplay;
        TMP_Text _targetGlyph;
        LineRenderer _line;
        Material _lineMaterial;
        Material _socketMaterial;
        Color _accent = Color.white;

        // Input
        ConstellationStone _held;
        Vector2 _pressScreen;
        float _pressTime;
        bool _pressOnMortar;
        bool _pressOnTarget;

        // Overlay
        TMP_Text _hint;
        TMP_Text _toast;
        Coroutine _toastRoutine;

        void Start()
        {
            _rng = new System.Random();
            var cc = FindObjectOfType<CharacterController>();
            _player = cc != null ? cc.transform : null;

            if (gemTable == null) gemTable = FindByName("table (1)");
            if (mortar == null) mortar = FindByName("mortar");
            if (gemTable == null || mortar == null)
            {
                Debug.LogWarning("[GemGame] Gem table or mortar missing — the constellation game stays asleep.");
                enabled = false;
                return;
            }

            if (speaker == null) speaker = GetComponent<LetterSpeaker>();
            if (speaker == null) speaker = gameObject.AddComponent<LetterSpeaker>();

            BindTable();
            EnsureSolidCollider(gemTable);
            EnsureSolidCollider(mortar);
            BuildLine();
            BuildOverlay();
            StartCoroutine(FirstRound());
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
            _up = letterUpWorld.sqrMagnitude < 0.01f ? Vector3.right : letterUpWorld.normalized;
            _up.y = 0f;
            _up.Normalize();
            _right = Vector3.Cross(Vector3.up, _up).normalized;
            _side = Mathf.Min(bounds.size.x, bounds.size.z) * tableAreaScale;
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

        Vector3 UnitToWorld(Vector2 unit) =>
            _center + _right * (unit.x * _side) + _up * (unit.y * _side) + Vector3.up * 0.015f;

        Vector2 WorldToUnit(Vector3 world)
        {
            Vector3 flat = world - _center;
            return new Vector2(Vector3.Dot(flat, _right), Vector3.Dot(flat, _up)) / _side;
        }

        // ------------------------------------------------------------------
        // Rounds

        IEnumerator FirstRound()
        {
            yield return new WaitForSeconds(1f);
            NextRound();
        }

        void NextRound()
        {
            _round++;
            _grinding = false;
            _complete = false;
            _letter = PickLetter();
            _accent = Color.HSVToRGB(((_letter - 'A') * 7 % 26) / 26f, 0.55f, 1f);
            BuildSockets();
            SpawnStones();
            BuildGuide();
            BuildTargetDisplay();
            UpdateLine();
            SetHint($"Lay the stones so the lines draw a {_letter}!  (follow the numbered spots — tap a stone to auto-place it)");
        }

        char PickLetter()
        {
            var pool = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++)
                if (LetterShapes.StrokeCount(c) == 1) pool.Add(c);

            // Creature letters the player has no vials of yet come first:
            // the loop the store exists for.
            foreach (char priority in priorityLetters.ToUpperInvariant())
                if (pool.Contains(priority) && VialInventory.Count(priority) == 0 && priority != _letter)
                    return priority;

            char pick = _letter;
            for (int attempt = 0; attempt < 10 && pick == _letter; attempt++)
                pick = pool[_rng.Next(pool.Count)];
            return pick;
        }

        void BuildSockets()
        {
            List<Vector2> dense = LetterShapes.SamplePath(_letter, 0.02f);
            float length = dense.Count * 0.02f;
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 0.28f), 4, 8);
            _sockets = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                int index = Mathf.RoundToInt(i * (dense.Count - 1) / (float)(count - 1));
                _sockets.Add(dense[index]);
            }
        }

        void SpawnStones()
        {
            foreach (ConstellationStone stone in _stones)
                if (stone != null) Destroy(stone.gameObject);
            _stones.Clear();
            _placed.Clear();

            GameObject prefab = (stonePrefabs != null && stonePrefabs.Length > 0)
                ? stonePrefabs[_round % stonePrefabs.Length]
                : null;

            for (int i = 0; i < _sockets.Count; i++)
            {
                GameObject go = prefab != null
                    ? Instantiate(prefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Stone {i + 1}";
                float spread = (_sockets.Count - 1) * 0.5f;
                Vector3 pile = _center
                    - _up * (_side * 0.72f)
                    + _right * ((i - spread) * 0.14f)
                    + Vector3.up * 0.015f;
                var stone = go.AddComponent<ConstellationStone>();
                stone.Setup(this, pile, _accent, stoneHeight);
                _stones.Add(stone);
            }
        }

        void BuildGuide()
        {
            if (_guideRoot != null) Destroy(_guideRoot.gameObject);
            if (!showGuideSockets) return;
            _guideRoot = new GameObject("Constellation Guide").transform;

            if (_socketMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _socketMaterial = new Material(shader);
            }
            _socketMaterial.color = new Color(_accent.r, _accent.g, _accent.b, 0.5f);

            for (int i = 0; i < _sockets.Count; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(dot.GetComponent<Collider>());
                dot.name = $"Socket {i + 1}";
                dot.transform.SetParent(_guideRoot, false);
                dot.transform.position = UnitToWorld(_sockets[i]) + Vector3.up * 0.002f;
                dot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                dot.transform.localScale = Vector3.one * 0.06f;
                dot.GetComponent<MeshRenderer>().sharedMaterial = _socketMaterial;

                var numberGo = new GameObject("Number");
                numberGo.transform.SetParent(_guideRoot, false);
                numberGo.transform.position = dot.transform.position + Vector3.up * 0.004f;
                numberGo.transform.rotation = Quaternion.LookRotation(Vector3.down, _up);
                var number = numberGo.AddComponent<TextMeshPro>();
                number.text = (i + 1).ToString();
                number.fontSize = 0.55f;
                number.alignment = TextAlignmentOptions.Center;
                number.color = new Color(_accent.r, _accent.g, _accent.b, 0.8f);
                number.rectTransform.sizeDelta = new Vector2(0.3f, 0.15f);
            }
        }

        void BuildTargetDisplay()
        {
            if (_targetDisplay == null)
            {
                _targetDisplay = new GameObject("Target Letter").transform;
                var glyphGo = new GameObject("Glyph");
                glyphGo.transform.SetParent(_targetDisplay, false);
                _targetGlyph = glyphGo.AddComponent<TextMeshPro>();
                _targetGlyph.fontSize = 4.5f;
                _targetGlyph.fontStyle = FontStyles.Bold;
                _targetGlyph.alignment = TextAlignmentOptions.Center;
                _targetGlyph.rectTransform.sizeDelta = new Vector2(2f, 1.6f);
                glyphGo.AddComponent<BillboardLabel>();
                var tap = _targetDisplay.gameObject.AddComponent<SphereCollider>();
                tap.radius = 0.35f;
                tap.isTrigger = true;
            }
            _targetDisplay.position = _center + Vector3.up * 0.85f;
            _targetGlyph.text = _letter.ToString();
            _targetGlyph.color = Color.Lerp(_accent, Color.white, 0.2f);
        }

        void BuildLine()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _lineMaterial = new Material(shader) { color = Color.white };
            var lineGo = new GameObject("Constellation Lines");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.material = _lineMaterial;
            _line.widthMultiplier = 0.016f;
            _line.numCapVertices = 3;
            _line.numCornerVertices = 3;
            _line.useWorldSpace = true;
            _line.positionCount = 0;
        }

        void UpdateLine()
        {
            if (_line == null) return;
            if (_placed.Count < 2)
            {
                _line.positionCount = 0;
                return;
            }
            _line.positionCount = _placed.Count;
            for (int i = 0; i < _placed.Count; i++)
                _line.SetPosition(i, _placed[i].transform.position + Vector3.up * 0.02f);
            Color color = _complete ? new Color(1f, 0.9f, 0.45f, 1f) : _accent;
            _lineMaterial.color = color;
        }

        void LateUpdate() => UpdateLine();   // stones bob and pulse; keep lines glued

        // ------------------------------------------------------------------
        // Placement, channels, evaluation

        void PlaceStone(ConstellationStone stone, Vector3 world)
        {
            Vector2 unit = WorldToUnit(world);
            unit.x = Mathf.Clamp(unit.x, -0.6f, 0.6f);
            unit.y = Mathf.Clamp(unit.y, -0.6f, 0.6f);

            bool isNew = stone.Order < 0;
            int order = isNew ? _placed.Count : stone.Order;
            stone.PlaceAt(UnitToWorld(unit), unit, order);
            if (isNew)
            {
                _placed.Add(stone);
                PlayChannel(order);
            }
            Evaluate();
        }

        void UnplaceStone(ConstellationStone stone)
        {
            if (stone.Order >= 0)
            {
                _placed.Remove(stone);
                for (int i = 0; i < _placed.Count; i++) _placed[i].SetOrder(i);
            }
            stone.ReturnToPile();
            _complete = false;
            foreach (ConstellationStone s in _stones) s.SetCelebrating(false);
            Evaluate();
        }

        /// <summary>
        /// Each placed stone lights one channel of the learning loop. Gentle
        /// flavor, never a gate: READ shows the glyph, HEAR says the sound,
        /// SAY invites saying it aloud, WRITE is the drawing itself.
        /// </summary>
        void PlayChannel(int order)
        {
            switch (order % 4)
            {
                case 0:
                    StartCoroutine(GlyphPop());
                    SetHint($"READ it: that's the letter {_letter}!");
                    break;
                case 1:
                    speaker.Speak(_letter, _center + Vector3.up * 0.6f, out _);
                    SetHint($"HEAR it: the book-voice says {_letter}!");
                    break;
                case 2:
                    SetHint($"SAY it out loud: \"{SoundOf(_letter)}\"!");
                    break;
                default:
                    SetHint($"WRITE it: connect the stones into a {_letter}!");
                    break;
            }
        }

        static string SoundOf(char letter)
        {
            var forms = VoiceLetterListener.SpokenFormsFor(letter);
            return forms.Count > 0 ? forms[forms.Count - 1] : letter.ToString();
        }

        IEnumerator GlyphPop()
        {
            var go = new GameObject("Read Glyph");
            go.transform.position = _center + Vector3.up * 0.5f;
            var label = go.AddComponent<TextMeshPro>();
            label.text = _letter.ToString();
            label.fontSize = 3f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = _accent;
            label.rectTransform.sizeDelta = new Vector2(1.5f, 1.2f);
            go.AddComponent<BillboardLabel>();
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float u = t / 1.2f;
                go.transform.position = _center + Vector3.up * (0.5f + 0.25f * u);
                Color c = label.color;
                c.a = u < 0.15f ? u / 0.15f : Mathf.Clamp01(1f - (u - 0.6f) / 0.4f);
                label.color = c;
                yield return null;
            }
            Destroy(go);
        }

        void Evaluate()
        {
            if (_placed.Count < _sockets.Count)
            {
                _complete = false;
                return;
            }

            var poly = new List<Vector2>();
            for (int i = 0; i < _placed.Count - 1; i++)
            {
                Vector2 a = _placed[i].TableUnit, b = _placed[i + 1].TableUnit;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.04f));
                for (int s = 0; s <= steps; s++)
                    poly.Add(Vector2.Lerp(a, b, s / (float)steps));
            }

            float score = LetterShapes.ScoreTrace(poly, _letter);
            _complete = score >= accuracyThreshold;
            foreach (ConstellationStone stone in _stones) stone.SetCelebrating(_complete);
            SetHint(_complete
                ? $"It really is a {_letter}! Tap the MORTAR to grind it into a potion vial."
                : $"Almost a {_letter} — {Mathf.RoundToInt(score * 100f)}%. Nudge the stones toward the glowing spots!");
        }

        // ------------------------------------------------------------------
        // The grind: constellation -> vial

        void Grind()
        {
            if (_grinding || !_complete) return;
            _grinding = true;
            StartCoroutine(GrindRoutine());
        }

        IEnumerator GrindRoutine()
        {
            SetHint("Grinding...");
            if (_guideRoot != null) Destroy(_guideRoot.gameObject);
            Bounds mortarBounds = BoundsOf(mortar);
            Vector3 mouth = new Vector3(mortarBounds.center.x, mortarBounds.max.y, mortarBounds.center.z);

            int arrived = 0;
            for (int i = 0; i < _placed.Count; i++)
                _placed[i].FlyTo(mouth, i * 0.12f, () => arrived++);
            int needed = _placed.Count;
            _stones.Clear();
            _placed.Clear();
            _line.positionCount = 0;

            while (arrived < needed) yield return null;

            StartCoroutine(MortarPulse());
            speaker.Speak(_letter, mouth, out _);
            yield return StartCoroutine(LetterPuff(mouth));

            VialInventory.Add(_letter);
            StartCoroutine(VialPopsOut(mouth));
            Toast($"You made a {_letter} potion vial!  ({_letter} vials: {VialInventory.Count(_letter)})  Brew it into a trap word at the cauldron.");
            yield return new WaitForSeconds(2.2f);
            NextRound();
        }

        IEnumerator MortarPulse()
        {
            Vector3 baseScale = mortar.localScale;
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                mortar.localScale = baseScale * (1f + 0.1f * Mathf.Sin(Mathf.PI * (t / 0.7f)));
                yield return null;
            }
            mortar.localScale = baseScale;
        }

        IEnumerator LetterPuff(Vector3 mouth)
        {
            var go = new GameObject($"Grind Puff {_letter}");
            go.transform.position = mouth + Vector3.up * 0.1f;
            var label = go.AddComponent<TextMeshPro>();
            label.text = _letter.ToString();
            label.fontSize = 3.4f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = _accent;
            label.rectTransform.sizeDelta = new Vector2(1.6f, 1.2f);
            go.AddComponent<BillboardLabel>();
            Vector3 start = go.transform.position;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                float u = t / 1.3f;
                go.transform.position = start + Vector3.up * (0.7f * u);
                Color c = label.color;
                c.a = u < 0.12f ? u / 0.12f : Mathf.Clamp01(1f - (u - 0.55f) / 0.45f);
                label.color = c;
                yield return null;
            }
            Destroy(go);
        }

        IEnumerator VialPopsOut(Vector3 mouth)
        {
            GameObject vial = vialPrefab != null
                ? Instantiate(vialPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vial.name = $"Vial {_letter}";
            foreach (Collider collider in vial.GetComponentsInChildren<Collider>())
                Destroy(collider);

            // Normalize and tint like the brewing shelves do.
            var renderers = vial.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.y > 0.001f) vial.transform.localScale *= 0.28f / bounds.size.y;
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

            var labelGo = new GameObject("Letter");
            labelGo.transform.SetParent(vial.transform, false);
            labelGo.transform.localPosition = Vector3.up * 0.3f;
            var label = labelGo.AddComponent<TextMeshPro>();
            label.text = _letter.ToString();
            label.fontSize = 1.6f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.Lerp(_accent, Color.white, 0.2f);
            label.rectTransform.sizeDelta = new Vector2(1f, 0.6f);
            labelGo.AddComponent<BillboardLabel>();

            Camera cam = Camera.main;
            Vector3 start = mouth;
            Vector3 apex = mouth + Vector3.up * 1f;
            Vector3 end = cam != null
                ? cam.transform.position + cam.transform.forward * 1.2f - Vector3.up * 0.15f
                : mouth + Vector3.up * 1.6f;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                float u = t / 1.5f;
                Vector3 a = Vector3.Lerp(start, apex, u);
                Vector3 b = Vector3.Lerp(apex, end, u);
                vial.transform.position = Vector3.Lerp(a, b, u);
                if (u > 0.78f) vial.transform.localScale *= 0.93f;
                yield return null;
            }
            Destroy(vial);
        }

        // ------------------------------------------------------------------
        // Input (same raycast recipe as the brewing game)

        void Update()
        {
            if (_grinding) { _held = null; return; }

            if (Input.GetMouseButtonDown(0) && !PointerOverBlockingUi())
                BeginPress();

            if (_held != null)
            {
                if (Input.GetMouseButton(0))
                {
                    Ray ray = PointerRay();
                    var plane = new Plane(Vector3.up, new Vector3(0f, _topY + 0.05f, 0f));
                    if (plane.Raycast(ray, out float distance))
                        _held.UpdateHold(ray.GetPoint(distance));
                }
                if (Input.GetMouseButtonUp(0))
                    ReleaseHeld();
            }
            else if (Input.GetMouseButtonUp(0))
            {
                if (_pressOnMortar && IsTap())
                {
                    if (_complete) Grind();
                    else if (_placed.Count > 0) SetHint($"Finish the {_letter} first — then the mortar can grind it!");
                }
                else if (_pressOnTarget && IsTap())
                {
                    RerollLetter();
                }
                _pressOnMortar = _pressOnTarget = false;
            }
        }

        void BeginPress()
        {
            _pressScreen = PointerScreen();
            _pressTime = Time.unscaledTime;
            _pressOnMortar = _pressOnTarget = false;

            Ray ray = PointerRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, 80f)) return;

            var stone = hit.collider.GetComponentInParent<ConstellationStone>();
            if (stone != null && PlayerWithin(stone.transform.position, interactRange)
                && (stone.CurrentState == ConstellationStone.State.Pile
                    || stone.CurrentState == ConstellationStone.State.Placed))
            {
                _held = stone;
                stone.BeginHold();
                return;
            }

            if (mortar != null && hit.transform.IsChildOf(mortar) && PlayerWithin(mortar.position, interactRange))
                _pressOnMortar = true;
            else if (_targetDisplay != null && hit.transform.IsChildOf(_targetDisplay)
                     && PlayerWithin(_targetDisplay.position, interactRange))
                _pressOnTarget = true;
        }

        void ReleaseHeld()
        {
            ConstellationStone stone = _held;
            _held = null;

            bool wasPlaced = stone.Order >= 0;
            if (IsTap() && !wasPlaced)
            {
                // Tap assist: a tapped pile-stone jumps to the next open socket.
                int next = _placed.Count;
                if (next < _sockets.Count)
                {
                    PlaceStone(stone, UnitToWorld(_sockets[next]));
                    return;
                }
            }
            if (IsTap() && wasPlaced)
            {
                UnplaceStone(stone);
                return;
            }

            Vector2 unit = WorldToUnit(stone.transform.position);
            bool onTable = Mathf.Abs(unit.x) <= 0.65f && Mathf.Abs(unit.y) <= 0.65f;
            if (onTable) PlaceStone(stone, stone.transform.position);
            else UnplaceStone(stone);
        }

        void RerollLetter()
        {
            foreach (ConstellationStone stone in _stones) stone.ReturnToPile();
            _placed.Clear();
            _complete = false;
            NextRound();
            Toast($"New letter: {_letter}!");
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
            return cam != null ? cam.ScreenPointToRay(PointerScreen()) : new Ray(Vector3.zero, Vector3.forward);
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

        // ------------------------------------------------------------------
        // Minimal overlay (one hint line + one toast, like the other games)

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

            _hint = MakeLabel(canvasGo.transform, 20f, new Color(1f, 1f, 1f, 0.95f));
            Rect(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(1060f, 60f));
            _toast = MakeLabel(canvasGo.transform, 26f, new Color(1f, 0.96f, 0.75f, 0f));
            Rect(_toast.rectTransform, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1000f, 90f));
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
            if (_hint != null && PlayerWithin(_center, interactRange + 4f)) _hint.text = text;
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

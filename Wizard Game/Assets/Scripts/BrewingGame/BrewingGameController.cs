using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using OtherwiseLabs.CreatureGame;

namespace OtherwiseLabs.BrewingGame
{
    /// <summary>
    /// The item-making mini-game in the alchemist store: the spell book on the
    /// table says letter sounds in a sequence (a real word from the shared
    /// WordBank), the player picks letter potions off the shelves and drops
    /// them into the cauldron in that order, and the cauldron puffs each added
    /// letter back out. Brew the word right and a word-trap paper lands in the
    /// satchel (<see cref="PaperInventory"/>) for later hunting trips; get the
    /// order wrong and the potion fizzles, the shelves refill, and the book
    /// happily says it again.
    ///
    /// Scene needs: this component plus references to the placed store pieces
    /// (book, cauldron/boiler, two shelves) and the potion prefabs to spawn.
    /// Everything else — colliders, labels, UI, audio — is built at runtime,
    /// so the third-party models stay untouched and the room can be
    /// redecorated freely in the editor. In-world interactions only; the sole
    /// screen overlay is one hint line and a toast, for immersion's sake.
    /// </summary>
    public class BrewingGameController : MonoBehaviour
    {
        [Header("Store pieces (scene objects)")]
        [Tooltip("The spell book that says the recipe. Tap it to hear the sequence (again).")]
        public Transform book;
        [Tooltip("The cauldron/boiler the ingredients go into.")]
        public Transform cauldron;
        [Tooltip("First shelf that displays letter potions.")]
        public Transform shelfA;
        [Tooltip("Second shelf for the rest of the potions.")]
        public Transform shelfB;
        [Tooltip("The book's voice. Lives on this same object.")]
        public LetterSpeaker speaker;

        [Header("Ingredients")]
        [Tooltip("Potion models to spawn as ingredients; variety is cosmetic.")]
        public GameObject[] potionPrefabs;
        [Tooltip("Tint each potion by its letter so bottles are tellable apart at a glance.")]
        public bool tintPotions = true;
        [Tooltip("Extra potions with letters that are NOT in the word.")]
        [Range(0, 5)] public int decoyCount = 2;

        [Header("Recipes")]
        [Tooltip("Word length of the first brews.")]
        [Range(2, 6)] public int startingLength = 3;
        [Tooltip("Longest word the book will ever ask for.")]
        [Range(3, 8)] public int maxLength = 5;
        [Tooltip("Successful brews needed before words grow one letter longer.")]
        [Range(1, 6)] public int brewsPerLengthUp = 2;

        [Header("Feel")]
        [Tooltip("Pause between spoken letters, on top of each letter's own length.")]
        [Range(0.1f, 1.5f)] public float gapBetweenLetters = 0.45f;
        [Tooltip("The book auto-reads the recipe when the player is this close (0 = only on tap).")]
        public float autoPlayRadius = 7f;
        [Tooltip("How far from the player a potion or the book can be tapped.")]
        public float interactRange = 6f;
        [Tooltip("Color of the letters puffed out by the cauldron.")]
        public Color puffColor = new Color(0.85f, 0.9f, 1f, 1f);
        [Tooltip("Show each letter over the book while it speaks, even when real audio plays. Always shown when there is no voice.")]
        public bool alwaysShowLetters = false;

        public BrewingCauldron Cauldron { get; private set; }

        enum Phase { Waiting, Speaking, Brewing, Resolving }

        Phase _phase = Phase.Waiting;
        Transform _player;
        System.Random _rng;
        string _word = "";
        readonly List<char> _poured = new List<char>();
        readonly List<PotionIngredient> _potions = new List<PotionIngredient>();
        readonly List<(char letter, Vector3 slot)> _layout = new List<(char, Vector3)>();
        int _successes;
        bool _heardOnce;

        // Input tracking
        PotionIngredient _held;
        float _holdDistance;
        Vector2 _pressScreen;
        float _pressTime;
        bool _pressOnBook;

        // Overlay
        TMP_Text _hint;
        TMP_Text _toast;
        Coroutine _toastRoutine;

        void Start()
        {
            _rng = new System.Random();
            var cc = FindObjectOfType<CharacterController>();
            _player = cc != null ? cc.transform : null;

            ResolveStorePieces();
            if (cauldron == null || book == null || shelfA == null)
            {
                Debug.LogWarning("[BrewingGame] Missing store pieces (book/cauldron/shelf) — the mini-game stays asleep in this scene.");
                enabled = false;
                return;
            }

            Cauldron = cauldron.GetComponent<BrewingCauldron>();
            if (Cauldron == null) Cauldron = cauldron.gameObject.AddComponent<BrewingCauldron>();
            Cauldron.Bind(puffColor);

            if (speaker == null) speaker = GetComponent<LetterSpeaker>();
            if (speaker == null) speaker = gameObject.AddComponent<LetterSpeaker>();

            EnsureTapCollider(book);
            EnsureTapCollider(shelfA);
            if (shelfB != null) EnsureTapCollider(shelfB);

            BuildOverlay();
            StartCoroutine(FirstRound());
        }

        /// <summary>Inspector references first; sleeves-rolled-up name search second.</summary>
        void ResolveStorePieces()
        {
            if (book == null) book = FindByName("locked_book", "book");
            if (cauldron == null) cauldron = FindByName("boiler", "cauldron");
            if (shelfA == null || shelfB == null)
            {
                var shelves = new List<Transform>();
                foreach (Transform t in FindObjectsOfType<Transform>())
                    if (t.name.ToLowerInvariant().Contains("shelf") && t.GetComponentInParent<PotionIngredient>() == null)
                        shelves.Add(t);
                shelves.Sort((a, b) => a.position.x.CompareTo(b.position.x));
                if (shelfA == null && shelves.Count > 0) shelfA = shelves[0];
                if (shelfB == null && shelves.Count > 1) shelfB = shelves[shelves.Count - 1];
            }
        }

        Transform FindByName(params string[] names)
        {
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                foreach (string wanted in names)
                    if (n.Contains(wanted)) return t;
            }
            return null;
        }

        static void EnsureTapCollider(Transform piece)
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

        // ------------------------------------------------------------------
        // Rounds

        IEnumerator FirstRound()
        {
            yield return new WaitForSeconds(1f);
            NextRound();
        }

        void NextRound()
        {
            int length = Mathf.Min(startingLength + _successes / Mathf.Max(1, brewsPerLengthUp), maxLength);
            _word = WordBank.PickWord(Mathf.Min(length, maxLength), length, _rng).ToUpperInvariant();
            _poured.Clear();
            _heardOnce = false;
            BuildShelfLayout();
            SpawnPotions();
            _phase = Phase.Waiting;
            SetHint("The spell book has a new recipe - tap it to listen!");

            if (autoPlayRadius > 0f && PlayerWithin(book.position, autoPlayRadius))
                StartCoroutine(SpeakSequence());
        }

        void BuildShelfLayout()
        {
            // The ingredients: one potion per letter occurrence, plus decoys,
            // shuffled and laid across the shelves' real widths.
            var letters = new List<char>(_word.ToCharArray());
            var pool = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++) if (_word.IndexOf(c) < 0) pool.Add(c);
            for (int i = 0; i < decoyCount && pool.Count > 0; i++)
            {
                int pick = _rng.Next(pool.Count);
                letters.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            for (int i = letters.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (letters[i], letters[j]) = (letters[j], letters[i]);
            }

            _layout.Clear();
            var slots = new List<Vector3>();
            CollectShelfSlots(shelfA, letters.Count, slots);
            if (shelfB != null) CollectShelfSlots(shelfB, letters.Count, slots);
            // Interleave across shelves by nearest count split
            int needed = letters.Count;
            if (slots.Count < needed)                        // degenerate shelves: line them up anyway
                for (int i = slots.Count; i < needed; i++)
                    slots.Add(shelfA.position + Vector3.up * 1.2f + Vector3.right * (0.4f * i));

            for (int i = 0; i < needed; i++)
                _layout.Add((letters[i], slots[i]));
        }

        void CollectShelfSlots(Transform shelf, int total, List<Vector3> slots)
        {
            if (shelf == null || slots.Count >= total) return;
            var renderers = shelf.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            bool alongX = bounds.size.x >= bounds.size.z;
            int here = Mathf.Min(total - slots.Count, Mathf.Max(1, (total + 1) / 2));
            for (int i = 0; i < here; i++)
            {
                float u = here == 1 ? 0.5f : i / (float)(here - 1);
                float span = 0.76f;
                Vector3 pos = bounds.center;
                if (alongX) pos.x = Mathf.Lerp(bounds.center.x - bounds.extents.x * span, bounds.center.x + bounds.extents.x * span, u);
                else pos.z = Mathf.Lerp(bounds.center.z - bounds.extents.z * span, bounds.center.z + bounds.extents.z * span, u);
                pos.y = bounds.max.y + 0.12f;
                slots.Add(pos);
            }
        }

        void SpawnPotions()
        {
            ClearPotions();
            foreach ((char letter, Vector3 slot) in _layout)
            {
                GameObject prefab = (potionPrefabs != null && potionPrefabs.Length > 0)
                    ? potionPrefabs[(letter - 'A') % potionPrefabs.Length]
                    : null;
                GameObject go = prefab != null
                    ? Instantiate(prefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                if (prefab == null) go.transform.localScale = Vector3.one * 0.25f;
                go.name = $"Potion {letter}";

                var ingredient = go.AddComponent<PotionIngredient>();
                ingredient.Setup(letter, this, slot, LetterColor(letter), tintPotions);
                _potions.Add(ingredient);
            }
        }

        void ClearPotions()
        {
            foreach (PotionIngredient potion in _potions)
                if (potion != null) Destroy(potion.gameObject);
            _potions.Clear();
        }

        static Color LetterColor(char letter)
        {
            float hue = ((letter - 'A') * 7 % 26) / 26f;   // scrambled so neighbors differ
            return Color.HSVToRGB(hue, 0.55f, 1f);
        }

        // ------------------------------------------------------------------
        // The book speaks

        IEnumerator SpeakSequence()
        {
            if (_phase == Phase.Speaking || _phase == Phase.Resolving) yield break;
            _phase = Phase.Speaking;
            _heardOnce = true;
            SetHint("Listen carefully...");

            Vector3 mouth = BookTop();
            yield return new WaitForSeconds(0.6f);

            foreach (char letter in _word)
            {
                float duration = speaker.Speak(letter, mouth, out bool voiced);
                if (!voiced || alwaysShowLetters)
                    StartCoroutine(LetterOverBook(letter, Mathf.Max(duration, 0.6f)));
                StartCoroutine(BookPulse(Mathf.Max(duration, 0.4f)));

                float waited = 0f;
                yield return new WaitForSeconds(duration);
                while (speaker.IsBusy && waited < 2.5f) { waited += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(gapBetweenLetters);
            }

            // An eager player may have finished pouring mid-sentence, which
            // moves the round to Resolving — don't drag it back to Brewing.
            if (_phase == Phase.Speaking)
            {
                _phase = Phase.Brewing;
                SetHint($"Drop the potions into the cauldron in that order!   {ProgressText()}\n(tap the book to hear it again)");
            }
        }

        Vector3 BookTop()
        {
            var renderers = book.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return book.position + Vector3.up * 0.4f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        IEnumerator LetterOverBook(char letter, float seconds)
        {
            var go = new GameObject($"Spoken {letter}");
            go.transform.position = BookTop() + Vector3.up * 0.25f;
            var label = go.AddComponent<TextMeshPro>();
            label.text = letter.ToString();
            label.fontSize = 3.2f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.95f, 0.6f, 1f);
            label.rectTransform.sizeDelta = new Vector2(2f, 1.4f);
            go.AddComponent<BillboardLabel>();

            float life = seconds + 0.35f;
            Vector3 start = go.transform.position;
            for (float t = 0f; t < life; t += Time.deltaTime)
            {
                float u = t / life;
                go.transform.position = start + Vector3.up * (0.3f * u);
                Color c = label.color;
                c.a = u < 0.15f ? u / 0.15f : Mathf.Clamp01(1f - (u - 0.7f) / 0.3f);
                label.color = c;
                yield return null;
            }
            Destroy(go);
        }

        IEnumerator BookPulse(float seconds)
        {
            Vector3 baseScale = book.localScale;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float pulse = 1f + 0.06f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / seconds));
                book.localScale = baseScale * pulse;
                yield return null;
            }
            book.localScale = baseScale;
        }

        // ------------------------------------------------------------------
        // Pours & evaluation

        public void NotifyPoured(PotionIngredient potion)
        {
            if (_phase == Phase.Resolving) return;
            _potions.Remove(potion);
            _poured.Add(potion.Letter);
            Cauldron.Pour(potion.Letter);
            if (_phase != Phase.Speaking)
                speaker.Speak(potion.Letter, Cauldron.Mouth, out _);   // the cauldron echoes the letter

            if (_phase == Phase.Brewing || _phase == Phase.Waiting)
                SetHint($"Drop the potions in the order you heard!   {ProgressText()}\n(tap the book to hear it again)");

            if (_poured.Count >= _word.Length)
                StartCoroutine(Evaluate());
        }

        string ProgressText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _word.Length; i++)
            {
                sb.Append(i < _poured.Count ? _poured[i] : '_');
                if (i < _word.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }

        IEnumerator Evaluate()
        {
            _phase = Phase.Resolving;
            yield return new WaitForSeconds(0.8f);

            bool correct = new string(_poured.ToArray()) == _word;
            if (correct)
            {
                _successes++;
                Cauldron.Celebrate(_word);
                PaperInventory.Add(_word);
                StartCoroutine(PaperFliesOut());
                Toast($"You brewed a {_word} paper!  It's in your satchel (x{PaperInventory.Count(_word)}).");
                yield return new WaitForSeconds(3.6f);
                NextRound();
            }
            else
            {
                Cauldron.Fizzle();
                Toast("Almost! The order wasn't quite right. Listen again...");
                yield return new WaitForSeconds(1.6f);
                _poured.Clear();
                SpawnPotions();                       // same word, fresh shelves
                _phase = Phase.Waiting;
                yield return SpeakSequence();
            }
        }

        IEnumerator PaperFliesOut()
        {
            var root = new GameObject($"Paper {_word}");
            root.transform.position = Cauldron.Mouth;

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.transform.SetParent(root.transform, false);
            sheet.transform.localScale = new Vector3(0.55f, 0.72f, 1f);
            Destroy(sheet.GetComponent<Collider>());

            var textGo = new GameObject("Word");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var text = textGo.AddComponent<TextMeshPro>();
            text.text = _word;
            text.fontSize = 2.4f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.2f, 0.16f, 0.1f, 1f);
            text.rectTransform.sizeDelta = new Vector2(0.55f, 0.7f);

            root.AddComponent<BillboardLabel>();

            Camera cam = Camera.main;
            Vector3 start = Cauldron.Mouth;
            Vector3 apex = start + Vector3.up * 1.6f;
            Vector3 end = cam != null
                ? cam.transform.position + cam.transform.forward * 1.4f - Vector3.up * 0.2f
                : start + Vector3.up * 2.2f;

            for (float t = 0f; t < 1.7f; t += Time.deltaTime)
            {
                float u = t / 1.7f;
                Vector3 a = Vector3.Lerp(start, apex, u);
                Vector3 b = Vector3.Lerp(apex, end, u);
                root.transform.position = Vector3.Lerp(a, b, u);
                if (u > 0.75f) root.transform.localScale = Vector3.one * (1f - (u - 0.75f) / 0.25f);
                yield return null;
            }
            Destroy(root);
        }

        // ------------------------------------------------------------------
        // Input: taps and drags in the world, nothing modal

        void Update()
        {
            if (_phase == Phase.Resolving) { _held = null; return; }

            if (Input.GetMouseButtonDown(0) && !PointerOverBlockingUi())
                BeginPress();

            if (_held != null)
            {
                if (Input.GetMouseButton(0))
                {
                    Ray ray = PointerRay();
                    _held.UpdateHold(ray.origin + ray.direction * _holdDistance);
                }
                if (Input.GetMouseButtonUp(0))
                    ReleaseHeld();
            }
            else if (_pressOnBook && Input.GetMouseButtonUp(0))
            {
                _pressOnBook = false;
                if (IsTap() && _phase != Phase.Speaking)
                    StartCoroutine(SpeakSequence());
            }
        }

        void BeginPress()
        {
            _pressScreen = PointerScreen();
            _pressTime = Time.unscaledTime;
            _pressOnBook = false;

            Ray ray = PointerRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, 80f)) return;

            var potion = hit.collider.GetComponentInParent<PotionIngredient>();
            if (potion != null && potion.CurrentState == PotionIngredient.State.Shelved
                && PlayerWithin(potion.transform.position, interactRange))
            {
                _held = potion;
                _holdDistance = Mathf.Clamp(Vector3.Distance(ray.origin, potion.transform.position), 1f, 3.6f);
                potion.BeginHold();
                return;
            }

            if (book != null && hit.transform.IsChildOf(book))
            {
                if (PlayerWithin(book.position, interactRange)) _pressOnBook = true;
                else SetHint("Step closer to the spell book to hear the recipe.");
            }
        }

        void ReleaseHeld()
        {
            bool tap = IsTap();
            Vector3 at = _held.transform.position;
            bool overCauldron = Vector3.Distance(
                new Vector3(at.x, 0f, at.z),
                new Vector3(Cauldron.Mouth.x, 0f, Cauldron.Mouth.z)) <= Cauldron.PourRadius
                && at.y > Cauldron.WorldBounds.min.y;

            _held.Release(tap || overCauldron);
            _held = null;
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
            Vector2 screen = PointerScreen();
            return cam != null ? cam.ScreenPointToRay(screen) : new Ray(Vector3.zero, Vector3.forward);
        }

        bool PlayerWithin(Vector3 position, float range)
        {
            if (_player == null) return true;
            return Vector3.Distance(_player.position, position) <= range;
        }

        /// <summary>
        /// True when the pointer is over real UI (buttons, panels) — but not
        /// over the invisible full-screen touch-look surface, which must never
        /// swallow world taps. Same recipe the creature game uses.
        /// </summary>
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
        // Minimal overlay: one hint line + one toast, no panels

        void BuildOverlay()
        {
            var canvasGo = new GameObject("Brewing Overlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _hint = MakeLabel(canvasGo.transform, 21f, new Color(1f, 1f, 1f, 0.95f));
            Rect(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(1080f, 84f));
            _toast = MakeLabel(canvasGo.transform, 27f, new Color(1f, 0.96f, 0.75f, 0f));
            Rect(_toast.rectTransform, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1000f, 90f));
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

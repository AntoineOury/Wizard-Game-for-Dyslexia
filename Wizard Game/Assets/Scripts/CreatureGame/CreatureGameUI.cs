using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Every screen of the mini-game, built at runtime in code (the same recipe
    /// as TouchControls — nothing to author in the scene, no prefab to lose):
    ///
    ///  - Left-edge buttons: Book (the creature booklet), Trap, Call — always
    ///    visible, so the game works identically with touch and mouse. B and T
    ///    keys reach the same places on a laptop.
    ///  - Booklet: caught counts and a friendly line per creature.
    ///  - Trap flow: pick a creature, then pick the word with the most of its
    ///    letter, then tap the ground to lay the paper.
    ///  - Call: pick a letter to shout, luring that creature to the player.
    ///  - Trace: dotted letter guide the player draws over to capture a stuck
    ///    creature. Guide dots and grading use the same points, so what the
    ///    player sees is exactly what is scored.
    ///
    /// Design leans on dyslexia-friendly basics: big type, one thing per
    /// screen, generous hit targets, and always-encouraging feedback.
    /// </summary>
    public class CreatureGameUI : MonoBehaviour
    {
        CreatureGameController _game;
        Canvas _canvas;
        Sprite _circle;

        RectTransform _bookletPanel;
        RectTransform _gridPanel;
        RectTransform _wordPanel;
        RectTransform _tracePanel;
        TMP_Text _hint;
        TMP_Text _toast;
        Coroutine _toastRoutine;

        // Trace session state.
        TraceSurface _traceSurface;
        RectTransform _traceGuide;
        TMP_Text _traceFeedback;
        LetterCreature _tracing;

        static readonly Color Ink = new Color(0.16f, 0.16f, 0.28f);
        static readonly Color Paper = new Color(0.98f, 0.95f, 0.87f);
        static readonly Color Accent = new Color(0.3f, 0.55f, 0.85f);
        static readonly Color Happy = new Color(0.35f, 0.7f, 0.35f);

        public bool AnyPanelOpen =>
            (_bookletPanel != null && _bookletPanel.gameObject.activeSelf)
            || (_gridPanel != null && _gridPanel.gameObject.activeSelf)
            || (_wordPanel != null && _wordPanel.gameObject.activeSelf)
            || (_tracePanel != null && _tracePanel.gameObject.activeSelf);

        public static CreatureGameUI Create(CreatureGameController game)
        {
            var go = new GameObject("Creature Game UI");
            go.transform.SetParent(game.transform, false);
            var ui = go.AddComponent<CreatureGameUI>();
            ui._game = game;
            ui.Build();
            return ui;
        }

        // ------------------------------------------------------------------
        // Public surface used by the controller
        // ------------------------------------------------------------------

        public void ToggleBooklet()
        {
            bool open = _bookletPanel.gameObject.activeSelf;
            CloseAllPanels();
            if (!open) OpenBooklet();
        }

        /// <summary>Trap flow step 1: which creature are we hunting?</summary>
        public void OpenTrapFlow()
        {
            CloseAllPanels();
            OpenLetterGrid("Make a trap! Who do you want to catch?",
                letter => OpenWordChallenge(letter));
        }

        public void OpenCallFlow()
        {
            CloseAllPanels();
            OpenCallPanel();
        }

        public void OpenTrace(LetterCreature creature)
        {
            CloseAllPanels();
            _tracing = creature;
            BuildTracePanel(creature.Letter);
            _tracePanel.gameObject.SetActive(true);
        }

        public void SetHint(string text)
        {
            _hint.text = text;
            _hint.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void Toast(string message)
        {
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine(message));
        }

        // ------------------------------------------------------------------
        // Canvas scaffolding
        // ------------------------------------------------------------------

        void Build()
        {
            _circle = MakeCircleSprite(64);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40; // above the scene UI and the touch controls

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Scene-authored buttons win: when the scene provides
            // CreatureGameButton objects (stylable in the Hierarchy), the
            // code-built side buttons stay out of the way entirely.
            if (FindObjectOfType<CreatureGameButton>(true) == null)
                BuildSideButtons();
            _hint = BuildBar(new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(760f, 46f), 24f);
            _toast = BuildBar(new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(700f, 52f), 26f);
            _toast.transform.parent.gameObject.SetActive(false);

            _bookletPanel = BuildWindow("Booklet", new Vector2(660f, 540f));
            _gridPanel = BuildWindow("LetterGrid", new Vector2(600f, 380f));
            _wordPanel = BuildWindow("WordChoice", new Vector2(660f, 470f));
            _tracePanel = BuildWindow("Trace", new Vector2(640f, 660f));
        }

        void BuildSideButtons()
        {
            // Left side per the design brief: the booklet lives on the left edge
            // in touch mode; Trap and Call sit with it so every action has a
            // no-keyboard path.
            string[] labels = { "Book", "Trap", "Call" };
            System.Action[] actions = { ToggleBooklet, OpenTrapFlow, OpenCallFlow };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = MakeButton(_canvas.transform, labels[i], new Vector2(126f, 56f), Accent, 26f, actions[i]);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(14f, 90f - i * 68f);
            }
        }

        TMP_Text BuildBar(Vector2 anchor, Vector2 offset, Vector2 size, float fontSize)
        {
            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)barGo.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.2f, 0.75f);
            bg.raycastTarget = false;

            TMP_Text label = MakeLabel(barGo.transform, "", fontSize, Color.white);
            Stretch((RectTransform)label.transform);
            return label;
        }

        RectTransform BuildWindow(string name, Vector2 size)
        {
            // Full-screen dim blocker (also swallows world taps) + the window.
            var blockerGo = new GameObject(name, typeof(RectTransform));
            blockerGo.transform.SetParent(_canvas.transform, false);
            Stretch((RectTransform)blockerGo.transform);
            var dim = blockerGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            var windowGo = new GameObject("Window", typeof(RectTransform));
            windowGo.transform.SetParent(blockerGo.transform, false);
            var window = (RectTransform)windowGo.transform;
            window.sizeDelta = size;
            windowGo.AddComponent<Image>().color = Paper;

            Button close = MakeButton(window, "X", new Vector2(46f, 46f), new Color(0.8f, 0.35f, 0.3f), 24f, CloseAllPanels);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);

            blockerGo.SetActive(false);
            return (RectTransform)blockerGo.transform;
        }

        void CloseAllPanels()
        {
            _bookletPanel.gameObject.SetActive(false);
            _gridPanel.gameObject.SetActive(false);
            _wordPanel.gameObject.SetActive(false);
            _tracePanel.gameObject.SetActive(false);
            _tracing = null;
            StopVoice();
        }

        void StopVoice()
        {
            if (_game == null || _game.Voice == null) return;
            if (_voiceAttached)
            {
                _game.Voice.LetterHeard -= OnVoiceLetter;
                _voiceAttached = false;
            }
            _game.Voice.StopListening();
        }

        static RectTransform Window(RectTransform panel) => (RectTransform)panel.GetChild(0);

        /// <summary>Rebuilds a window's content, keeping only the close button (child 0).</summary>
        static void ClearWindow(RectTransform panel)
        {
            RectTransform window = Window(panel);
            for (int i = window.childCount - 1; i >= 1; i--)
                Destroy(window.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------
        // Booklet
        // ------------------------------------------------------------------

        void OpenBooklet()
        {
            ClearWindow(_bookletPanel);
            RectTransform window = Window(_bookletPanel);

            TMP_Text title = MakeLabel(window, "My Creature Book", 34f, Ink, bold: true);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(560f, 44f));

            float y = -110f;
            foreach (CreatureDefinition definition in _game.creatures)
            {
                if (definition == null) continue;
                int caught = CaptureJournal.CountOf(definition.Letter);

                TMP_Text letter = MakeLabel(window, definition.Letter.ToString(), 72f, caught > 0 ? Accent : new Color(0.6f, 0.6f, 0.65f), bold: true);
                Place(letter, new Vector2(0f, 1f), new Vector2(78f, y), new Vector2(110f, 100f));

                TMP_Text name = MakeLabel(window, $"{definition.DisplayName}   •   Caught: {caught}", 27f, Ink, bold: true);
                name.alignment = TextAlignmentOptions.Left;
                Place(name, new Vector2(0f, 1f), new Vector2(400f, y + 24f), new Vector2(480f, 36f));

                string story = caught > 0
                    ? definition.blurb
                    : "Not caught yet! Lay a word trap and trace its letter.";
                TMP_Text blurb = MakeLabel(window, story, 21f, new Color(0.3f, 0.3f, 0.4f));
                blurb.alignment = TextAlignmentOptions.TopLeft;
                Place(blurb, new Vector2(0f, 1f), new Vector2(400f, y - 26f), new Vector2(480f, 64f));

                y -= 118f;
            }

            TMP_Text total = MakeLabel(window, $"Total caught: {CaptureJournal.TotalCaught}", 24f, Ink);
            Place(total, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(400f, 32f));

            _bookletPanel.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // Calling — voice first, letter buttons as the everywhere-fallback
        // ------------------------------------------------------------------

        bool _voiceAttached;

        void OpenCallPanel()
        {
            ClearWindow(_gridPanel);
            RectTransform window = Window(_gridPanel);

            TMP_Text title = MakeLabel(window, "Call a creature!", 30f, Ink, bold: true);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(520f, 40f));

            VoiceLetterListener voice = _game.Voice;
            bool listening = false;
            if (voice != null && voice.IsSupported)
            {
                var letters = new List<char>();
                foreach (CreatureDefinition definition in _game.creatures)
                    if (definition != null) letters.Add(definition.Letter);

                voice.StartListening(letters);
                listening = voice.IsListening;
                if (listening)
                {
                    voice.LetterHeard += OnVoiceLetter;
                    _voiceAttached = true;
                }
            }

            TMP_Text status = MakeLabel(window,
                listening
                    ? "Say the creature's letter out loud!\n(or tap it below)"
                    : "No microphone here — tap the letter to shout it:",
                23f, listening ? Happy : new Color(0.35f, 0.35f, 0.45f), bold: listening);
            Place(status, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(540f, 58f));

            BuildLetterButtons(window, letter =>
            {
                CloseAllPanels();
                _game.CallLetter(letter);
            });

            _gridPanel.gameObject.SetActive(true);
        }

        void OnVoiceLetter(char letter)
        {
            Toast($"You said {letter}!");
            CloseAllPanels();
            _game.CallLetter(letter);
        }

        // ------------------------------------------------------------------
        // Letter grid (Trap step 1)
        // ------------------------------------------------------------------

        void OpenLetterGrid(string titleText, System.Action<char> onPick)
        {
            ClearWindow(_gridPanel);
            RectTransform window = Window(_gridPanel);

            TMP_Text title = MakeLabel(window, titleText, 28f, Ink, bold: true);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(520f, 70f));

            BuildLetterButtons(window, onPick);
            _gridPanel.gameObject.SetActive(true);
        }

        /// <summary>One big letter button per defined creature, laid out 3 per row.</summary>
        void BuildLetterButtons(RectTransform window, System.Action<char> onPick)
        {
            int index = 0;
            foreach (CreatureDefinition definition in _game.creatures)
            {
                if (definition == null) continue;
                char letter = definition.Letter;

                Button button = MakeButton(window, letter.ToString(), new Vector2(120f, 120f),
                    new Color(0.95f, 0.8f, 0.35f), 62f, () => onPick(letter));
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                int column = index % 3, row = index / 3;
                rect.anchoredPosition = new Vector2(-150f + column * 150f, -10f - row * 150f);

                TMP_Text caption = MakeLabel(window, definition.DisplayName, 18f, Ink);
                Place(caption, new Vector2(0.5f, 0.5f), rect.anchoredPosition + new Vector2(0f, -76f), new Vector2(150f, 24f));
                index++;
            }
        }

        // ------------------------------------------------------------------
        // Word challenge (trap step 2)
        // ------------------------------------------------------------------

        void OpenWordChallenge(char letter)
        {
            CloseAllPanels();
            ClearWindow(_wordPanel);
            RectTransform window = Window(_wordPanel);

            WordBank.Challenge challenge = WordBank.Build(letter, _game.Rng);

            TMP_Text title = MakeLabel(window, $"Bait for Creature {letter}!", 32f, Ink, bold: true);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(560f, 42f));

            TMP_Text ask = MakeLabel(window, $"Tap the word with the MOST  {letter} {char.ToLowerInvariant(letter)}  letters:", 25f, Ink);
            Place(ask, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(580f, 36f));

            TMP_Text feedback = MakeLabel(window, "", 24f, Happy, bold: true);
            Place(feedback, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(580f, 36f));

            for (int i = 0; i < challenge.words.Length; i++)
            {
                int optionIndex = i;
                string word = challenge.words[i];
                Button button = MakeButton(window, word, new Vector2(420f, 74f), Color.white, 40f, null);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -166f - i * 90f);
                button.GetComponentInChildren<TMP_Text>().color = Ink;

                Button captured = button;
                button.onClick.AddListener(() =>
                {
                    if (optionIndex == challenge.correctIndex)
                    {
                        int count = WordBank.CountLetter(word, letter);
                        Toast($"Yes! \"{word}\" has {count} {letter}'{(count == 1 ? "" : "s")}. Now tap the ground to lay it!");
                        CloseAllPanels();
                        _game.BeginTrapPlacement(letter, word);
                    }
                    else
                    {
                        feedback.text = $"Almost! Count the {letter}'s in each word again.";
                        captured.interactable = false;
                    }
                });
            }

            _wordPanel.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // Tracing (capture)
        // ------------------------------------------------------------------

        void BuildTracePanel(char letter)
        {
            ClearWindow(_tracePanel);
            RectTransform window = Window(_tracePanel);

            TMP_Text title = MakeLabel(window, $"Trace the {letter} to catch Creature {letter}!", 28f, Ink, bold: true);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(560f, 40f));

            // Drawing board.
            var boardGo = new GameObject("Board", typeof(RectTransform));
            boardGo.transform.SetParent(window, false);
            var board = (RectTransform)boardGo.transform;
            board.sizeDelta = new Vector2(460f, 460f);
            board.anchoredPosition = new Vector2(0f, 20f);
            boardGo.AddComponent<Image>().color = Color.white;
            _traceSurface = boardGo.AddComponent<TraceSurface>();
            _traceSurface.Init(_circle, Ink);

            // Dotted guide from the very points the grader uses.
            var guideGo = new GameObject("Guide", typeof(RectTransform));
            guideGo.transform.SetParent(board, false);
            _traceGuide = (RectTransform)guideGo.transform;
            Stretch(_traceGuide);
            float scale = 460f * 0.8f;
            foreach (Vector2 point in LetterShapes.SamplePath(letter, 0.075f))
                MakeDot(_traceGuide, point * scale, 16f, new Color(0.55f, 0.7f, 0.95f, 0.9f));

            _traceFeedback = MakeLabel(window, "Draw over the dots with your finger or mouse", 21f, new Color(0.3f, 0.3f, 0.4f));
            Place(_traceFeedback, new Vector2(0.5f, 0f), new Vector2(0f, 78f), new Vector2(560f, 30f));

            Button done = MakeButton(window, "Done!", new Vector2(180f, 58f), Happy, 28f, EvaluateTrace);
            var doneRect = (RectTransform)done.transform;
            doneRect.anchorMin = doneRect.anchorMax = new Vector2(0.5f, 0f);
            doneRect.anchoredPosition = new Vector2(110f, 40f);

            Button clear = MakeButton(window, "Start over", new Vector2(180f, 58f), new Color(0.65f, 0.65f, 0.7f), 24f,
                () => _traceSurface.Clear());
            var clearRect = (RectTransform)clear.transform;
            clearRect.anchorMin = clearRect.anchorMax = new Vector2(0.5f, 0f);
            clearRect.anchoredPosition = new Vector2(-110f, 40f);
        }

        void EvaluateTrace()
        {
            if (_tracing == null) { CloseAllPanels(); return; }
            if (_traceSurface.PointCount < 5)
            {
                _traceFeedback.text = "Draw the letter first — follow the dots!";
                return;
            }

            float scale = 460f * 0.8f;
            List<Vector2> template = LetterShapes.SamplePath(_tracing.Letter, 0.04f);
            for (int i = 0; i < template.Count; i++) template[i] *= scale;

            float accuracy = OverlapAccuracy(_traceSurface.AllPoints, template, tolerance: scale * 0.14f);

            if (accuracy >= _game.traceAccuracyThreshold)
            {
                LetterCreature caught = _tracing;
                CloseAllPanels();
                _game.CompleteCapture(caught);
            }
            else
            {
                _traceFeedback.text = $"So close ({Mathf.RoundToInt(accuracy * 100f)}%)! Start over and stay on the dots.";
            }
        }

        /// <summary>
        /// The overlap score proven out by the project's ShapeRecognizer demo:
        /// the worse of "how much of the drawing sits on the letter" and "how
        /// much of the letter got covered" — so neither scribbling everywhere
        /// nor tracing only half the shape can pass.
        /// </summary>
        static float OverlapAccuracy(IReadOnlyList<Vector2> drawn, List<Vector2> template, float tolerance)
        {
            float toleranceSqr = tolerance * tolerance;

            int drawnOnPath = 0;
            foreach (Vector2 point in drawn)
                if (NearAny(point, template, toleranceSqr)) drawnOnPath++;

            int covered = 0;
            foreach (Vector2 point in template)
                if (NearAny(point, drawn, toleranceSqr)) covered++;

            float onPath = drawn.Count > 0 ? drawnOnPath / (float)drawn.Count : 0f;
            float coverage = template.Count > 0 ? covered / (float)template.Count : 0f;
            return Mathf.Min(onPath, coverage);
        }

        static bool NearAny(Vector2 point, IReadOnlyList<Vector2> others, float toleranceSqr)
        {
            for (int i = 0; i < others.Count; i++)
                if ((others[i] - point).sqrMagnitude <= toleranceSqr) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Small builders
        // ------------------------------------------------------------------

        IEnumerator ToastRoutine(string message)
        {
            _toast.text = message;
            _toast.transform.parent.gameObject.SetActive(true);
            yield return new WaitForSeconds(3.2f);
            _toast.transform.parent.gameObject.SetActive(false);
        }

        Button MakeButton(Transform parent, string label, Vector2 size, Color color, float fontSize, System.Action onClick)
        {
            var go = new GameObject($"Button {label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            TMP_Text text = MakeLabel(go.transform, label, fontSize, Color.white, bold: true);
            Stretch((RectTransform)text.transform);
            return button;
        }

        TMP_Text MakeLabel(Transform parent, string content, float fontSize, Color color, bool bold = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.raycastTarget = false;
            return text;
        }

        void MakeDot(Transform parent, Vector2 position, float size, Color color)
        {
            var go = new GameObject("Dot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.sprite = _circle;
            image.color = color;
            image.raycastTarget = false;
        }

        static void Place(TMP_Text label, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Soft-edged circle for guide and ink dots, generated in code.</summary>
        static Sprite MakeCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Creature Game Dot";
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((center - distance) / 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>
    /// The drawing board: captures pointer strokes in its own local space and
    /// lays ink dots along them. Same pointer-event recipe as VirtualJoystick,
    /// so it works identically for a mouse and a finger.
    /// </summary>
    public class TraceSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        readonly List<Vector2> _points = new List<Vector2>();
        RectTransform _rect;
        Transform _inkRoot;
        Sprite _dot;
        Color _inkColor;
        Vector2 _lastInk;
        bool _drawing;

        public IReadOnlyList<Vector2> AllPoints => _points;
        public int PointCount => _points.Count;

        public void Init(Sprite dotSprite, Color inkColor)
        {
            _rect = (RectTransform)transform;
            _dot = dotSprite;
            _inkColor = inkColor;

            var inkGo = new GameObject("Ink", typeof(RectTransform));
            inkGo.transform.SetParent(transform, false);
            var inkRect = (RectTransform)inkGo.transform;
            inkRect.anchorMin = Vector2.zero;
            inkRect.anchorMax = Vector2.one;
            inkRect.offsetMin = inkRect.offsetMax = Vector2.zero;
            _inkRoot = inkGo.transform;
        }

        public void Clear()
        {
            _points.Clear();
            for (int i = _inkRoot.childCount - 1; i >= 0; i--)
                Destroy(_inkRoot.GetChild(i).gameObject);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!TryLocalPoint(eventData, out Vector2 local)) return;
            _drawing = true;
            AddInk(local, force: true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_drawing || !TryLocalPoint(eventData, out Vector2 local)) return;
            AddInk(local, force: false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _drawing = false;
        }

        bool TryLocalPoint(PointerEventData eventData, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect, eventData.position, eventData.pressEventCamera, out local);
        }

        void AddInk(Vector2 local, bool force)
        {
            // Thin the stream so scoring stays cheap and the line stays even.
            if (!force && (local - _lastInk).sqrMagnitude < 36f) return;
            _lastInk = local;
            _points.Add(local);

            var go = new GameObject("Ink Dot", typeof(RectTransform));
            go.transform.SetParent(_inkRoot, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(13f, 13f);
            rect.anchoredPosition = local;
            var image = go.AddComponent<Image>();
            image.sprite = _dot;
            image.color = _inkColor;
            image.raycastTarget = false;
        }
    }
}

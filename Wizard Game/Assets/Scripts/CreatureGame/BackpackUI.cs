using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// The player's backpack: shows the word-trap papers brewed in the
    /// alchemist store (<see cref="PaperInventory"/>), ready for hunting
    /// trips. Built to be identical in EVERY scene with zero authoring:
    /// it bootstraps itself on play, survives scene loads, and floats a
    /// "Bag" button on the right edge of the screen (plus the `I` key).
    ///
    /// Wants a hand-styled button instead? Put a UI Button anywhere in a
    /// scene and add the <see cref="BackpackButton"/> component — the
    /// floating button hides in scenes that author their own, the same
    /// contract the creature-game buttons follow. In scenes with no player
    /// (like the main menu) the button stays hidden entirely.
    ///
    /// While the panel is open, gameplay input pauses through the same
    /// PlayerControlScheme.UiMode flag every other panel uses — and it
    /// refuses to open on top of someone else's panel (booklet, capture).
    /// </summary>
    public class BackpackUI : MonoBehaviour
    {
        public static BackpackUI Instance { get; private set; }

        const int SortingOrder = 45;   // above the creature UI (40), far above touch controls (-10)

        static readonly Color WindowColor = new Color(0.11f, 0.10f, 0.18f, 0.96f);
        static readonly Color Gold = new Color(1f, 0.87f, 0.45f, 1f);
        static readonly Color Paper = new Color(0.96f, 0.93f, 0.84f, 1f);

        Canvas _canvas;
        GameObject _floatingButton;
        GameObject _panel;
        RectTransform _window;
        readonly List<GameObject> _rows = new List<GameObject>();
        TMP_Text _totalLabel;
        TMP_Text _emptyLabel;
        bool _claimedUiMode;
        bool _uiAvailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var existing = FindObjectOfType<BackpackUI>();
            if (existing != null) { Instance = existing; return; }
            new GameObject("Backpack UI").AddComponent<BackpackUI>();
        }

        /// <summary>What scene buttons call. Safe before/after bootstrap.</summary>
        public static void ToggleBackpack()
        {
            if (Instance == null) Bootstrap();
            if (Instance != null) Instance.Toggle();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
            SceneManager.sceneLoaded += OnSceneLoaded;
            RefreshSceneBinding();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            PaperInventory.Changed -= OnInventoryChanged;
            ReleaseUiMode();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Close();
            RefreshSceneBinding();
        }

        void RefreshSceneBinding()
        {
            bool gameplayScene = FindObjectOfType<CharacterController>() != null;
            bool sceneHasButton = FindObjectOfType<BackpackButton>(true) != null;
            _uiAvailable = gameplayScene || sceneHasButton;
            _floatingButton.SetActive(gameplayScene && !sceneHasButton);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.I)) Toggle();
            if (_panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) Close();
            else Open();
        }

        public void Open()
        {
            if (_panel.activeSelf || !_uiAvailable) return;
            if (PlayerControlScheme.UiMode) return;   // another panel (booklet, capture) owns the screen
            _panel.SetActive(true);
            Rebuild();
            PaperInventory.Changed += OnInventoryChanged;
            PlayerControlScheme.UiMode = true;
            _claimedUiMode = true;
        }

        public void Close()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            PaperInventory.Changed -= OnInventoryChanged;
            ReleaseUiMode();
        }

        void ReleaseUiMode()
        {
            if (!_claimedUiMode) return;
            PlayerControlScheme.UiMode = false;
            _claimedUiMode = false;
        }

        void OnInventoryChanged()
        {
            if (_panel.activeSelf) Rebuild();
        }

        // ------------------------------------------------------------------
        // Construction (runtime-built, so it exists in every scene alike)

        void Build()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // The floating "Bag" button, middle of the right edge.
            _floatingButton = MakeButton(_canvas.transform, "Bag (I)", Toggle, out _);
            var buttonRect = (RectTransform)_floatingButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-10f, 0f);
            buttonRect.sizeDelta = new Vector2(118f, 48f);

            // The panel: dim blocker (tap outside = close) + the window.
            _panel = new GameObject("Backpack Panel", typeof(RectTransform));
            _panel.transform.SetParent(_canvas.transform, false);
            Stretch((RectTransform)_panel.transform);

            var dim = _panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.5f);
            _panel.AddComponent<Button>().onClick.AddListener(Close);

            var windowGo = new GameObject("Window", typeof(RectTransform));
            windowGo.transform.SetParent(_panel.transform, false);
            _window = (RectTransform)windowGo.transform;
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(560f, 600f);
            windowGo.AddComponent<Image>().color = WindowColor;
            windowGo.AddComponent<Button>();   // swallows taps so the blocker doesn't close under the window

            TMP_Text title = MakeText(_window, "Backpack", 34f, Gold, FontStyles.Bold);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(480f, 50f));

            TMP_Text subtitle = MakeText(_window, "Word papers for your hunting trips", 18f,
                new Color(0.75f, 0.75f, 0.85f, 1f), FontStyles.Normal);
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(500f, 30f));

            _emptyLabel = MakeText(_window,
                "Nothing in here yet!\n\nBrew word papers at the alchemist store\nand they will wait here for the hunt.",
                21f, Paper, FontStyles.Normal);
            Place(_emptyLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(480f, 220f));

            _totalLabel = MakeText(_window, "", 20f, new Color(0.75f, 0.75f, 0.85f, 1f), FontStyles.Normal);
            Place(_totalLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(480f, 32f));

            GameObject closeGo = MakeButton(_window, "X", Close, out TMP_Text closeLabel);
            closeLabel.fontSize = 22f;
            var closeRect = (RectTransform)closeGo.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-10f, -10f);
            closeRect.sizeDelta = new Vector2(44f, 44f);

            _panel.SetActive(false);
        }

        void Rebuild()
        {
            foreach (GameObject row in _rows) Destroy(row);
            _rows.Clear();

            var entries = new List<KeyValuePair<string, int>>(PaperInventory.All);
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            _emptyLabel.gameObject.SetActive(entries.Count == 0);
            _totalLabel.text = entries.Count == 0 ? "" : $"Papers in the bag: {PaperInventory.TotalCount}";

            // Rows stack under the subtitle; spacing shrinks if the bag is full.
            float top = -122f;
            float bottom = 70f;
            float available = _window.sizeDelta.y - (-top) - bottom;
            float spacing = Mathf.Min(52f, entries.Count > 0 ? available / entries.Count : 52f);

            for (int i = 0; i < entries.Count; i++)
            {
                var rowGo = new GameObject($"Row {entries[i].Key}", typeof(RectTransform));
                rowGo.transform.SetParent(_window, false);
                var rect = (RectTransform)rowGo.transform;
                Place(rect, new Vector2(0.5f, 1f), new Vector2(0f, top - spacing * i - spacing * 0.5f),
                    new Vector2(470f, spacing - 6f));

                var stripe = rowGo.AddComponent<Image>();
                stripe.color = new Color(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.09f);
                stripe.raycastTarget = false;

                TMP_Text word = MakeText(rect, entries[i].Key, 26f, Paper, FontStyles.Bold);
                word.alignment = TextAlignmentOptions.MidlineLeft;
                Place(word.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(14f, 0f), new Vector2(440f, 40f));

                TMP_Text count = MakeText(rect, $"x{entries[i].Value}", 24f, Gold, FontStyles.Bold);
                count.alignment = TextAlignmentOptions.MidlineRight;
                Place(count.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-14f, 0f), new Vector2(440f, 40f));

                _rows.Add(rowGo);
            }
        }

        // ------------------------------------------------------------------
        // Small builders (same recipes as the other runtime UIs)

        GameObject MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, out TMP_Text text)
        {
            var go = new GameObject($"Button {label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.16f, 0.14f, 0.24f, 0.92f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            text = MakeText((RectTransform)go.transform, label, 21f, Paper, FontStyles.Bold);
            Stretch(text.rectTransform);
            return go;
        }

        static TMP_Text MakeText(Transform parent, string content, float size, Color color, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

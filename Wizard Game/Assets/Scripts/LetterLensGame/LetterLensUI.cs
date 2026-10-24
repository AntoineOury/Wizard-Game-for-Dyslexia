using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// Letter Lens screen layer, built in code: the hint line, the found
    /// letters, a viewfinder frame while the view is locked, the ink of the
    /// trace, a big letter when one is found, and a Lock in / Step out button
    /// (always shown while locked; while walking only in touch mode, where
    /// there is no C key).
    ///
    /// Ink is placed by VIEWPORT position (anchors), not canvas pixels, so it
    /// lands under the pointer whatever the canvas scaling — the game hands
    /// in TruePointer-derived viewport points.
    /// </summary>
    public class LetterLensUI : MonoBehaviour
    {
        static readonly Color BarColor = new Color(0.1f, 0.1f, 0.2f, 0.75f);
        static readonly Color Accent = new Color(0.86f, 0.62f, 0.2f, 0.95f);
        static readonly Color InkColor = new Color(1f, 0.86f, 0.3f, 1f);

        LetterLensGame _game;
        Canvas _canvas;
        Sprite _circle;
        GameObject _hintBar;
        TMP_Text _hint;
        TMP_Text _foundLabel;
        GameObject _frame;
        RectTransform _inkLayer;
        Button _lockButton;
        TMP_Text _lockLabel;
        TMP_Text _celebration;
        float _celebrateStart = -10f;
        bool _lockedIn;

        public void Init(LetterLensGame game)
        {
            _game = game;
            Build();
        }

        public void SetHint(string text)
        {
            if (_hint == null) return;
            _hint.text = text;
            _hintBar.SetActive(!string.IsNullOrEmpty(text));
        }

        public void SetFound(IReadOnlyList<char> letters)
        {
            if (_foundLabel == null) return;
            _foundLabel.text = letters.Count == 0 ? "" : "Letters found:  " + string.Join("  ", letters);
            _foundLabel.transform.parent.gameObject.SetActive(letters.Count > 0);
        }

        public void ShowLockedIn(bool locked)
        {
            _lockedIn = locked;
            _frame.SetActive(locked);
        }

        public void AddInk(Vector2 viewport)
        {
            var go = new GameObject("Ink", typeof(RectTransform));
            go.transform.SetParent(_inkLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = viewport;
            rect.sizeDelta = new Vector2(16f, 16f);
            var image = go.AddComponent<Image>();
            image.sprite = _circle;
            image.color = InkColor;
            image.raycastTarget = false;
        }

        public void ClearInk()
        {
            if (_inkLayer == null) return;
            for (int i = _inkLayer.childCount - 1; i >= 0; i--)
                Destroy(_inkLayer.GetChild(i).gameObject);
        }

        public void Celebrate(char letter)
        {
            _celebration.text = letter.ToString();
            _celebration.gameObject.SetActive(true);
            _celebrateStart = Time.time;
        }

        void Update()
        {
            if (_game == null) return;

            bool touch = PlayerControlScheme.Current == ControlSchemeKind.Touch;
            bool showButton = _lockedIn || (touch && _game.CanLockIn);
            if (_lockButton.gameObject.activeSelf != showButton) _lockButton.gameObject.SetActive(showButton);
            _lockLabel.text = _lockedIn ? "Step out" : "Lock in";

            if (_celebration.gameObject.activeSelf)
            {
                float t = (Time.time - _celebrateStart) / 1.8f;
                if (t >= 1f) _celebration.gameObject.SetActive(false);
                else
                {
                    float pop = t < 0.2f ? Mathf.SmoothStep(0.3f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 1f, (t - 0.2f) / 0.8f);
                    _celebration.transform.localScale = Vector3.one * pop;
                    _celebration.alpha = t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f;
                }
            }
        }

        // ------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------

        void Build()
        {
            _circle = MakeCircleSprite(32);

            var canvasGo = new GameObject("Letter Lens Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 45; // above the scene UI, touch controls and creature game UI
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var inkGo = new GameObject("Ink Layer", typeof(RectTransform));
            inkGo.transform.SetParent(_canvas.transform, false);
            _inkLayer = (RectTransform)inkGo.transform;
            Stretch(_inkLayer);

            BuildFrame();

            // Top, below the creature game's toast line.
            _hint = BuildBar(new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(760f, 46f), 24f, out _hintBar);
            _hintBar.SetActive(false);
            _foundLabel = BuildBar(new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(330f, 40f), 22f, out _);
            _foundLabel.transform.parent.gameObject.SetActive(false);

            _lockButton = MakeButton(_canvas.transform, "Lock in", new Vector2(150f, 60f), Accent, 26f, () => _game.ToggleLockIn());
            var buttonRect = (RectTransform)_lockButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-14f, 40f);
            _lockLabel = _lockButton.GetComponentInChildren<TMP_Text>();
            _lockButton.gameObject.SetActive(false);

            _celebration = MakeLabel(_canvas.transform, "", 220f, InkColor, bold: true);
            var celebrationRect = (RectTransform)_celebration.transform;
            celebrationRect.anchorMin = celebrationRect.anchorMax = new Vector2(0.5f, 0.55f);
            celebrationRect.sizeDelta = new Vector2(400f, 300f);
            _celebration.outlineWidth = 0.15f;
            _celebration.outlineColor = new Color32(60, 40, 10, 255);
            _celebration.gameObject.SetActive(false);
        }

        /// <summary>Four corner brackets: the "viewfinder" that says the view is locked.</summary>
        void BuildFrame()
        {
            _frame = new GameObject("Viewfinder Frame", typeof(RectTransform));
            _frame.transform.SetParent(_canvas.transform, false);
            Stretch((RectTransform)_frame.transform);

            const float Inset = 36f, Length = 70f, Thickness = 6f;
            Vector2[] corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            foreach (Vector2 corner in corners)
            {
                Vector2 inward = new Vector2(corner.x == 0f ? 1f : -1f, corner.y == 0f ? 1f : -1f);
                MakeBar(corner, inward * Inset, new Vector2(Length, Thickness), inward);
                MakeBar(corner, inward * Inset, new Vector2(Thickness, Length), inward);
            }
            _frame.SetActive(false);
        }

        void MakeBar(Vector2 anchor, Vector2 position, Vector2 size, Vector2 inward)
        {
            var go = new GameObject("Bracket", typeof(RectTransform));
            go.transform.SetParent(_frame.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(inward.x > 0f ? 0f : 1f, inward.y > 0f ? 0f : 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.85f);
            image.raycastTarget = false;
        }

        TMP_Text BuildBar(Vector2 anchor, Vector2 offset, Vector2 size, float fontSize, out GameObject bar)
        {
            bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            var background = bar.AddComponent<Image>();
            background.color = BarColor;
            background.raycastTarget = false;

            TMP_Text label = MakeLabel(bar.transform, "", fontSize, Color.white);
            Stretch((RectTransform)label.transform);
            return label;
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

        static TMP_Text MakeLabel(Transform parent, string content, float fontSize, Color color, bool bold = false)
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

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite MakeCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Letter Lens Dot" };
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((center - distance) / 2f)));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}

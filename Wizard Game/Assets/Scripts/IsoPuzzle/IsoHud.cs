using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// The Iso Puzzle's screen layer, built in code so the scene needs no
    /// prefabs: hearts (top left), rooms cleared (top right), the hint line
    /// and the Snap button (bottom), viewfinder corners that warm up as the
    /// view nears the letter, the photo/tracing layer, a camera flash, and
    /// the title and game-over cards.
    /// </summary>
    public class IsoHud : MonoBehaviour
    {
        public const int MaxHearts = 3;

        static readonly Color HeartFull = new Color(0.9f, 0.22f, 0.3f);
        static readonly Color HeartEmpty = new Color(0.25f, 0.22f, 0.28f, 0.35f);
        static readonly Color Ink = new Color(0.17f, 0.15f, 0.28f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.25f);

        public RectTransform CanvasRect { get; private set; }
        public Button SnapButton { get; private set; }
        public Button RetakeButton { get; private set; }
        public Button PlayButton { get; private set; }
        public Button AgainButton { get; private set; }

        /// <summary>Full-screen photo the player traces on.</summary>
        public RawImage Photo { get; private set; }
        /// <summary>Parent for ink and guide dots, laid over the photo, same rect as the screen.</summary>
        public RectTransform TraceLayer { get; private set; }

        Font _font;
        Sprite _heart, _circle;
        Image[] _hearts;
        Text _score, _hint, _gameOverText, _snapLabel;
        Image _flash;
        float _flashAlpha;
        Color _flashColor;
        Image[] _corners;
        GameObject _photoRoot, _titleCard, _gameOverCard;
        float _shake;
        RectTransform _heartRow;

        public static IsoHud Create(Transform parent)
        {
            if (EventSystem.current == null && FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<EventSystem>();
                events.AddComponent<StandaloneInputModule>();
            }

            var go = new GameObject("Iso HUD");
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var hud = go.AddComponent<IsoHud>();
            hud.Build();
            return hud;
        }

        void Build()
        {
            CanvasRect = (RectTransform)transform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _heart = MakeHeartSprite();
            _circle = MakeCircleSprite();

            // Photo + tracing layer (under the HUD chrome).
            _photoRoot = new GameObject("Photo");
            _photoRoot.transform.SetParent(transform, false);
            Stretch(_photoRoot.AddComponent<RectTransform>());
            Photo = _photoRoot.AddComponent<RawImage>();
            Photo.raycastTarget = false;
            var frame = Panel(_photoRoot.transform, "Frame", new Color(1f, 1f, 1f, 0f));
            Stretch(frame);
            AddOutline(frame, 18f, Color.white);
            TraceLayer = new GameObject("Trace Layer").AddComponent<RectTransform>();
            TraceLayer.SetParent(_photoRoot.transform, false);
            Stretch(TraceLayer);
            _photoRoot.SetActive(false);

            // Viewfinder corners.
            _corners = new Image[8];
            for (int c = 0; c < 4; c++)
            {
                Vector2 anchor = new Vector2(c % 2, c / 2);
                for (int arm = 0; arm < 2; arm++)
                {
                    var rt = Panel(transform, "Corner", Color.white);
                    rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                    rt.anchoredPosition = new Vector2(anchor.x == 0 ? 40 : -40, anchor.y == 0 ? 40 : -40);
                    rt.sizeDelta = arm == 0 ? new Vector2(110f, 10f) : new Vector2(10f, 110f);
                    _corners[c * 2 + arm] = rt.GetComponent<Image>();
                }
            }

            // Hearts.
            _heartRow = new GameObject("Hearts").AddComponent<RectTransform>();
            _heartRow.SetParent(transform, false);
            _heartRow.anchorMin = _heartRow.anchorMax = _heartRow.pivot = new Vector2(0f, 1f);
            _heartRow.anchoredPosition = new Vector2(70f, -70f);
            _heartRow.sizeDelta = new Vector2(300f, 80f);
            _hearts = new Image[MaxHearts];
            for (int i = 0; i < MaxHearts; i++)
            {
                var img = new GameObject("Heart").AddComponent<Image>();
                img.transform.SetParent(_heartRow, false);
                img.sprite = _heart;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(i * 84f, 0f);
                rt.sizeDelta = new Vector2(72f, 66f);
                _hearts[i] = img;
            }

            // Score.
            _score = Label(transform, "Score", "", 40, TextAnchor.UpperRight, Ink);
            var srt = _score.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-70f, -70f);
            srt.sizeDelta = new Vector2(600f, 70f);

            // Hint line.
            var hintBack = Panel(transform, "Hint", new Color(1f, 1f, 1f, 0.82f));
            hintBack.anchorMin = hintBack.anchorMax = hintBack.pivot = new Vector2(0.5f, 0f);
            hintBack.anchoredPosition = new Vector2(0f, 220f);
            hintBack.sizeDelta = new Vector2(1100f, 76f);
            _hint = Label(hintBack, "Text", "", 32, TextAnchor.MiddleCenter, Ink);
            Stretch(_hint.rectTransform);

            // Snap button.
            SnapButton = RoundButton(transform, "Snap", new Vector2(0.5f, 0f), new Vector2(0f, 105f), 150f, out _snapLabel);

            // Retake button (only while tracing).
            RetakeButton = PillButton(transform, "Turn again", new Vector2(1f, 0f), new Vector2(-180f, 70f));
            RetakeButton.gameObject.SetActive(false);

            // Flash.
            var flash = Panel(transform, "Flash", new Color(1f, 1f, 1f, 0f));
            Stretch(flash);
            _flash = flash.GetComponent<Image>();
            _flash.raycastTarget = false;

            // Title card.
            _titleCard = Card("Title", out RectTransform titleBody);
            Label(titleBody, "Heading", "Iso Puzzle", 76, TextAnchor.UpperCenter, Ink, new Vector2(0f, -40f), new Vector2(900f, 110f));
            Label(titleBody, "Body",
                  "Every room hides one letter.\n\nDrag to turn the room. When the shapes line up into a letter, press Snap to take its picture, then trace the letter on the photo.\n\nA wrong snap or a wrong trace costs a heart. Tap a potion to get one back, but drink it in its room: potions can't be saved.",
                  30, TextAnchor.UpperCenter, Ink, new Vector2(0f, -160f), new Vector2(820f, 330f));
            PlayButton = PillButton(titleBody, "Play", new Vector2(0.5f, 0f), new Vector2(0f, 70f));

            // Game over card.
            _gameOverCard = Card("Game Over", out RectTransform overBody);
            Label(overBody, "Heading", "Out of hearts", 64, TextAnchor.UpperCenter, Ink, new Vector2(0f, -50f), new Vector2(900f, 100f));
            _gameOverText = Label(overBody, "Body", "", 36, TextAnchor.UpperCenter, Ink, new Vector2(0f, -190f), new Vector2(820f, 200f));
            AgainButton = PillButton(overBody, "Play again", new Vector2(0.5f, 0f), new Vector2(0f, 70f));
            _gameOverCard.SetActive(false);
        }

        void Update()
        {
            if (_flashAlpha > 0f)
            {
                _flashAlpha = Mathf.Max(0f, _flashAlpha - Time.deltaTime * 2.2f);
                _flash.color = new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flashAlpha);
            }
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - Time.deltaTime);
                float offset = Mathf.Sin(Time.time * 70f) * 14f * (_shake / 0.4f);
                _heartRow.anchoredPosition = new Vector2(70f + offset, -70f);
            }
        }

        // ------------------------------------------------------------------
        // What the game calls
        // ------------------------------------------------------------------

        public void SetHearts(int hearts)
        {
            for (int i = 0; i < _hearts.Length; i++)
                _hearts[i].color = i < hearts ? HeartFull : HeartEmpty;
        }

        public void ShakeHearts() => _shake = 0.4f;

        public void SetScore(int roomsCleared) => _score.text = "Rooms cleared: " + roomsCleared;

        public void SetHint(string text)
        {
            _hint.text = text;
            _hint.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>0 = far from the letter's view, 1 = lined up.</summary>
        public void SetAlignment(float closeness, bool aligned, bool visible)
        {
            Color c = aligned ? Gold : Color.Lerp(new Color(1f, 1f, 1f, 0.25f), new Color(1f, 0.92f, 0.7f, 0.9f), closeness);
            foreach (Image corner in _corners)
            {
                corner.enabled = visible;
                corner.color = c;
            }
            _snapLabel.color = aligned ? Gold : Color.white;
        }

        public void Flash(Color color, float strength = 1f)
        {
            _flashColor = color;
            _flashAlpha = strength;
            _flash.color = new Color(color.r, color.g, color.b, strength);
        }

        public void ShowPhoto(Texture texture)
        {
            Photo.texture = texture;
            _photoRoot.SetActive(texture != null);
            ClearTrace();
        }

        public void ClearTrace()
        {
            for (int i = TraceLayer.childCount - 1; i >= 0; i--)
                Destroy(TraceLayer.GetChild(i).gameObject);
        }

        /// <summary>A dot on the trace layer at a viewport position (0..1).</summary>
        public void Dot(Vector2 viewport, float size, Color color)
        {
            var img = new GameObject("Dot").AddComponent<Image>();
            img.transform.SetParent(TraceLayer, false);
            img.sprite = _circle;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = viewport;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
        }

        public void ShowTitle(bool show) => _titleCard.SetActive(show);

        public void ShowGameOver(int roomsCleared, int best)
        {
            _gameOverText.text = roomsCleared == 1
                ? $"You cleared 1 room.\nBest run: {best}"
                : $"You cleared {roomsCleared} rooms.\nBest run: {best}";
            _gameOverCard.SetActive(true);
        }

        public void HideGameOver() => _gameOverCard.SetActive(false);

        public void SetPlayingChrome(bool snapVisible, bool retakeVisible)
        {
            SnapButton.gameObject.SetActive(snapVisible);
            RetakeButton.gameObject.SetActive(retakeVisible);
        }

        // ------------------------------------------------------------------
        // Builders
        // ------------------------------------------------------------------

        GameObject Card(string name, out RectTransform body)
        {
            var dim = Panel(transform, name, new Color(0.1f, 0.08f, 0.16f, 0.55f));
            Stretch(dim);
            body = Panel(dim, "Card", new Color(1f, 0.98f, 0.94f, 0.97f));
            body.anchorMin = body.anchorMax = body.pivot = new Vector2(0.5f, 0.5f);
            body.sizeDelta = new Vector2(960f, 680f);
            return dim.gameObject;
        }

        Button RoundButton(Transform parent, string label, Vector2 anchor, Vector2 position, float size, out Text text)
        {
            var rt = Panel(parent, label + " Button", new Color(0.24f, 0.2f, 0.4f, 0.95f));
            rt.GetComponent<Image>().sprite = _circle;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position + new Vector2(0f, size * 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var button = rt.gameObject.AddComponent<Button>();
            text = Label(rt, "Label", label, 40, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        Button PillButton(Transform parent, string label, Vector2 anchor, Vector2 position)
        {
            var rt = Panel(parent, label + " Button", new Color(0.24f, 0.2f, 0.4f, 0.95f));
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(320f, 96f);
            var button = rt.gameObject.AddComponent<Button>();
            Text text = Label(rt, "Label", label, 38, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img.rectTransform;
        }

        Text Label(Transform parent, string name, string value, int size, TextAnchor align, Color color,
                   Vector2? position = null, Vector2? box = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (position.HasValue)
            {
                var rt = text.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = position.Value;
                rt.sizeDelta = box ?? new Vector2(800f, 100f);
            }
            return text;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void AddOutline(RectTransform frame, float width, Color color)
        {
            for (int side = 0; side < 4; side++)
            {
                var edge = Panel(frame, "Edge", color);
                edge.GetComponent<Image>().raycastTarget = false;
                bool horizontal = side < 2;
                edge.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side - 2, 0f);
                edge.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side - 2, 1f);
                edge.pivot = horizontal ? new Vector2(0.5f, side) : new Vector2(side - 2, 0.5f);
                edge.anchoredPosition = Vector2.zero;
                edge.sizeDelta = horizontal ? new Vector2(0f, width) : new Vector2(width, 0f);
            }
            frame.GetComponent<Image>().raycastTarget = false;
        }

        static Sprite MakeHeartSprite()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Classic implicit heart: (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0
                float fx = (x + 0.5f) / n * 2.6f - 1.3f;
                float fy = (y + 0.5f) / n * 2.6f - 1.25f;
                float a = fx * fx + fy * fy - 1f;
                float v = a * a * a - fx * fx * fy * fy * fy;
                float alpha = Mathf.Clamp01(-v * 40f + 0.5f);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeCircleSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float alpha = Mathf.Clamp01(r - d);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}

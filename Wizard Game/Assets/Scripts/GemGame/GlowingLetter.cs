using System.Collections;
using TMPro;
using UnityEngine;
using OtherwiseLabs.BrewingGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// The prize of a finished stencil: the gems fuse into one glowing letter
    /// that floats over the scroll. Tap it and the player carries it (it rides
    /// in front of the camera, lower right, like a held lantern); tap the
    /// mortar and it arcs in. A golden glyph, a halo and a small point light
    /// — no prefab needed.
    /// </summary>
    public class GlowingLetter : MonoBehaviour
    {
        public enum State { Floating, Carried, Dropping }

        public State CurrentState { get; private set; }
        public char Letter { get; private set; }

        Vector3 _home;
        TMP_Text _glyph;
        SpriteRenderer _halo;
        Light _light;
        SphereCollider _tap;
        float _seed;

        public static GlowingLetter Create(char letter, Vector3 position, Color color)
        {
            var go = new GameObject($"Glowing Letter {letter}");
            var glowing = go.AddComponent<GlowingLetter>();
            glowing.Build(letter, position, color);
            return glowing;
        }

        void Build(char letter, Vector3 position, Color color)
        {
            Letter = char.ToUpperInvariant(letter);
            _home = position;
            _seed = Random.value * 10f;
            transform.position = position;
            Color gold = Color.Lerp(color, new Color(1f, 0.9f, 0.45f, 1f), 0.55f);

            var glyphGo = new GameObject("Glyph");
            glyphGo.transform.SetParent(transform, false);
            _glyph = glyphGo.AddComponent<TextMeshPro>();
            _glyph.text = Letter.ToString();
            _glyph.fontSize = 3.4f;
            _glyph.fontStyle = FontStyles.Bold;
            _glyph.alignment = TextAlignmentOptions.Center;
            _glyph.color = gold;
            _glyph.outlineWidth = 0.2f;
            _glyph.outlineColor = new Color32(255, 250, 220, 255);
            _glyph.rectTransform.sizeDelta = new Vector2(1.2f, 1.2f);
            glyphGo.AddComponent<BillboardLabel>();

            var haloGo = new GameObject("Halo");
            haloGo.transform.SetParent(transform, false);
            _halo = haloGo.AddComponent<SpriteRenderer>();
            _halo.sprite = PotionIngredient.HaloSprite();
            _halo.color = new Color(gold.r, gold.g, gold.b, 0.7f);
            haloGo.transform.localScale = Vector3.one * 0.9f;
            haloGo.AddComponent<BillboardLabel>();

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = gold;
            _light.range = 2.2f;
            _light.intensity = 1.6f;

            _tap = gameObject.AddComponent<SphereCollider>();
            _tap.radius = 0.35f;
            _tap.isTrigger = true;

            StartCoroutine(PopIn());
        }

        IEnumerator PopIn()
        {
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float u = t / 0.35f;
                transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(u * Mathf.PI)) * u;
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f + _seed);
            if (_light != null) _light.intensity = 1.6f * pulse;
            if (_halo != null) _halo.transform.localScale = Vector3.one * (0.9f * pulse);

            if (CurrentState == State.Floating)
            {
                transform.position = _home + Vector3.up * (Mathf.Sin(Time.time * 1.6f + _seed) * 0.04f);
            }
            else if (CurrentState == State.Carried)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                Transform c = cam.transform;
                Vector3 target = c.position + c.forward * 0.85f + c.right * 0.38f - c.up * 0.26f
                                 + c.up * (Mathf.Sin(Time.time * 2.2f + _seed) * 0.012f);
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
            }
        }

        /// <summary>Picked up: shrink to lantern size and follow the camera.</summary>
        public void PickUp()
        {
            if (CurrentState != State.Floating) return;
            CurrentState = State.Carried;
            _tap.enabled = false;   // never block taps on the mortar while carried
            StartCoroutine(ScaleTo(0.4f, 0.25f));
        }

        /// <summary>Arcs into the mortar's mouth, sinks, and reports back.</summary>
        public void DropInto(Vector3 mouth, System.Action onLanded)
        {
            if (CurrentState != State.Carried) return;
            CurrentState = State.Dropping;
            StopAllCoroutines();
            StartCoroutine(DropRoutine(mouth, onLanded));
        }

        IEnumerator DropRoutine(Vector3 mouth, System.Action onLanded)
        {
            Vector3 start = transform.position;
            float startScale = transform.localScale.x;
            float arc = Mathf.Max(0.5f, Vector3.Distance(start, mouth) * 0.3f);
            const float seconds = 0.6f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                Vector3 pos = Vector3.Lerp(start, mouth + Vector3.up * 0.1f, u);
                pos.y += Mathf.Sin(u * Mathf.PI) * arc;
                transform.position = pos;
                transform.localScale = Vector3.one * Mathf.Lerp(startScale, 0.55f, u);
                yield return null;
            }
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                float u = t / 0.25f;
                transform.position = Vector3.Lerp(mouth + Vector3.up * 0.1f, mouth - Vector3.up * 0.05f, u);
                transform.localScale = Vector3.one * (0.55f * (1f - u));
                yield return null;
            }
            onLanded?.Invoke();
            Destroy(gameObject);
        }

        IEnumerator ScaleTo(float scale, float seconds)
        {
            float start = transform.localScale.x;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.localScale = Vector3.one * Mathf.Lerp(start, scale, t / seconds);
                yield return null;
            }
            transform.localScale = Vector3.one * scale;
        }
    }
}

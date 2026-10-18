using System.Collections;
using UnityEngine;
using OtherwiseLabs.BrewingGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// One gem for the stencil: a crystal from the store's gem pack, sized to
    /// sit in a vertex ring. It waits in a row at the table's edge, follows
    /// the pointer while dragged, snaps into a ring, and glows (halo + pulse)
    /// once every ring holds a gem. All colliders and the halo are added at
    /// runtime; the crystal prefabs stay untouched.
    /// </summary>
    public class StencilGem : MonoBehaviour
    {
        public enum State { Pile, Held, Placed, Flying }

        public State CurrentState { get; private set; }

        /// <summary>The vertex ring this gem sits in; -1 while in the row.</summary>
        public int Vertex { get; private set; } = -1;

        Vector3 _pileHome;
        float _bobSeed;
        float _baseScale = 1f;
        bool _selected;
        bool _glowing;
        float _glow;
        SpriteRenderer _halo;
        float _haloScale = 1f;
        Color _glowColor = Color.white;

        public void Setup(Vector3 pileHome, Color color, float targetSize)
        {
            _pileHome = pileHome;
            _bobSeed = Random.value * 20f;
            _glowColor = Color.Lerp(color, Color.white, 0.35f);

            // Normalize whatever crystal model this is to a hand-sized gem.
            Bounds bounds = WorldBounds();
            float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (extent > 0.001f) transform.localScale *= targetSize / extent;
            _baseScale = transform.localScale.x;
            transform.position = pileHome;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            bounds = WorldBounds();
            var grab = gameObject.AddComponent<SphereCollider>();
            grab.center = transform.InverseTransformPoint(bounds.center);
            grab.radius = Mathf.Max(bounds.extents.magnitude / Mathf.Max(_baseScale, 0.0001f), 0.05f) * 1.4f;
            grab.isTrigger = true;

            var haloGo = new GameObject("Halo");
            haloGo.transform.SetParent(transform, false);
            haloGo.transform.localPosition = transform.InverseTransformPoint(bounds.center);
            _halo = haloGo.AddComponent<SpriteRenderer>();
            _halo.sprite = PotionIngredient.HaloSprite();
            _halo.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b, 0f);
            _haloScale = Mathf.Max(bounds.size.magnitude, 0.08f) * 2.6f / Mathf.Max(_baseScale, 0.0001f);
            haloGo.transform.localScale = Vector3.zero;
            haloGo.AddComponent<BillboardLabel>();
        }

        Bounds WorldBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        void Update()
        {
            UpdateGlow();
            if (CurrentState == State.Pile)
            {
                Vector3 pos = _pileHome;
                pos.y += (_selected ? 0.05f : 0f) + Mathf.Sin(Time.time * 1.7f + _bobSeed) * 0.01f;
                transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-14f * Time.deltaTime));
                transform.Rotate(0f, (_selected ? 70f : 16f) * Time.deltaTime, 0f, Space.World);
            }
            else if (CurrentState == State.Placed && _glowing)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f + _bobSeed);
                transform.localScale = Vector3.one * (_baseScale * pulse);
                transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            }
        }

        void UpdateGlow()
        {
            float target = _glowing ? 1f : (_selected || CurrentState == State.Held) ? 0.55f : 0f;
            if (_glow == 0f && target == 0f) return;
            _glow = Mathf.MoveTowards(_glow, target, Time.deltaTime * 4f);
            if (_halo == null) return;
            float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 5.5f + _bobSeed);
            Color c = _glowColor;
            c.a = 0.75f * _glow * pulse;
            _halo.color = c;
            _halo.transform.localScale = Vector3.one * (_haloScale * (0.8f + 0.2f * pulse) * _glow);
        }

        /// <summary>Tapped in the row: lift and sparkle, waiting for a ring to be tapped.</summary>
        public void SetSelected(bool selected) => _selected = selected && CurrentState == State.Pile;

        public void BeginHold()
        {
            if (CurrentState == State.Flying) return;
            _selected = false;
            CurrentState = State.Held;
        }

        public void UpdateHold(Vector3 worldPoint)
        {
            if (CurrentState != State.Held) return;
            transform.position = Vector3.Lerp(transform.position, worldPoint, 1f - Mathf.Exp(-16f * Time.deltaTime));
        }

        /// <summary>Snaps into a vertex ring with a little hop.</summary>
        public void PlaceAt(int vertex, Vector3 world)
        {
            _selected = false;
            Vertex = vertex;
            CurrentState = State.Placed;
            StopAllCoroutines();
            StartCoroutine(Hop(world));
        }

        IEnumerator Hop(Vector3 target)
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float u = t / 0.18f;
                Vector3 pos = Vector3.Lerp(start, target, u);
                pos.y += Mathf.Sin(u * Mathf.PI) * 0.06f;
                transform.position = pos;
                yield return null;
            }
            transform.position = target;
        }

        public void ReturnToPile()
        {
            if (CurrentState == State.Flying) return;
            StopAllCoroutines();
            CurrentState = State.Pile;
            Vertex = -1;
            _selected = false;
            SetGlowing(false);
        }

        public void SetGlowing(bool glowing)
        {
            if (_glowing == glowing) return;
            _glowing = glowing;
            if (!glowing) transform.localScale = Vector3.one * _baseScale;
        }

        /// <summary>Flies into the glowing letter (the fuse) and vanishes.</summary>
        public void FlyTo(Vector3 target, float delay, System.Action onArrived)
        {
            if (CurrentState == State.Flying) return;
            CurrentState = State.Flying;
            StopAllCoroutines();
            StartCoroutine(FlyRoutine(target, delay, onArrived));
        }

        IEnumerator FlyRoutine(Vector3 target, float delay, System.Action onArrived)
        {
            yield return new WaitForSeconds(delay);
            Vector3 start = transform.position;
            const float seconds = 0.55f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                Vector3 pos = Vector3.Lerp(start, target, u * u);
                pos.y += Mathf.Sin(u * Mathf.PI) * 0.15f;
                transform.position = pos;
                transform.localScale = Vector3.one * (_baseScale * (1f - 0.8f * u));
                transform.Rotate(0f, 400f * Time.deltaTime, 0f, Space.World);
                yield return null;
            }
            onArrived?.Invoke();
            Destroy(gameObject);
        }
    }
}

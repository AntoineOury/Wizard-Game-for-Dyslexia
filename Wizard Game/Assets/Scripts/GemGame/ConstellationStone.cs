using System.Collections;
using TMPro;
using UnityEngine;
using OtherwiseLabs.BrewingGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// One placeable constellation stone: a small crystal (spawned from the
    /// store's gem pack) the player drags from the pile onto the gem table.
    /// Placed stones remember their placement order — the constellation lines
    /// connect them in that order — and glow together when the shape reads as
    /// the target letter. All colliders, labels and sizing are added at
    /// runtime; the crystal prefabs stay untouched.
    /// </summary>
    public class ConstellationStone : MonoBehaviour
    {
        public enum State { Pile, Held, Placed, Flying }

        public State CurrentState { get; private set; }

        /// <summary>Placement order (0-based); -1 while in the pile.</summary>
        public int Order { get; private set; } = -1;

        /// <summary>Position in letter-unit space while placed.</summary>
        public Vector2 TableUnit { get; set; }

        GemConstellationController _game;
        Vector3 _pileHome;
        float _bobSeed;
        float _baseScale = 1f;
        bool _celebrating;
        TMP_Text _orderLabel;
        Transform _labelRoot;

        public void Setup(GemConstellationController game, Vector3 pileHome, Color accent, float targetHeight)
        {
            _game = game;
            _pileHome = pileHome;
            _bobSeed = Random.value * 20f;

            // Normalize whatever crystal model this is to a hand-sized stone.
            Bounds bounds = WorldBounds();
            if (bounds.size.y > 0.001f)
                transform.localScale *= targetHeight / bounds.size.y;
            _baseScale = transform.localScale.x;
            transform.position = pileHome;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            bounds = WorldBounds();
            var grab = gameObject.AddComponent<SphereCollider>();
            grab.center = transform.InverseTransformPoint(bounds.center);
            grab.radius = Mathf.Max(bounds.extents.magnitude / Mathf.Max(_baseScale, 0.0001f), 0.06f) * 1.35f;
            grab.isTrigger = true;

            // Order number above the stone (counter-scaled: crystals shrink a lot).
            _labelRoot = new GameObject("Order").transform;
            _labelRoot.SetParent(transform, false);
            _labelRoot.localScale = Vector3.one / Mathf.Max(_baseScale, 0.0001f);
            _labelRoot.localPosition = transform.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + Vector3.up * 0.055f);
            _orderLabel = _labelRoot.gameObject.AddComponent<TextMeshPro>();
            _orderLabel.text = "";
            _orderLabel.fontSize = 0.85f;
            _orderLabel.fontStyle = FontStyles.Bold;
            _orderLabel.alignment = TextAlignmentOptions.Center;
            _orderLabel.color = Color.Lerp(accent, Color.white, 0.25f);
            _orderLabel.rectTransform.sizeDelta = new Vector2(0.6f, 0.25f);
            _labelRoot.gameObject.AddComponent<BillboardLabel>();
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
            if (CurrentState == State.Pile)
            {
                Vector3 pos = _pileHome;
                pos.y += Mathf.Sin(Time.time * 1.7f + _bobSeed) * 0.012f;
                transform.position = pos;
                transform.Rotate(0f, 16f * Time.deltaTime, 0f, Space.World);
            }
            else if (CurrentState == State.Placed && _celebrating)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f + _bobSeed);
                transform.localScale = Vector3.one * (_baseScale * pulse);
                transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            }
        }

        public void BeginHold()
        {
            if (CurrentState == State.Flying) return;
            CurrentState = State.Held;
        }

        public void UpdateHold(Vector3 worldPoint)
        {
            if (CurrentState != State.Held) return;
            transform.position = Vector3.Lerp(transform.position, worldPoint, 1f - Mathf.Exp(-16f * Time.deltaTime));
        }

        /// <summary>Snapped onto the table at a letter-space position.</summary>
        public void PlaceAt(Vector3 world, Vector2 unit, int order)
        {
            CurrentState = State.Placed;
            transform.position = world;
            TableUnit = unit;
            SetOrder(order);
        }

        public void SetOrder(int order)
        {
            Order = order;
            if (_orderLabel != null) _orderLabel.text = order >= 0 ? (order + 1).ToString() : "";
        }

        public void ReturnToPile()
        {
            if (CurrentState == State.Flying) return;
            CurrentState = State.Pile;
            SetOrder(-1);
            SetCelebrating(false);
            transform.position = _pileHome;
        }

        public void SetCelebrating(bool celebrating)
        {
            if (_celebrating == celebrating) return;
            _celebrating = celebrating;
            if (!celebrating && CurrentState == State.Placed)
                transform.localScale = Vector3.one * _baseScale;
        }

        /// <summary>Arc into the mortar and vanish (the grind).</summary>
        public void FlyTo(Vector3 target, float delay, System.Action onArrived)
        {
            if (CurrentState == State.Flying) return;
            CurrentState = State.Flying;
            SetCelebrating(false);
            if (_labelRoot != null) _labelRoot.gameObject.SetActive(false);
            StartCoroutine(FlyRoutine(target, delay, onArrived));
        }

        IEnumerator FlyRoutine(Vector3 target, float delay, System.Action onArrived)
        {
            yield return new WaitForSeconds(delay);
            Vector3 start = transform.position;
            float seconds = Mathf.Clamp(Vector3.Distance(start, target) * 0.12f, 0.35f, 0.9f);
            float arc = Mathf.Max(0.8f, Vector3.Distance(start, target) * 0.25f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                Vector3 pos = Vector3.Lerp(start, target, u);
                pos.y += Mathf.Sin(u * Mathf.PI) * arc;
                transform.position = pos;
                transform.localScale = Vector3.one * (_baseScale * (1f - 0.65f * u));
                transform.Rotate(220f * Time.deltaTime, 60f * Time.deltaTime, 0f);
                yield return null;
            }
            onArrived?.Invoke();
            Destroy(gameObject);
        }
    }
}

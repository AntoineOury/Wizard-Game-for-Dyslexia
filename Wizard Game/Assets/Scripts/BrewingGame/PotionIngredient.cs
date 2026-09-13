using System.Collections;
using TMPro;
using UnityEngine;

namespace OtherwiseLabs.BrewingGame
{
    /// <summary>
    /// One ingredient bottle on the shelf, carrying one letter. The visual is
    /// a pack potion prefab spawned by the controller; this component adds the
    /// floating letter label, a grab collider, the idle bob, and the little
    /// journeys: following the finger, arcing into the cauldron, or gliding
    /// home to its shelf slot after a change of heart.
    ///
    /// Tap = throw it in. Drag = carry it; release over the cauldron pours,
    /// anywhere else returns it. (Both inputs, per the design flowchart.)
    /// </summary>
    public class PotionIngredient : MonoBehaviour
    {
        public char Letter { get; private set; }
        public State CurrentState { get; private set; }

        public enum State { Shelved, Held, Flying, Consumed }

        BrewingGameController _game;
        Vector3 _home;
        float _bobSeed;
        Transform _label;
        float _baseScale = 1f;

        public void Setup(char letter, BrewingGameController game, Vector3 home, Color tint, bool applyTint)
        {
            Letter = char.ToUpperInvariant(letter);
            _game = game;
            _home = home;
            _bobSeed = Random.value * 20f;
            _baseScale = transform.localScale.x;
            transform.position = home;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Grab collider sized from what the model actually looks like.
            Bounds bounds = ComputeLocalBounds();
            var grab = gameObject.AddComponent<SphereCollider>();
            grab.center = bounds.center;
            grab.radius = Mathf.Max(bounds.extents.magnitude, 0.18f) * 1.25f;
            grab.isTrigger = true;

            if (applyTint)
            {
                var block = new MaterialPropertyBlock();
                Color soft = Color.Lerp(Color.white, tint, 0.45f);
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", soft);
                    block.SetColor("_Color", soft);
                    renderer.SetPropertyBlock(block);
                }
            }

            // The floating letter so players know which potion is which.
            var labelGo = new GameObject($"Letter {Letter}");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, bounds.max.y + 0.16f, 0f);
            var text = labelGo.AddComponent<TextMeshPro>();
            text.text = Letter.ToString();
            text.fontSize = 1.7f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.Lerp(tint, Color.white, 0.2f);
            text.outlineWidth = 0.22f;
            text.rectTransform.sizeDelta = new Vector2(2f, 1f);
            labelGo.AddComponent<BillboardLabel>();
            _label = labelGo.transform;
        }

        Bounds ComputeLocalBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.3f);
            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);
            return new Bounds(transform.InverseTransformPoint(world.center),
                              transform.InverseTransformVector(world.size));
        }

        void Update()
        {
            if (CurrentState != State.Shelved) return;
            // A gentle shelf bob so the ingredients read as magical and alive.
            Vector3 pos = _home;
            pos.y += Mathf.Sin(Time.time * 1.6f + _bobSeed) * 0.035f;
            transform.position = pos;
            transform.Rotate(0f, 12f * Time.deltaTime, 0f, Space.World);
        }

        public void BeginHold()
        {
            if (CurrentState != State.Shelved) return;
            CurrentState = State.Held;
        }

        /// <summary>Follow the pointer while held (world point fed by the controller).</summary>
        public void UpdateHold(Vector3 worldPoint)
        {
            if (CurrentState != State.Held) return;
            transform.position = Vector3.Lerp(transform.position, worldPoint, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        /// <summary>Released: pour if asked (tap or dropped on the pot), else glide home.</summary>
        public void Release(bool pour)
        {
            if (CurrentState != State.Held) return;
            if (pour) FlyToCauldron();
            else StartCoroutine(ReturnHome());
        }

        public void FlyToCauldron()
        {
            if (CurrentState == State.Flying || CurrentState == State.Consumed) return;
            CurrentState = State.Flying;
            StartCoroutine(FlyRoutine());
        }

        IEnumerator FlyRoutine()
        {
            Vector3 start = transform.position;
            Vector3 target = _game.Cauldron.Mouth;
            float seconds = Mathf.Clamp(Vector3.Distance(start, target) * 0.14f, 0.35f, 0.9f);
            float arc = Mathf.Max(1.1f, Vector3.Distance(start, target) * 0.35f);

            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                Vector3 pos = Vector3.Lerp(start, target, u);
                pos.y += Mathf.Sin(u * Mathf.PI) * arc;
                transform.position = pos;
                transform.Rotate(260f * Time.deltaTime, 40f * Time.deltaTime, 0f);
                yield return null;
            }

            // Dive into the brew and vanish.
            CurrentState = State.Consumed;
            if (_label != null) _label.gameObject.SetActive(false);
            Vector3 mouth = _game.Cauldron.Mouth;
            for (float t = 0f; t < 0.22f; t += Time.deltaTime)
            {
                float u = t / 0.22f;
                transform.position = Vector3.Lerp(mouth, mouth + Vector3.down * 0.35f, u);
                transform.localScale = Vector3.one * (_baseScale * (1f - u));
                yield return null;
            }
            _game.NotifyPoured(this);
            Destroy(gameObject);
        }

        IEnumerator ReturnHome()
        {
            CurrentState = State.Flying;
            Vector3 start = transform.position;
            float seconds = Mathf.Clamp(Vector3.Distance(start, _home) * 0.12f, 0.2f, 0.7f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, _home, t / seconds);
                yield return null;
            }
            transform.position = _home;
            CurrentState = State.Shelved;
        }
    }

    /// <summary>Keeps a world-space label facing the camera. Runtime-only helper.</summary>
    public class BillboardLabel : MonoBehaviour
    {
        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}

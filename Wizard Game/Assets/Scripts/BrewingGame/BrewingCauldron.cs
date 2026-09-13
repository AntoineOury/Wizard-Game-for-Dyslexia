using System.Collections;
using TMPro;
using UnityEngine;

namespace OtherwiseLabs.BrewingGame
{
    /// <summary>
    /// Runtime brain attached to the cauldron model (the store's "boiler"
    /// prefab — no prefab edits, everything is added when the scene starts).
    /// Its one gameplay job is the flowchart's feedback beat: every poured
    /// ingredient is PUFFED back out as a big smoky letter, so the player
    /// always sees the sequence they actually built. It also throws the
    /// success burst (the whole word rises) and the gray fail fizzle.
    /// </summary>
    public class BrewingCauldron : MonoBehaviour
    {
        public Bounds WorldBounds { get; private set; }

        /// <summary>Where poured potions dive and puffs rise from.</summary>
        public Vector3 Mouth { get; private set; }

        /// <summary>How close a dragged potion must get to count as a pour.</summary>
        public float PourRadius { get; private set; }

        Color _puffColor = new Color(0.92f, 0.88f, 0.98f, 1f);

        public void Bind(Color puffColor)
        {
            _puffColor = puffColor;

            WorldBounds = ComputeBounds();
            Mouth = new Vector3(WorldBounds.center.x, WorldBounds.max.y, WorldBounds.center.z);
            PourRadius = Mathf.Max(WorldBounds.extents.x, WorldBounds.extents.z) + 0.45f;

            // The pack prefabs ship without physics: give the pot a solid body
            // so the player can't wade through it while brewing.
            if (GetComponent<Collider>() == null)
            {
                var solid = gameObject.AddComponent<BoxCollider>();
                solid.center = transform.InverseTransformPoint(WorldBounds.center);
                Vector3 size = transform.InverseTransformVector(WorldBounds.size);
                solid.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            }
        }

        Bounds ComputeBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>An ingredient landed: puff its letter out of the brew.</summary>
        public void Pour(char letter)
        {
            StartCoroutine(PuffRoutine(letter.ToString(), _puffColor, 5.2f, 1.35f, 2.2f));
        }

        /// <summary>The whole word rises when the potion is brewed right.</summary>
        public void Celebrate(string word)
        {
            StartCoroutine(PuffRoutine(word.ToUpperInvariant(), new Color(1f, 0.92f, 0.45f, 1f), 4.2f, 2.6f, 3.4f));
        }

        /// <summary>Wrong sequence: a gray, sinking fizzle.</summary>
        public void Fizzle()
        {
            StartCoroutine(PuffRoutine("...", new Color(0.5f, 0.5f, 0.5f, 1f), 3f, 0.8f, 1.4f));
        }

        IEnumerator PuffRoutine(string text, Color color, float size, float rise, float seconds)
        {
            var go = new GameObject($"Puff {text}");
            go.transform.position = Mouth + Vector3.up * 0.15f;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            var rect = label.rectTransform;
            rect.sizeDelta = new Vector2(6f, 3f);

            float wobbleSeed = Random.value * 10f;
            Vector3 start = go.transform.position;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                float ease = 1f - (1f - u) * (1f - u);
                Vector3 pos = start + Vector3.up * (rise * ease);
                pos.x += Mathf.Sin((t + wobbleSeed) * 2.1f) * 0.08f;
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.15f, ease);

                Camera cam = Camera.main;
                if (cam != null)
                    go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);

                Color faded = color;
                faded.a = u < 0.12f ? u / 0.12f : Mathf.Clamp01(1f - (u - 0.55f) / 0.45f);
                label.color = faded;
                yield return null;
            }
            Destroy(go);
        }
    }
}

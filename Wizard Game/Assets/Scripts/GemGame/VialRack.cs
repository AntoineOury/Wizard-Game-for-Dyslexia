using System.Collections.Generic;
using TMPro;
using UnityEngine;
using OtherwiseLabs.BrewingGame;
using OtherwiseLabs.CreatureGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// The vials the player has made, standing on the potion shelf: one
    /// bottle per letter owned, labeled with the letter (and "x3" when there
    /// are several). It mirrors <see cref="VialInventory"/> and rebuilds
    /// whenever that changes, so a vial made at the mortar appears for good
    /// and a vial the cauldron spends disappears.
    ///
    /// The rack takes the end of the brewing game's second shelf that
    /// <see cref="BrewingGameController.vialRackShare"/> reserves, so it
    /// never overlaps the recipe bottles. Without a brewing game it uses the
    /// whole of the shelf it was given.
    /// </summary>
    public class VialRack : MonoBehaviour
    {
        GameObject _vialPrefab;
        Transform _fallbackShelf;
        BrewingGameController _brewing;
        readonly List<GameObject> _bottles = new List<GameObject>();

        public void Setup(GameObject vialPrefab, Transform fallbackShelf)
        {
            _vialPrefab = vialPrefab;
            _fallbackShelf = fallbackShelf;
            _brewing = FindObjectOfType<BrewingGameController>();
            VialInventory.Changed += Rebuild;
            Rebuild();
        }

        void OnDestroy() => VialInventory.Changed -= Rebuild;

        /// <summary>Where a vial of this letter stands (or will stand once added).</summary>
        public Vector3 SlotFor(char letter)
        {
            List<char> letters = OwnedLetters(char.ToUpperInvariant(letter));
            int index = letters.IndexOf(char.ToUpperInvariant(letter));
            return SlotPosition(index, letters.Count, out _);
        }

        void Rebuild()
        {
            foreach (GameObject bottle in _bottles) if (bottle != null) Destroy(bottle);
            _bottles.Clear();

            List<char> letters = OwnedLetters('\0');
            for (int i = 0; i < letters.Count; i++)
            {
                char letter = letters[i];
                Vector3 position = SlotPosition(i, letters.Count, out float height);
                GameObject bottle = BuildVial(_vialPrefab, letter, height);
                bottle.transform.SetParent(transform, true);
                bottle.transform.position = position;

                int count = VialInventory.Count(letter);
                if (count > 1) AddCountLabel(bottle, count, height);
                _bottles.Add(bottle);
            }
        }

        static List<char> OwnedLetters(char include)
        {
            var letters = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++)
                if (VialInventory.Count(c) > 0 || c == include) letters.Add(c);
            return letters;
        }

        /// <summary>Evenly spaced along the rack; bottles shrink a little when many letters share it.</summary>
        Vector3 SlotPosition(int index, int count, out float height)
        {
            if (!RackLine(out Vector3 start, out Vector3 end))
            {
                height = 0.22f;
                return transform.position + Vector3.right * (0.25f * index);
            }
            count = Mathf.Max(1, count);
            float spacing = Vector3.Distance(start, end) / count;
            height = Mathf.Clamp(spacing * 2f, 0.1f, 0.24f);
            return Vector3.Lerp(start, end, (index + 0.5f) / count);
        }

        bool RackLine(out Vector3 start, out Vector3 end)
        {
            if (_brewing != null && _brewing.TryGetVialRack(out start, out end)) return true;
            start = end = Vector3.zero;
            if (_fallbackShelf == null) return false;
            Bounds bounds = BrewingGameController.ShelfBounds(_fallbackShelf);
            Vector3 along = bounds.size.x >= bounds.size.z
                ? Vector3.right * bounds.extents.x
                : Vector3.forward * bounds.extents.z;
            Vector3 top = new Vector3(bounds.center.x, bounds.max.y + 0.01f, bounds.center.z);
            start = top - along * 0.8f;
            end = top + along * 0.8f;
            return true;
        }

        /// <summary>
        /// A letter vial: the potion model normalized to <paramref name="height"/>,
        /// standing on its base, tinted by letter like the brewing bottles, with
        /// the letter floating above. Shared with the vial that flies from the mortar.
        /// </summary>
        public static GameObject BuildVial(GameObject prefab, char letter, float height)
        {
            GameObject vial = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vial.name = $"Vial {letter}";
            foreach (Collider collider in vial.GetComponentsInChildren<Collider>())
                Destroy(collider);

            // A root at the bottle's base, so placing it at a shelf point stands it on the shelf.
            var root = new GameObject($"Vial {letter}");
            Color tint = Color.Lerp(Color.white, LetterColor(letter), 0.45f);
            var renderers = vial.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.y > 0.001f) vial.transform.localScale *= height / bounds.size.y;
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                vial.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                var block = new MaterialPropertyBlock();
                foreach (Renderer renderer in renderers)
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", tint);
                    block.SetColor("_Color", tint);
                    renderer.SetPropertyBlock(block);
                }
            }
            vial.transform.SetParent(root.transform, false);

            var labelGo = new GameObject("Letter");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = Vector3.up * (height + 0.1f);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.text = letter.ToString();
            label.fontSize = 1.4f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.Lerp(LetterColor(letter), Color.white, 0.2f);
            label.outlineWidth = 0.22f;
            label.rectTransform.sizeDelta = new Vector2(1f, 0.5f);
            labelGo.AddComponent<BillboardLabel>();
            return root;
        }

        static void AddCountLabel(GameObject bottle, int count, float height)
        {
            var go = new GameObject("Count");
            go.transform.SetParent(bottle.transform, false);
            go.transform.localPosition = Vector3.up * (height + 0.02f);
            var label = go.AddComponent<TextMeshPro>();
            label.text = $"x{count}";
            label.fontSize = 0.8f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.92f, 0.6f, 1f);
            label.outlineWidth = 0.22f;
            label.rectTransform.sizeDelta = new Vector2(0.6f, 0.3f);
            go.AddComponent<BillboardLabel>();
        }

        /// <summary>Same letter colors as the brewing bottles and the gem rounds.</summary>
        public static Color LetterColor(char letter)
        {
            float hue = ((char.ToUpperInvariant(letter) - 'A') * 7 % 26) / 26f;
            return Color.HSVToRGB(hue, 0.55f, 1f);
        }
    }
}

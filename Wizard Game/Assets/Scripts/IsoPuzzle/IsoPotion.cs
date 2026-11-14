using UnityEngine;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// The healing potion that sometimes drops as loot when a room is cleared.
    /// It is used on the spot (one heart back); potions cannot be carried, so
    /// with full hearts it fizzles away.
    /// </summary>
    public class IsoPotion : MonoBehaviour
    {
        static readonly Color GlassColor = new Color(1f, 0.42f, 0.62f);
        static readonly Color CorkColor = new Color(0.62f, 0.42f, 0.26f);

        Vector3 _basePosition;
        float _phase;
        bool _drunk;
        float _drinkT;
        Vector3 _startScale;

        public bool IsDrunk => _drunk;
        /// <summary>When false the potion stops its idle bob, so a script can move it (the loot drop).</summary>
        public bool Idle { get; set; } = true;

        public static IsoPotion Create(Transform parent, Vector3 groundPosition)
        {
            var root = new GameObject("Potion");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = groundPosition + Vector3.up * 0.55f;

            Material glass = IsoShapes.Glow(GlassColor);
            Material cork = IsoShapes.Lit(CorkColor);

            var bulb = IsoShapes.Ball(0.38f, glass, root.transform);
            bulb.transform.localPosition = Vector3.zero;
            var neck = IsoShapes.Cylinder(0.13f, 0.34f, glass, root.transform, 10);
            neck.transform.localPosition = new Vector3(0f, 0.44f, 0f);
            var stopper = IsoShapes.Cylinder(0.16f, 0.14f, cork, root.transform, 10);
            stopper.transform.localPosition = new Vector3(0f, 0.66f, 0f);

            // Generous tap target: small fingers, small potion.
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.75f;
            collider.center = new Vector3(0f, 0.2f, 0f);

            var potion = root.AddComponent<IsoPotion>();
            potion._basePosition = root.transform.localPosition;
            potion._phase = Random.value * 10f;
            return potion;
        }

        /// <summary>Plays the drink animation, then removes the potion.</summary>
        public void Drink()
        {
            if (_drunk) return;
            _drunk = true;
            _startScale = transform.localScale;
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        void Update()
        {
            if (_drunk)
            {
                _drinkT += Time.deltaTime / 0.45f;
                transform.localScale = _startScale * Mathf.Lerp(1.25f, 0f, _drinkT);
                transform.localPosition += Vector3.up * Time.deltaTime * 2f;
                if (_drinkT >= 1f) Destroy(gameObject);
                return;
            }

            if (!Idle) return;
            _phase += Time.deltaTime;
            transform.localPosition = _basePosition + Vector3.up * (Mathf.Sin(_phase * 2.2f) * 0.12f);
            transform.localRotation = Quaternion.Euler(0f, _phase * 40f, 0f);
        }
    }
}

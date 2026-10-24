using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// Which props a hidden letter can be built from, one list per letter
    /// size. Any prefab works: the factory measures it, scales it to the dot
    /// size the letter needs at that distance, strips its colliders and moves
    /// built-in "Standard" materials onto URP Lit — so add or swap props here
    /// freely without resizing them first.
    ///
    /// The game loads the copy at Resources/LetterLensPalette. Empty slots and
    /// an empty list are fine: missing props fall back to plain stones.
    /// </summary>
    [CreateAssetMenu(menuName = "Otherwise Labs/Letter Lens Palette", fileName = "LetterLensPalette")]
    public class LetterLensPalette : ScriptableObject
    {
        public const string ResourcePath = "LetterLensPalette";

        [Tooltip("Props for bush-sized letters (about a meter tall, a few steps away): mushrooms, pebbles, small gems.")]
        public List<GameObject> bushProps = new List<GameObject>();

        [Tooltip("Props for house-sized letters (about 5 m tall): rocks, stumps, barrels, crystal clusters.")]
        public List<GameObject> houseProps = new List<GameObject>();

        [Tooltip("Props for building-sized letters (about 14 m tall, far away): boulders, big crystal clusters.")]
        public List<GameObject> buildingProps = new List<GameObject>();

        public IReadOnlyList<GameObject> PropsFor(LetterSize size)
        {
            switch (size)
            {
                case LetterSize.Bush: return bushProps;
                case LetterSize.House: return houseProps;
                default: return buildingProps;
            }
        }

        static LetterLensPalette _loaded;
        static bool _triedLoad;

        public static LetterLensPalette Load()
        {
            if (!_triedLoad)
            {
                _triedLoad = true;
                _loaded = Resources.Load<LetterLensPalette>(ResourcePath);
            }
            return _loaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _loaded = null;
            _triedLoad = false;
        }
    }
}

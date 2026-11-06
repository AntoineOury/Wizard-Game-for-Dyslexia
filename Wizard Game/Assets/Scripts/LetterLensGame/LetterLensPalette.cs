using System;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// One kind of thing a letter can be made of, and the range of real-world
    /// heights it may be scaled to. The range is what keeps placement
    /// natural: a dot that needs a 9 m tall object gets a tree, never a 9 m
    /// mushroom.
    /// </summary>
    [Serializable]
    public class LensProp
    {
        public GameObject prefab;
        [Tooltip("Smallest believable height in meters, base to top.")]
        [Min(0.02f)] public float minHeight = 0.2f;
        [Tooltip("Largest believable height in meters, base to top.")]
        [Min(0.05f)] public float maxHeight = 2f;
    }

    /// <summary>
    /// What hidden letters are built from. Every prop STANDS on the ground —
    /// nothing floats — and its top (a mushroom cap, a rock's crown, a tree's
    /// canopy) is what forms the dot. Low dots become rocks, mushrooms and
    /// plants; dots higher above the ground become stumps, bushes and trees.
    ///
    /// Any prefab works: the factory measures it, scales it within its
    /// height range, strips colliders from small props and moves built-in
    /// "Standard" materials onto URP Lit. The game loads the copy at
    /// Resources/LetterLensPalette. Missing entries are skipped.
    /// </summary>
    [CreateAssetMenu(menuName = "Otherwise Labs/Letter Lens Palette", fileName = "LetterLensPalette")]
    public class LetterLensPalette : ScriptableObject
    {
        public const string ResourcePath = "LetterLensPalette";

        public List<LensProp> props = new List<LensProp>();

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

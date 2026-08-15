using System;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Where a creature likes to live, expressed against the water line rather
    /// than against any terrain system's internals — the mini-game only ever
    /// asks "how high is the ground here, and where is the water surface?", so
    /// it works on any walkable scene without referencing terrain code.
    /// </summary>
    public enum CreatureHabitat
    {
        /// <summary>Roams below the waterline, sometimes wanders up onto the beach.</summary>
        Water = 0,
        /// <summary>Sticks to the band just above the waterline.</summary>
        Shore = 1,
        /// <summary>Roams anywhere on dry land above the shore band.</summary>
        Meadow = 2,
    }

    /// <summary>
    /// One letter-creature type: Creature W, Creature S, and so on. The letter
    /// IS the species — it decides the word challenge used to bait a trap, the
    /// name the player calls to lure it, and the shape traced to capture it.
    /// </summary>
    [Serializable]
    public class CreatureDefinition
    {
        [Tooltip("The creature's letter — one character, e.g. W. Uppercased automatically.")]
        public string letter = "A";

        [Tooltip("Name shown in UI and the booklet, e.g. \"Creature W\". Empty = built from the letter.")]
        public string displayName = "";

        [Tooltip("A friendly line for the booklet. Narrative properties can grow here later.")]
        [TextArea] public string blurb = "";

        [Tooltip("Model to spawn (e.g. an RPG Monster DUO prefab). A collider and simple wander brain are added at runtime.")]
        public GameObject prefab;

        [Tooltip("Where this creature roams, relative to the water line.")]
        public CreatureHabitat habitat = CreatureHabitat.Meadow;

        [Tooltip("How many of this creature roam the world at once.")]
        [Range(1, 12)] public int maxAlive = 4;

        [Tooltip("Wander speed in m/s. Kept gentle so young players can follow one on foot.")]
        [Range(0.2f, 5f)] public float walkSpeed = 1.4f;

        [Tooltip("How far THIS creature hears its letter called, in meters. 0 = use the controller's global Call Radius.")]
        [Min(0f)] public float callResponseRadius = 0f;

        [Header("Temperament")]
        [Tooltip("How skittish this creature is around the player, scaling the controller's Player Shy Radius. 0 = fearless, 1 = normal, 2 = extra jumpy.")]
        [Range(0f, 2f)] public float shyness = 1f;

        [Tooltip("Familiarity: fraction of shyness each capture of this letter removes from the whole species. At 0.15, seven captures make them fearless. 0 = wild forever. A future training system drives the same number further.")]
        [Range(0f, 1f)] public float tamingPerCapture = 0.15f;

        [Tooltip("Uniform scale applied to the spawned model.")]
        [Range(0.2f, 4f)] public float modelScale = 1f;

        /// <summary>Uppercase letter this creature answers to.</summary>
        public char Letter => string.IsNullOrEmpty(letter) ? 'A' : char.ToUpperInvariant(letter[0]);

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? $"Creature {Letter}" : displayName;
    }
}

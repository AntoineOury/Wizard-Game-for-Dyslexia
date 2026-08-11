using System;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// One region type of the streaming world: its own assets, height shaping and
    /// ground colors. Which biome owns a world position is decided by a second,
    /// much lower-frequency "climate" noise field, so it stays a pure function of
    /// (seed, position) and the infinite world remains deterministic.
    ///
    /// Biomes are laid out along the 0-1 climate axis in list order, each taking a
    /// share of it proportional to its Coverage. That means a biome only ever
    /// borders its neighbours in the list — order the list like a climate
    /// gradient (e.g. Winter, Forest, Desert) and transitions will make sense.
    /// </summary>
    [Serializable]
    public class BiomeDefinition
    {
        [Tooltip("Name shown in the Inspector and the asset drop target menu.")]
        public string name = "New Biome";

        [Tooltip("Relative share of the world this biome covers. Shares are normalized across all biomes, so 2 covers roughly twice the area of 1.")]
        [Min(0.01f)] public float coverage = 1f;

        [Header("Height")]
        [Tooltip("Peak height of this biome in world units. Winter at 50 beside forest at 30 gives the winter region visibly higher mountains, blended smoothly at the border.")]
        [Min(0f)] public float heightMultiplier = 30f;

        [Tooltip("Remaps the shared base noise (X: 0-1) inside this biome. Flatten the low end for plains, steepen the top for jagged peaks.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Raises or lowers the whole biome, in world units. Useful for a highland plateau or a sunken marsh.")]
        public float heightOffset = 0f;

        [Header("Look")]
        [Tooltip("Ground colors by height for this biome. Winter wants whites and pale blues, forest wants greens. Blended with neighbours at borders.")]
        public Gradient colorByHeight = BiomeField.DefaultBiomeGradient();

        [Header("Environment Assets")]
        [Tooltip("Assets that appear ONLY in this biome. The streamer's global Environment Assets list appears in every biome.")]
        public List<EnvironmentAssetRule> environmentAssets = new List<EnvironmentAssetRule>();

        [Header("Ambience")]
        [Tooltip("Looping soundscape while this biome is dominant (wind, birds, snow). Crossfaded by the BiomeAmbience component.")]
        public AudioClip ambientLoop;

        [Range(0f, 1f)] public float ambientVolume = 0.6f;

        [Tooltip("Tint the scene fog while inside this biome. Fog must be enabled in Lighting > Environment for this to show.")]
        public bool overrideFogColor = false;

        public Color fogColor = new Color(0.70f, 0.75f, 0.80f);
    }

    /// <summary>
    /// Anything that can say which biome owns a world position. Implemented by
    /// both terrain systems so shared atmosphere components (BiomeAmbience) can
    /// serve either without referencing one specifically — the streamer and the
    /// finite generator must never depend on each other.
    /// </summary>
    public interface IBiomeSource
    {
        /// <summary>Dominant biome at a world position, or null when no biomes are defined.</summary>
        BiomeDefinition DominantBiomeAt(Vector3 worldPosition);
    }

    /// <summary>
    /// Turns a climate value into per-biome blend weights.
    /// </summary>
    public static class BiomeField
    {
        /// <summary>
        /// Fills <paramref name="weights"/> with each biome's influence at the
        /// given climate value. Weights always sum to 1.
        ///
        /// Each biome owns a band of the climate axis sized by its coverage.
        /// Inside the band, weight is 1; across a blend window straddling each
        /// border it crossfades with the neighbour. Heights and colors blended by
        /// these weights therefore cross biome borders smoothly, because the
        /// weights themselves do.
        /// </summary>
        public static void GetWeights(IList<BiomeDefinition> biomes, float climate, float blend, float[] weights)
        {
            int count = biomes.Count;
            if (count == 0) return;
            if (count == 1) { weights[0] = 1f; return; }

            float totalCoverage = 0f;
            for (int i = 0; i < count; i++)
                totalCoverage += Mathf.Max(0.01f, biomes[i] != null ? biomes[i].coverage : 1f);

            // Half-width of the crossfade window straddling each band border.
            float half = Mathf.Max(0.0005f, blend * 0.5f);
            climate = Mathf.Clamp01(climate);

            float cursor = 0f;
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float band = Mathf.Max(0.01f, biomes[i] != null ? biomes[i].coverage : 1f) / totalCoverage;
                float lower = cursor;
                float upper = cursor + band;
                cursor = upper;

                // Trapezoid: full weight inside the band, linear ramps across the
                // blend window at each border. The first and last band extend
                // outward so the climate extremes always have a full-weight owner.
                float fromLower = i == 0 ? 1f : Mathf.Clamp01((climate - (lower - half)) / (2f * half));
                float fromUpper = i == count - 1 ? 1f : Mathf.Clamp01(((upper + half) - climate) / (2f * half));
                float w = Mathf.Min(fromLower, fromUpper);

                // Smoothstep so the crossfade eases in and out instead of creasing
                // at the window edges; normalization below restores sum = 1.
                w = w * w * (3f - 2f * w);

                weights[i] = w;
                sum += w;
            }

            if (sum <= 0f)
            {
                // Unreachable for climate in 0..1, but never divide by zero.
                weights[0] = 1f;
                for (int i = 1; i < count; i++) weights[i] = 0f;
                return;
            }

            float inverse = 1f / sum;
            for (int i = 0; i < count; i++) weights[i] *= inverse;
        }

        /// <summary>
        /// A list element freshly added in the Inspector arrives zeroed rather
        /// than with field-initializer defaults, which would mean a flat black
        /// biome. Treat that state as "give me sensible defaults". Shared by both
        /// terrain systems so their biome lists behave identically.
        /// </summary>
        public static void Sanitize(List<BiomeDefinition> biomes)
        {
            if (biomes == null) return;
            foreach (BiomeDefinition biome in biomes)
            {
                if (biome == null) continue;
                if (string.IsNullOrWhiteSpace(biome.name)) biome.name = "Biome";
                biome.coverage = Mathf.Max(0.01f, biome.coverage);
                if (biome.heightCurve == null || biome.heightCurve.length == 0)
                    biome.heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                if (biome.colorByHeight == null || biome.colorByHeight.colorKeys == null || biome.colorByHeight.colorKeys.Length == 0)
                    biome.colorByHeight = DefaultBiomeGradient();
                if (biome.heightMultiplier <= 0f && biome.heightOffset == 0f)
                    biome.heightMultiplier = 30f;
                biome.environmentAssets ??= new List<EnvironmentAssetRule>();
            }
        }

        /// <summary>Neutral green-ish default so a fresh biome isn't black.</summary>
        public static Gradient DefaultBiomeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.24f, 0.50f, 0.62f), 0.00f),
                    new GradientColorKey(new Color(0.80f, 0.72f, 0.46f), 0.35f),
                    new GradientColorKey(new Color(0.30f, 0.52f, 0.26f), 0.50f),
                    new GradientColorKey(new Color(0.42f, 0.38f, 0.33f), 0.75f),
                    new GradientColorKey(new Color(0.93f, 0.94f, 0.96f), 0.92f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}

using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// World-space terrain sampling shared by the finite generator and the
    /// infinite streamer.
    ///
    /// Everything here is a pure function of (world position, seed). That is the
    /// property the streaming world depends on: a chunk regenerated an hour later
    /// produces byte-identical terrain and asset placement, so a player can walk
    /// away and come back to the same landscape without anything being stored.
    /// </summary>
    public static class TerrainNoise
    {
        /// <summary>
        /// Mathf.PerlinNoise mirrors around zero, so sampling negative world
        /// coordinates would make the world visibly symmetric about the origin.
        /// Shifting every sample into positive space avoids that. The value is
        /// large enough for any sane play area and small enough to keep float
        /// precision comfortable.
        /// </summary>
        public const float NoiseOrigin = 100000f;

        /// <summary>
        /// Per-octave sample offsets derived from the seed. Same seed, same
        /// offsets, forever. <paramref name="worldOrigin"/> shifts sampling into
        /// positive space for infinite worlds; pass 0 to reproduce the finite
        /// generator's original output exactly.
        /// </summary>
        public static Vector2[] BuildOctaveOffsets(int seed, int octaves, Vector2 noiseOffset, float worldOrigin = 0f)
        {
            int count = Mathf.Clamp(octaves, 1, 8);
            var rng = new System.Random(seed);
            var offsets = new Vector2[count];
            for (int o = 0; o < count; o++)
            {
                offsets[o] = new Vector2(
                    rng.Next(0, 10000) + noiseOffset.x + worldOrigin,
                    rng.Next(0, 10000) + noiseOffset.y + worldOrigin);
            }
            return offsets;
        }

        /// <summary>
        /// Multi-octave Perlin value in 0-1 at a world position. Identical maths
        /// to the finite generator, just fed absolute coordinates.
        /// </summary>
        public static float SampleNormalized(float worldX, float worldZ, Vector2[] octaveOffsets,
                                             float noiseScale, float persistence, float lacunarity)
        {
            float scale = Mathf.Max(0.01f, noiseScale);
            float pers = Mathf.Clamp01(persistence);
            float lac = Mathf.Max(1f, lacunarity);

            float amplitude = 1f;
            float frequency = 1f;
            float value = 0f;
            float amplitudeSum = 0f;

            for (int o = 0; o < octaveOffsets.Length; o++)
            {
                float sampleX = (worldX + octaveOffsets[o].x) / scale * frequency;
                float sampleZ = (worldZ + octaveOffsets[o].y) / scale * frequency;
                value += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= pers;
                frequency *= lac;
            }

            return amplitudeSum > 0f ? value / amplitudeSum : 0f;
        }

        /// <summary>
        /// Normalized value shaped into a world-space height. A null or empty
        /// curve falls back to linear — a keyless curve evaluates to 0, which
        /// would silently flatten the terrain (a fresh Inspector-added biome
        /// arrives with exactly such a curve).
        /// </summary>
        public static float ToWorldHeight(float normalized, AnimationCurve heightCurve, float heightMultiplier)
        {
            float shaped = heightCurve != null && heightCurve.length > 0 ? heightCurve.Evaluate(normalized) : normalized;
            return shaped * heightMultiplier;
        }

        /// <summary>
        /// Surface normal from the height field by central differences. Because it
        /// reads the continuous noise rather than mesh topology, normals match
        /// across chunk borders and there is no lighting seam.
        /// </summary>
        public static Vector3 SampleNormal(float worldX, float worldZ, float step, Vector2[] octaveOffsets,
                                           float noiseScale, float persistence, float lacunarity,
                                           AnimationCurve heightCurve, float heightMultiplier)
        {
            float hL = ToWorldHeight(SampleNormalized(worldX - step, worldZ, octaveOffsets, noiseScale, persistence, lacunarity), heightCurve, heightMultiplier);
            float hR = ToWorldHeight(SampleNormalized(worldX + step, worldZ, octaveOffsets, noiseScale, persistence, lacunarity), heightCurve, heightMultiplier);
            float hD = ToWorldHeight(SampleNormalized(worldX, worldZ - step, octaveOffsets, noiseScale, persistence, lacunarity), heightCurve, heightMultiplier);
            float hU = ToWorldHeight(SampleNormalized(worldX, worldZ + step, octaveOffsets, noiseScale, persistence, lacunarity), heightCurve, heightMultiplier);

            float dx = (hR - hL) / (2f * step);
            float dz = (hU - hD) / (2f * step);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        // ------------------------------------------------------------------
        // Deterministic hashing
        // ------------------------------------------------------------------

        /// <summary>
        /// FNV-1a mix with an avalanche step. Used instead of GetHashCode because
        /// this value seeds world generation: it has to be stable across runs,
        /// builds and platforms, which string/object hashes are not.
        /// </summary>
        public static int Hash(int a, int b, int c = 0, int d = 0)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)d) * 16777619u;
                h ^= h >> 13;
                h *= 2246822519u;
                h ^= h >> 16;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        /// <summary>Deterministic 0-1 value from a hash, for jitter and dice rolls.</summary>
        public static float HashToUnit(int hash)
        {
            return (hash & 0xFFFFFF) / (float)0x1000000;
        }
    }
}

using System.IO;
using System.Text;
using OtherwiseLabs.TerrainTools;
using UnityEditor;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Regression guard for the world's determinism — the property everything
    /// rests on: walk-back persistence, cross-chunk prop collision and the
    /// WorldEdits delta store all assume the same seed always produces the same
    /// world.
    ///
    /// Record a baseline once, then Verify after any terrain code change. A
    /// refactor that silently alters generation fails loudly here instead of
    /// quietly rearranging every player's world.
    ///
    /// The baseline also stores a hash of the generation settings, so Verify can
    /// tell "you changed the Inspector settings — re-record" apart from "the
    /// CODE broke determinism".
    /// </summary>
    public static class TerrainDeterminismTest
    {
        const string BaselinePath = "ProjectSettings/OtherwiseLabsTerrainBaseline.json";

        static readonly Vector2Int[] TestCoords =
        {
            new Vector2Int(0, 0),
            new Vector2Int(3, -2),
            new Vector2Int(-5, 7),
            new Vector2Int(11, 4),
            new Vector2Int(-8, -8),
        };

        [System.Serializable]
        class Baseline
        {
            public long settingsHash;
            public long[] signatures;
        }

        [MenuItem("Tools/Otherwise Labs/Terrain/Record Determinism Baseline")]
        static void Record()
        {
            InfiniteTerrainStreamer streamer = FindStreamer();
            if (streamer == null) return;

            var baseline = new Baseline
            {
                settingsHash = SettingsHash(streamer),
                signatures = new long[TestCoords.Length],
            };
            for (int i = 0; i < TestCoords.Length; i++)
                baseline.signatures[i] = streamer.ComputeChunkSignature(TestCoords[i]);

            File.WriteAllText(BaselinePath, JsonUtility.ToJson(baseline, true));
            Debug.Log($"[Determinism] Baseline recorded for {TestCoords.Length} chunks from '{streamer.name}'. " +
                      "Run Verify after terrain code changes.");
        }

        [MenuItem("Tools/Otherwise Labs/Terrain/Verify Determinism")]
        static void Verify()
        {
            InfiniteTerrainStreamer streamer = FindStreamer();
            if (streamer == null) return;

            if (!File.Exists(BaselinePath))
            {
                Debug.LogWarning("[Determinism] No baseline recorded yet — run Record Determinism Baseline first.");
                return;
            }

            Baseline baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(BaselinePath));
            if (baseline?.signatures == null || baseline.signatures.Length != TestCoords.Length)
            {
                Debug.LogWarning("[Determinism] Baseline file is unreadable or from an older layout — re-record it.");
                return;
            }

            if (baseline.settingsHash != SettingsHash(streamer))
            {
                Debug.LogWarning("[Determinism] Generation settings differ from when the baseline was recorded " +
                                 "(seed, noise, biomes, warp...). That legitimately changes the world — " +
                                 "re-record the baseline once you are happy with the new settings.");
                return;
            }

            int failures = 0;
            for (int i = 0; i < TestCoords.Length; i++)
            {
                long signature = streamer.ComputeChunkSignature(TestCoords[i]);
                if (signature != baseline.signatures[i])
                {
                    failures++;
                    Debug.LogError($"[Determinism] FAIL chunk {TestCoords[i]}: signature {signature} != baseline {baseline.signatures[i]}. " +
                                   "Terrain code now generates a different world for the same settings — walk-back persistence is broken.");
                }
            }

            if (failures == 0)
                Debug.Log($"[Determinism] PASS — all {TestCoords.Length} chunk signatures match the baseline.");
        }

        static InfiniteTerrainStreamer FindStreamer()
        {
            var streamer = Object.FindObjectOfType<InfiniteTerrainStreamer>(true);
            if (streamer == null)
                Debug.LogWarning("[Determinism] No InfiniteTerrainStreamer in the open scene. Open Base Scene first.");
            return streamer;
        }

        /// <summary>
        /// Folds every setting that participates in generation into one hash.
        /// Quantized where float precision could differ in text round-trips.
        /// </summary>
        static long SettingsHash(InfiniteTerrainStreamer s)
        {
            var text = new StringBuilder();
            text.Append(s.seed).Append('|').Append(s.scatterSeed).Append('|').Append(s.biomeSeed).Append('|');
            text.Append(Mathf.RoundToInt(s.noiseScale * 100f)).Append('|').Append(s.octaves).Append('|');
            text.Append(Mathf.RoundToInt(s.persistence * 1000f)).Append('|').Append(Mathf.RoundToInt(s.lacunarity * 1000f)).Append('|');
            text.Append(Mathf.RoundToInt(s.noiseOffset.x * 100f)).Append(',').Append(Mathf.RoundToInt(s.noiseOffset.y * 100f)).Append('|');
            text.Append(Mathf.RoundToInt(s.heightMultiplier * 100f)).Append('|');
            text.Append(Mathf.RoundToInt(s.chunkSize * 100f)).Append('|').Append(s.chunkResolution).Append('|');
            text.Append(Mathf.RoundToInt(s.warpStrength * 100f)).Append('|').Append(Mathf.RoundToInt(s.warpScale * 100f)).Append('|');
            text.Append(Mathf.RoundToInt(s.biomeScale * 100f)).Append('|').Append(Mathf.RoundToInt(s.biomeBlend * 1000f)).Append('|');
            text.Append(s.biomes != null ? s.biomes.Count : 0).Append('|');
            if (s.biomes != null)
            {
                foreach (BiomeDefinition biome in s.biomes)
                {
                    if (biome == null) continue;
                    text.Append(biome.name).Append(',')
                        .Append(Mathf.RoundToInt(biome.coverage * 100f)).Append(',')
                        .Append(Mathf.RoundToInt(biome.heightMultiplier * 100f)).Append(',')
                        .Append(biome.environmentAssets != null ? biome.environmentAssets.Count : 0).Append(';');
                }
            }
            text.Append(s.environmentAssets != null ? s.environmentAssets.Count : 0);

            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (char c in text.ToString())
                    hash = (hash ^ c) * 1099511628211L;
                return hash;
            }
        }
    }
}

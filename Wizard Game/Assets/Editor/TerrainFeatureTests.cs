using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Editor test suite for the finite terrain's feature set: legacy height
    /// compatibility, biomes, water, paths/roads, scatter determinism and the
    /// shared-rule helpers. Companion to TerrainDeterminismTest, which guards
    /// the streamer; this one guards ProceduralTerrainGenerator and the shared
    /// pieces both systems build on.
    ///
    /// Run via Tools > Otherwise Labs > Terrain > Run Feature Tests. Every test
    /// builds its own throwaway objects and cleans up after itself, so it works
    /// in any open scene without touching it. Failures log as errors with the
    /// failing expectation named; a summary line closes the run.
    ///
    /// Same-assembly note: project code lives in the predefined assemblies, so
    /// these are menu-driven checks (the established pattern here) rather than
    /// Unity Test Framework tests, which would require an asmdef restructure.
    /// </summary>
    public static class TerrainFeatureTests
    {
        static int _checks;
        static int _failures;

        [MenuItem("Tools/Otherwise Labs/Terrain/Run Feature Tests")]
        public static void RunAll()
        {
            _checks = 0;
            _failures = 0;
            var cleanup = new List<Object>();

            try
            {
                LegacyHeightIdentity(cleanup);
                SingleBiomeHeightIdentity(cleanup);
                BiomeWeights();
                ScatterDeterminismAndStreamStability(cleanup);
                WaterSheet(cleanup);
                PathGeometry(cleanup);
                PathCarvingPaintAndClearance(cleanup);
                BiomeSourceContract(cleanup);
                ScatterRulePresets();
                WaterMaterialResolver(cleanup);
            }
            finally
            {
                foreach (Object o in cleanup)
                    if (o != null) Object.DestroyImmediate(o);
            }

            if (_failures == 0)
                Debug.Log($"[FeatureTests] PASS — {_checks} checks, all green.");
            else
                Debug.LogError($"[FeatureTests] {_failures} of {_checks} checks FAILED — see errors above.");
        }

        // ------------------------------------------------------------------
        // Groups
        // ------------------------------------------------------------------

        /// <summary>
        /// The backbone guarantee: with biomes, warp, water and paths all at
        /// defaults, the generator must reproduce the ORIGINAL inline noise
        /// formula exactly — otherwise every scene authored before the feature
        /// port silently changes shape. The old formula is replicated here,
        /// byte for byte, and compared against the generated mesh.
        /// </summary>
        static void LegacyHeightIdentity(List<Object> cleanup)
        {
            Group("Legacy height identity (old inline formula vs generated mesh)");
            ProceduralTerrainGenerator generator = NewGenerator(cleanup, resolution: 64);
            generator.GenerateTerrain();

            Vector3[] vertices = generator.GetComponent<MeshFilter>().sharedMesh.vertices;

            // --- the pre-port formula, replicated exactly ---
            int res = 64;
            int oct = Mathf.Clamp(generator.octaves, 1, 8);
            float pers = Mathf.Clamp01(generator.persistence);
            float lac = Mathf.Max(1f, generator.lacunarity);
            float scale = Mathf.Max(0.01f, generator.noiseScale);
            var size = new Vector2(Mathf.Max(1f, generator.terrainSize.x), Mathf.Max(1f, generator.terrainSize.y));

            var rng = new System.Random(generator.seed);
            var octaveOffsets = new Vector2[oct];
            for (int o = 0; o < oct; o++)
                octaveOffsets[o] = new Vector2(rng.Next(0, 10000) + generator.noiseOffset.x, rng.Next(0, 10000) + generator.noiseOffset.y);

            float maxDiff = 0f;
            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    float worldX = x / (float)res * size.x;
                    float worldZ = z / (float)res * size.y;

                    float amplitude = 1f, frequency = 1f, value = 0f, amplitudeSum = 0f;
                    for (int o = 0; o < oct; o++)
                    {
                        float sampleX = (worldX + octaveOffsets[o].x) / scale * frequency;
                        float sampleZ = (worldZ + octaveOffsets[o].y) / scale * frequency;
                        value += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
                        amplitudeSum += amplitude;
                        amplitude *= pers;
                        frequency *= lac;
                    }
                    value /= amplitudeSum;

                    float expected = generator.heightCurve.Evaluate(value) * generator.heightMultiplier;
                    float actual = vertices[z * (res + 1) + x].y;
                    maxDiff = Mathf.Max(maxDiff, Mathf.Abs(actual - expected));
                }
            }

            Check(maxDiff <= 1e-5f, $"default-settings mesh matches the pre-port formula (max diff {maxDiff})");
        }

        /// <summary>
        /// One biome configured identically to the global settings must produce
        /// the exact same heights as no biomes at all — the biome path is a
        /// weighted blend, and a single biome's weight must be exactly 1.
        /// </summary>
        static void SingleBiomeHeightIdentity(List<Object> cleanup)
        {
            Group("Single neutral biome produces identical heights");
            ProceduralTerrainGenerator plain = NewGenerator(cleanup, resolution: 64);
            plain.GenerateTerrain();
            Vector3[] withoutBiome = plain.GetComponent<MeshFilter>().sharedMesh.vertices;

            ProceduralTerrainGenerator biomed = NewGenerator(cleanup, resolution: 64);
            biomed.biomes.Add(NeutralBiome("Only", biomed));
            biomed.GenerateTerrain();
            Vector3[] withBiome = biomed.GetComponent<MeshFilter>().sharedMesh.vertices;

            float maxDiff = 0f;
            for (int i = 0; i < withoutBiome.Length; i++)
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(withoutBiome[i].y - withBiome[i].y));

            Check(withoutBiome.Length == withBiome.Length, "vertex counts match");
            Check(maxDiff <= 1e-5f, $"heights identical with a neutral biome (max diff {maxDiff})");
        }

        static void BiomeWeights()
        {
            Group("Biome weight blending");

            var biomes = new List<BiomeDefinition>
            {
                NeutralBiome("A", null), NeutralBiome("B", null), NeutralBiome("C", null),
            };
            var weights = new float[3];
            var reachedFullWeight = new bool[3];
            bool sumsToOne = true, nonNegative = true;

            for (float climate = 0f; climate <= 1.0001f; climate += 0.005f)
            {
                BiomeField.GetWeights(biomes, climate, 0.12f, weights);
                float sum = 0f;
                for (int i = 0; i < 3; i++)
                {
                    sum += weights[i];
                    if (weights[i] < 0f) nonNegative = false;
                    if (weights[i] > 0.99f) reachedFullWeight[i] = true;
                }
                if (Mathf.Abs(sum - 1f) > 1e-3f) sumsToOne = false;
            }

            Check(sumsToOne, "weights sum to 1 across the whole climate axis");
            Check(nonNegative, "weights never go negative");
            Check(reachedFullWeight[0] && reachedFullWeight[1] && reachedFullWeight[2],
                "every equal-coverage biome owns some ground outright");

            var single = new List<BiomeDefinition> { NeutralBiome("Solo", null) };
            var soloWeight = new float[1];
            BiomeField.GetWeights(single, 0.37f, 0.12f, soloWeight);
            Check(soloWeight[0] == 1f, "a single biome always has weight exactly 1");

            // Inspector-added entries arrive zeroed; the shared sanitizer must
            // turn that into a usable biome instead of a flat black one.
            var zeroed = new List<BiomeDefinition> { new BiomeDefinition { name = "", coverage = 0f, heightCurve = new AnimationCurve(), heightMultiplier = 0f } };
            BiomeField.Sanitize(zeroed);
            Check(zeroed[0].coverage >= 0.01f && zeroed[0].heightCurve.length > 0 && zeroed[0].heightMultiplier > 0f,
                "Sanitize repairs a zeroed Inspector entry");
        }

        /// <summary>
        /// Scatter must be a pure function of its seed: rerunning reproduces the
        /// layout exactly. And appending a biome must NOT move globally-scattered
        /// assets — global rules keep their table indices, so their random
        /// streams are untouched (the compatibility rule for existing scenes).
        /// </summary>
        static void ScatterDeterminismAndStreamStability(List<Object> cleanup)
        {
            Group("Scatter determinism + rule stream stability");
            ProceduralTerrainGenerator generator = NewGenerator(cleanup, resolution: 48);
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "FeatureTest Template";
            template.transform.position = new Vector3(0f, -500f, 0f);
            cleanup.Add(template);

            generator.environmentAssets.Add(new EnvironmentAssetRule
            {
                prefab = template,
                displayName = "Cubes",
                density = 1f,
                maxInstances = 40,
                minSpacing = 0.5f,
                maxSlopeAngle = 89f,
                restrictToZones = false,
                useZoneWeights = false,
                footprintRadius = 0.4f,
                embedDepth = 0f,
            });

            generator.GenerateAll();
            List<Vector3> first = CollectInstancePositions(generator, "Cubes");
            generator.ScatterEnvironment();
            List<Vector3> second = CollectInstancePositions(generator, "Cubes");

            Check(first.Count > 0, $"scatter placed instances ({first.Count})");
            Check(PositionsIdentical(first, second), "rerunning scatter reproduces the exact layout");

            // Neutral biome with its own (smaller-footprint) rule: appended to the
            // table AFTER the global rule, so the global stream must not shift.
            BiomeDefinition biome = NeutralBiome("B", generator);
            biome.environmentAssets.Add(new EnvironmentAssetRule
            {
                prefab = template,
                displayName = "BiomeCubes",
                density = 1f,
                maxInstances = 20,
                minSpacing = 0.5f,
                maxSlopeAngle = 89f,
                restrictToZones = false,
                useZoneWeights = false,
                footprintRadius = 0.2f,
                embedDepth = 0f,
            });
            generator.biomes.Add(biome);

            generator.ScatterEnvironment();
            List<Vector3> globalAfterBiome = CollectInstancePositions(generator, "Cubes");
            List<Vector3> biomeInstances = CollectInstancePositions(generator, "BiomeCubes");

            Check(PositionsIdentical(first, globalAfterBiome), "global rule layout unchanged after adding a biome");
            Check(biomeInstances.Count > 0, $"biome-owned rule scattered under its own group ({biomeInstances.Count})");
        }

        static void WaterSheet(List<Object> cleanup)
        {
            Group("Water sheet build / skip");
            ProceduralTerrainGenerator generator = NewGenerator(cleanup, resolution: 48);
            // Terrain floor pinned at 0.5 * multiplier = 12.5 so the dry case has
            // a guaranteed gap to clear.
            generator.heightCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1f));
            generator.waterEnabled = true;

            // DRY: waterline mapped to 10.5, well below the 12.5 floor — the
            // whole sheet must be skipped.
            generator.zoneBands.waterLevel = 0f;
            generator.waterSurfaceOffset = -2f;
            generator.GenerateTerrain();
            Transform water = generator.transform.Find(ProceduralTerrainGenerator.WaterRootName);
            Check(water == null || !water.gameObject.activeSelf, "no water sheet when terrain never dips near the waterline");

            // WET: waterline mapped to exactly 25 — sheet builds, level and flat.
            generator.zoneBands.waterLevel = 1f;
            generator.waterSurfaceOffset = 0f;
            generator.GenerateTerrain();
            water = generator.transform.Find(ProceduralTerrainGenerator.WaterRootName);
            bool active = water != null && water.gameObject.activeSelf;
            Check(active, "water sheet exists when terrain sits below the waterline");

            if (active)
            {
                Mesh mesh = water.GetComponent<MeshFilter>().sharedMesh;
                float worstLevel = 0f;
                foreach (Vector3 vertex in mesh.vertices)
                    worstLevel = Mathf.Max(worstLevel, Mathf.Abs(vertex.y - 25f));
                Check(mesh.vertexCount > 0 && worstLevel <= 1e-3f,
                    $"uniform world puts every water vertex at the mapped level (worst off by {worstLevel})");
            }

            generator.waterEnabled = false;
            generator.GenerateTerrain();
            water = generator.transform.Find(ProceduralTerrainGenerator.WaterRootName);
            Check(water == null || !water.gameObject.activeSelf, "disabling water hides the sheet on the next generate");
        }

        static void PathGeometry(List<Object> cleanup)
        {
            Group("TerrainPath geometry (standalone)");
            var space = new GameObject("FeatureTest PathSpace");
            cleanup.Add(space);

            TerrainPath path = MakePath(space.transform, new[]
            {
                new Vector3(-30f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 0f),
            });
            path.width = 4f;           // half width 2
            path.shoulderWidth = 2f;   // influence ends at 4
            path.scatterClearance = 1.5f;
            path.smoothingPasses = 3;

            path.Bake(space.transform, (x, z) => 0f);
            Check(path.IsBaked, "3 waypoints bake into a route");

            Check(path.SampleInfluence(new Vector2(0f, 0f), out float flatten, out _, out float height)
                  && flatten == 1f && Mathf.Abs(height) <= 1e-4f,
                "centerline: full flatten at the route, at terrain height");
            Check(path.SampleInfluence(new Vector2(0f, 1.9f), out flatten, out _, out _) && flatten == 1f,
                "everywhere inside the half width is fully flattened");
            Check(path.SampleInfluence(new Vector2(0f, 3f), out flatten, out _, out _) && flatten > 0f && flatten < 1f,
                "the shoulder blends between road bed and raw terrain");
            Check(!path.SampleInfluence(new Vector2(0f, 4.05f), out _, out _, out _),
                "no influence beyond half width + shoulder");

            float previous = 2f;
            bool monotonic = true;
            foreach (float distance in new[] { 2.2f, 2.8f, 3.4f, 3.9f })
            {
                path.SampleInfluence(new Vector2(0f, distance), out float f, out _, out _);
                if (f >= previous) monotonic = false;
                previous = f;
            }
            Check(monotonic, "flatten falls off monotonically across the shoulder");

            Check(path.BlocksScatter(new Vector2(0f, 0f), 0f), "scatter blocked on the roadway");
            Check(!path.BlocksScatter(new Vector2(0f, 3.8f), 0f), "scatter allowed beyond roadway + clearance");
            Check(path.BlocksScatter(new Vector2(0f, 3.8f), 0.5f), "a footprint widens the blocked band");

            // Open ends stay pinned to the terrain even with smoothing on, so
            // the road emerges from the ground instead of hovering.
            path.Bake(space.transform, (x, z) => x * 0.5f);
            path.SampleInfluence(new Vector2(-30f, 0f), out _, out _, out float endHeight);
            Check(Mathf.Abs(endHeight - (-15f)) <= 1e-4f, $"open route ends pinned to terrain height (got {endHeight})");

            // A spike under the route must be graded down by profile smoothing.
            path.Bake(space.transform, (x, z) => Mathf.Max(0f, 10f - Mathf.Abs(x)));
            path.SampleInfluence(new Vector2(0f, 0f), out _, out _, out float spikeHeight);
            Check(spikeHeight < 9.5f, $"smoothing grades a terrain spike down along the route (got {spikeHeight})");

            // Closed loop: the wrap segment between last and first waypoint is
            // part of the route. The spline bulges outside the waypoint square
            // (Catmull-Rom overshoot), so probe where the curve actually runs.
            TerrainPath loop = MakePath(space.transform, new[]
            {
                new Vector3(-20f, 0f, -20f), new Vector3(20f, 0f, -20f),
                new Vector3(20f, 0f, 20f), new Vector3(-20f, 0f, 20f),
            });
            loop.closedLoop = true;
            loop.Bake(space.transform, (x, z) => 0f);
            Check(loop.SampleInfluence(new Vector2(-25f, 0f), out _, out _, out _),
                "closed loop covers the wrap segment between last and first waypoint");

            TerrainPath tooShort = MakePath(space.transform, new[] { new Vector3(1f, 0f, 1f) });
            tooShort.Bake(space.transform, (x, z) => 0f);
            Check(!tooShort.IsBaked, "a single waypoint does not bake a route");
        }

        static void PathCarvingPaintAndClearance(List<Object> cleanup)
        {
            Group("Path carving, painting and scatter clearance (integrated)");
            ProceduralTerrainGenerator generator = NewGenerator(cleanup, resolution: 96);
            generator.GenerateTerrain();
            Mesh mesh = generator.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] rawVertices = mesh.vertices;
            Color[] rawColors = mesh.colors;

            TerrainPath path = MakePath(generator.transform, new[]
            {
                new Vector3(-80f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(80f, 0f, 0f),
            });
            path.width = 6f;
            path.shoulderWidth = 4f;
            path.surfaceColor = new Color(1f, 0f, 0f);

            generator.GenerateTerrain();
            mesh = generator.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] carvedVertices = mesh.vertices;
            Color[] carvedColors = mesh.colors;

            // Grid center = local (0,0), dead on the route.
            int res = 96;
            int center = (res / 2) * (res + 1) + res / 2;
            // Far row: local z = -100, a hundred units from the route.
            int far = res / 2;

            // Painting lerps toward pure red, so red never falls and the color
            // must visibly change — phrased this way it holds whatever ground
            // color the seed happens to put at the map center.
            Check(carvedColors[center] != rawColors[center] && carvedColors[center].r >= rawColors[center].r,
                "roadway vertices are painted toward the surface color");
            Check(carvedVertices[far].y == rawVertices[far].y && carvedColors[far] == rawColors[far],
                "vertices far from the route are untouched, height and color");

            // Disabling the path must restore the raw terrain exactly.
            path.enabled = false;
            generator.GenerateTerrain();
            Vector3[] restored = generator.GetComponent<MeshFilter>().sharedMesh.vertices;
            float maxDiff = 0f;
            for (int i = 0; i < restored.Length; i++)
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(restored[i].y - rawVertices[i].y));
            Check(maxDiff == 0f, $"a disabled path leaves the terrain bit-identical (max diff {maxDiff})");
            path.enabled = true;

            // Scatter must keep every instance off roadway + clearance.
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "FeatureTest ClearanceTemplate";
            template.transform.position = new Vector3(0f, -500f, 0f);
            cleanup.Add(template);
            generator.environmentAssets.Add(new EnvironmentAssetRule
            {
                prefab = template,
                displayName = "ClearanceCubes",
                density = 1f,
                maxInstances = 60,
                minSpacing = 0.3f,
                maxSlopeAngle = 89f,
                restrictToZones = false,
                useZoneWeights = false,
                footprintRadius = 0.3f,
                embedDepth = 0f,
            });
            generator.GenerateAll();

            List<Vector3> instances = CollectInstancePositions(generator, "ClearanceCubes");
            bool allClear = true;
            foreach (Vector3 position in instances)
                if (path.BlocksScatter(new Vector2(position.x, position.z), 0f)) allClear = false;

            Check(instances.Count > 0, $"clearance test scattered instances ({instances.Count})");
            Check(allClear, "no scattered instance stands on the roadway or inside its clearance");
        }

        static void BiomeSourceContract(List<Object> cleanup)
        {
            Group("IBiomeSource contract (shared ambience hook)");
            Check(typeof(IBiomeSource).IsAssignableFrom(typeof(InfiniteTerrainStreamer)),
                "the streamer implements IBiomeSource");
            Check(typeof(IBiomeSource).IsAssignableFrom(typeof(ProceduralTerrainGenerator)),
                "the finite generator implements IBiomeSource");

            ProceduralTerrainGenerator generator = NewGenerator(cleanup, resolution: 16);
            Check(generator.DominantBiomeAt(generator.transform.position) == null,
                "no biomes defined -> DominantBiomeAt reports null");

            generator.biomes.Add(NeutralBiome("Alpha", generator));
            generator.biomes.Add(NeutralBiome("Beta", generator));
            BiomeDefinition dominant = generator.DominantBiomeAt(generator.transform.position);
            Check(dominant != null && generator.biomes.Contains(dominant),
                "with biomes defined, DominantBiomeAt returns one of them");
        }

        static void ScatterRulePresets()
        {
            Group("Shared scatter rule helpers");
            Check(ScatterRules.EstimateFootprintRadius(null) == 0.5f, "null prefab falls back to a 0.5 footprint");

            Check(ScatterRules.GuessCategory("Cedar03") == ScatterRules.AssetCategory.Tree, "species name -> Tree");
            Check(ScatterRules.GuessCategory("Boulder_A") == ScatterRules.AssetCategory.Rock, "boulder -> Rock");
            Check(ScatterRules.GuessCategory("Tree_Log") == ScatterRules.AssetCategory.Debris, "log beats tree (debris checked first)");
            Check(ScatterRules.GuessCategory("WoodenHouse") == ScatterRules.AssetCategory.Building, "house -> Building");
            Check(ScatterRules.GuessCategory("Fern") == ScatterRules.AssetCategory.GroundCover, "fern -> GroundCover");
            Check(ScatterRules.GuessCategory("Zyzzyx") == ScatterRules.AssetCategory.Generic, "unknown name -> Generic");

            var rule = new EnvironmentAssetRule();
            ScatterRules.ApplyCategoryDefaults(rule, ScatterRules.AssetCategory.Building);
            Check(rule.restrictToZones && rule.allowedZones == TerrainZone.Grass && rule.maxSlopeAngle <= 10f,
                "building preset: flat Grass-only placement");

            ScatterRules.ApplyCategoryDefaults(rule, ScatterRules.AssetCategory.Tree);
            Check(rule.zoneWeights.water == 0f, "tree preset never spawns in water");
        }

        static void WaterMaterialResolver(List<Object> cleanup)
        {
            Group("Shared water material resolver");
            Material cache = null;
            Material auto = TerrainWaterMaterial.Resolve(null, ref cache);
            Check(auto != null && auto.shader != null && auto.shader.name == TerrainWaterMaterial.ShaderName,
                "auto material comes from the shared water shader");
            if (auto != null) cleanup.Add(auto);

            if (auto != null)
            {
                var assigned = new Material(auto.shader);
                cleanup.Add(assigned);
                Check(TerrainWaterMaterial.Resolve(assigned, ref cache) == assigned,
                    "an authored material always wins over the auto one");
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static void Group(string title) => Debug.Log($"[FeatureTests] — {title}");

        static void Check(bool condition, string expectation)
        {
            _checks++;
            if (condition) return;
            _failures++;
            Debug.LogError($"[FeatureTests] FAIL — {expectation}");
        }

        static ProceduralTerrainGenerator NewGenerator(List<Object> cleanup, int resolution)
        {
            var go = new GameObject("FeatureTest Terrain");
            cleanup.Add(go);
            var generator = go.AddComponent<ProceduralTerrainGenerator>();
            generator.resolution = resolution;
            generator.autoRebuild = false;
            generator.autoAssignMaterial = false; // material state is not under test
            return generator;
        }

        /// <summary>
        /// A biome whose height settings equal the generator's global ones, so
        /// adding it must not change the terrain shape — the perfect probe for
        /// blend-math and compatibility checks.
        /// </summary>
        static BiomeDefinition NeutralBiome(string name, ProceduralTerrainGenerator generator)
        {
            return new BiomeDefinition
            {
                name = name,
                coverage = 1f,
                heightMultiplier = generator != null ? generator.heightMultiplier : 25f,
                heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                heightOffset = 0f,
            };
        }

        static TerrainPath MakePath(Transform parent, Vector3[] waypoints)
        {
            var go = new GameObject("FeatureTest Path");
            go.transform.SetParent(parent, false);
            var path = go.AddComponent<TerrainPath>();
            for (int i = 0; i < waypoints.Length; i++)
            {
                var waypoint = new GameObject($"Waypoint {i}");
                waypoint.transform.SetParent(go.transform, false);
                waypoint.transform.localPosition = waypoints[i];
            }
            return path;
        }

        static List<Vector3> CollectInstancePositions(ProceduralTerrainGenerator generator, string containerName)
        {
            var positions = new List<Vector3>();
            Transform root = generator.transform.Find(ProceduralTerrainGenerator.EnvironmentRootName);
            Transform container = root != null ? root.Find(containerName) : null;
            if (container == null && root != null)
            {
                // Biome-owned rules nest one level deeper: root/<biome>/<rule>.
                for (int i = 0; i < root.childCount && container == null; i++)
                    container = root.GetChild(i).Find(containerName);
            }
            if (container == null) return positions;

            for (int i = 0; i < container.childCount; i++)
                positions.Add(container.GetChild(i).localPosition);
            return positions;
        }

        static bool PositionsIdentical(List<Vector3> a, List<Vector3> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                // Exact, not approximate: the same seed must reproduce the same
                // bits, and Vector3 == would hide a real drift behind its epsilon.
                if (a[i].x != b[i].x || a[i].y != b[i].y || a[i].z != b[i].z) return false;
            }
            return true;
        }
    }
}

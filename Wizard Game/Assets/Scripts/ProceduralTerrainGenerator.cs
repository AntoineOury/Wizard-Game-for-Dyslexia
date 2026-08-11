using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Procedural terrain + environment generator for Unity 2022.3.
    /// Builds a bounded terrain mesh from multi-octave Perlin noise and scatters
    /// prefabs (trees, rocks, buildings, ...) over it using per-asset placement
    /// rules. Use the custom inspector buttons, or call GenerateAll() at runtime.
    ///
    /// Feature parity with the Infinite Terrain Streamer, without depending on
    /// it (both build on the same shared pieces — TerrainNoise, BiomeDefinition,
    /// EnvironmentAssetRule, the water shader — and neither references the
    /// other):
    /// - Biomes: regions with their own height shaping, colors and assets, laid
    ///   out by a low-frequency climate noise and blended at borders.
    /// - Water: a translucent surface where terrain dips below the Water zone.
    /// - Domain warp: bends noise lookups to break up Perlin's round blobs.
    /// - Ambience + fog: implements IBiomeSource, so the shared BiomeAmbience
    ///   component crossfades soundscapes and tints fog per biome here too.
    /// - Paths/roads: add child objects with a TerrainPath component; the
    ///   generator flattens the ground along them, tints the roadway and keeps
    ///   scattered props off it. (This one is exclusive to the finite terrain.)
    ///
    /// With biomes, warp and water left at their defaults, a terrain generated
    /// by an older version of this tool rebuilds bit-identically from the same
    /// seeds — existing scenes keep their exact shape.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Otherwise Labs/Procedural Terrain Generator")]
    public class ProceduralTerrainGenerator : MonoBehaviour, IBiomeSource
    {
        public const string EnvironmentRootName = "-- Environment --";
        public const string WaterRootName = "-- Water --";
        public const string TerrainShaderName = "OtherwiseLabs/Terrain Vertex Color";
        const string GeneratedMeshName = "Procedural Terrain Mesh";
        const string GeneratedMaterialName = "Procedural Terrain Material";
        const string GeneratedWaterMeshName = "Procedural Terrain Water Mesh";

        [Header("Terrain Dimensions")]
        [Tooltip("Width (X) and length (Z) of the terrain in world units.")]
        public Vector2 terrainSize = new Vector2(200f, 200f);

        [Tooltip("Quads per side. Vertices per side = resolution + 1. Higher = more detail, more triangles.")]
        [Range(8, 512)] public int resolution = 128;

        [Header("Perlin Noise")]
        [Tooltip("Seed for the height noise. Same seed + settings = same terrain.")]
        public int seed = 12345;

        [Tooltip("Zoom of the base noise in world units. Bigger = wider, smoother features.")]
        [Min(0.01f)] public float noiseScale = 60f;

        [Tooltip("Number of noise layers. Each octave adds finer detail.")]
        [Range(1, 8)] public int octaves = 4;

        [Tooltip("How much each successive octave contributes (amplitude falloff).")]
        [Range(0f, 1f)] public float persistence = 0.5f;

        [Tooltip("How much the frequency grows each octave (detail scale-down).")]
        [Range(1f, 4f)] public float lacunarity = 2f;

        [Tooltip("Scrolls the noise field. Change to explore the same seed's neighborhood.")]
        public Vector2 noiseOffset;

        [Header("Height Shaping")]
        [Tooltip("World-space height of the highest terrain point. With biomes defined, each biome's own multiplier applies instead.")]
        [Min(0f)] public float heightMultiplier = 25f;

        [Tooltip("Remaps normalized noise (X: 0-1) to height (Y: 0-1). Flatten the low end for plains/lakes, steepen the top for peaks. With biomes defined, each biome's own curve applies instead.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Fades heights down toward the terrain edges to make an island. 0 = off. Combines with biomes and water: the coast dips below the waterline, so an island gets a real sea.")]
        [Range(0f, 1f)] public float islandFalloff = 0f;

        [Header("Domain Warp")]
        [Tooltip("Distorts sample positions with a second noise field before every height and biome lookup, breaking up Perlin's characteristic round blobs. 0 = OFF, and existing terrains keep their exact shape — any other value generates a DIFFERENT terrain for the same seed.")]
        [Range(0f, 80f)] public float warpStrength = 0f;

        [Tooltip("Feature size of the distortion field in world units.")]
        [Min(10f)] public float warpScale = 250f;

        [Header("Rendering")]
        [Tooltip("Vertex colors by normalized height (water > sand > grass > rock > snow). Rendered by the included 'OtherwiseLabs/Terrain Vertex Color' shader. With biomes defined, each biome's own gradient applies instead.")]
        public Gradient colorByHeight = CreateDefaultGradient();

        [Tooltip("If the MeshRenderer has no material, assign one automatically using the included vertex color shader.")]
        public bool autoAssignMaterial = true;

        [Header("Terrain Zones")]
        [Tooltip("Normalized height thresholds that name the terrain bands. Assets can then be restricted " +
                 "to zones (e.g. trees on Shore + Grass only). Keep these lined up with the gradient above.")]
        public TerrainZoneBands zoneBands = new TerrainZoneBands();

        [Header("Biomes")]
        [Tooltip("Region types with their own assets, heights and colors, laid out along a low-frequency " +
                 "climate noise. A biome only borders its list neighbours, so order the list like a climate " +
                 "gradient (e.g. Winter, Forest, Desert). Empty = one uniform terrain using the settings above.")]
        public List<BiomeDefinition> biomes = new List<BiomeDefinition>();

        [Tooltip("Size of biome regions in world units. Bigger = larger stretches of a single type. " +
                 "Around your terrain size gives a couple of regions per map; well below it gives a patchwork.")]
        [Min(50f)] public float biomeScale = 300f;

        [Tooltip("Width of the crossfade between neighbouring biomes, as a fraction of the climate range. " +
                 "Bigger = wider, softer borders; smaller = crisper region edges.")]
        [Range(0.01f, 0.4f)] public float biomeBlend = 0.12f;

        [Tooltip("Seed for the biome layout. Re-roll to rearrange which region lands where without changing the terrain detail inside them.")]
        public int biomeSeed = 13579;

        [Header("Water")]
        [Tooltip("Spawn a translucent surface where terrain dips below the Water zone threshold, making lakes real instead of blue-painted ground. Follows biome height shaping, so lakes in a high biome sit higher than lakes in a low one.")]
        public bool waterEnabled = false;

        [Tooltip("Material for the water surface. Empty = auto-created from the shared 'OtherwiseLabs/Terrain Water' shader.")]
        public Material waterMaterial;

        [Tooltip("Drops the surface slightly below the zone threshold so shorelines don't z-fight the sand.")]
        public float waterSurfaceOffset = -0.2f;

        [Tooltip("Quads per side of the water sheet. It only needs enough to follow biome height differences — far fewer than the terrain.")]
        [Range(2, 128)] public int waterResolution = 48;

        [Header("Environment Assets")]
        [Tooltip("Prefabs to scatter everywhere and their placement rules. Assets that belong to ONE biome go " +
                 "in that biome's own list instead. Use the drag & drop area below to add entries quickly.")]
        public List<EnvironmentAssetRule> environmentAssets = new List<EnvironmentAssetRule>();

        [Tooltip("Seed for asset placement. Same seed = same layout.")]
        public int scatterSeed = 54321;

        [Tooltip("Stop assets from different rules overlapping each other (rock inside a tree). " +
                 "Uses each rule's Footprint Radius. Turn off to compare against the old behaviour.")]
        public bool preventAssetOverlap = true;

        [Header("Editor Behaviour")]
        [Tooltip("Regenerate the terrain mesh automatically whenever a setting changes in the Inspector (Editor only). Scattering still requires a button press.")]
        public bool autoRebuild = true;

        // Height caches from the last generate, used by the scatterer so placement
        // always matches the visible mesh. Not serialized: rebuilt on demand.
        [NonSerialized] float[,] _normalizedHeights;
        [NonSerialized] float[,] _worldHeights;
        [NonSerialized] int _cachedResolution;

        // Sampling state shared by heights, water, scatter and DominantBiomeAt.
        [NonSerialized] Vector2[] _octaveOffsets;
        [NonSerialized] Vector2[] _biomeOctaveOffsets;
        [NonSerialized] Vector2[] _warpOffsetsX;
        [NonSerialized] Vector2[] _warpOffsetsY;
        [NonSerialized] float[] _biomeWeights;
        [NonSerialized] int _offsetsStamp;
        [NonSerialized] bool _offsetsBuilt;

        // Path influence from the last height build: carved into _worldHeights,
        // with the paint mask kept for the mesh's color pass.
        [NonSerialized] List<TerrainPath> _bakedPaths;
        [NonSerialized] float[,] _pathPaintMask;
        [NonSerialized] Color[,] _pathPaintColor;

        [NonSerialized] Material _autoWaterMaterial;

        public int LastVertexCount { get; private set; }
        public int LastTriangleCount { get; private set; }
        public double LastGenerateMilliseconds { get; private set; }
        public int LastScatterCount { get; private set; }

        public bool UsesBiomes => biomes != null && biomes.Count > 0;

        // ------------------------------------------------------------------
        // Public build API
        // ------------------------------------------------------------------

        [ContextMenu("Generate All (Terrain + Environment)")]
        public void GenerateAll()
        {
            GenerateTerrain();
            ScatterEnvironment();
        }

        [ContextMenu("Generate Terrain")]
        public void GenerateTerrain()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            BuildHeightData();

            int res = _cachedResolution;
            int vertsPerLine = res + 1;
            int vertexCount = vertsPerLine * vertsPerLine;
            Vector2 size = SafeSize;

            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            var triangles = new int[res * res * 6];

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int i = z * vertsPerLine + x;
                    float nx = x / (float)res;
                    float nz = z / (float)res;

                    // Mesh is centered on the transform so the pivot sits in the middle.
                    vertices[i] = new Vector3(
                        (nx - 0.5f) * size.x,
                        _worldHeights[x, z],
                        (nz - 0.5f) * size.y);
                    uvs[i] = new Vector2(nx, nz);

                    Color color = SampleVertexColor(nx * size.x, nz * size.y, _normalizedHeights[x, z]);
                    if (_pathPaintMask != null && _pathPaintMask[x, z] > 0f)
                        color = Color.Lerp(color, _pathPaintColor[x, z], _pathPaintMask[x, z]);
                    colors[i] = color;
                }
            }

            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * vertsPerLine + x;
                    triangles[t++] = i;
                    triangles[t++] = i + vertsPerLine;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + vertsPerLine;
                    triangles[t++] = i + vertsPerLine + 1;
                }
            }

            var meshFilter = GetComponent<MeshFilter>();
            Mesh mesh = meshFilter.sharedMesh;

            // Only reuse a mesh this tool created; never write into a user-assigned
            // or saved mesh asset in place.
            bool reusable = mesh != null && mesh.name == GeneratedMeshName;
#if UNITY_EDITOR
            if (reusable && EditorUtility.IsPersistent(mesh)) reusable = false;
#endif
            if (!reusable)
            {
                mesh = new Mesh { name = GeneratedMeshName };
                meshFilter.sharedMesh = mesh;
            }

            mesh.Clear();
            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            if (autoAssignMaterial) EnsureMaterial();

            BuildWater();

            stopwatch.Stop();
            LastVertexCount = vertexCount;
            LastTriangleCount = triangles.Length / 3;
            LastGenerateMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        [ContextMenu("Scatter Environment")]
        public void ScatterEnvironment()
        {
            List<RuleEntry> table = BuildRuleTable();
            if (table.Count == 0)
            {
                Debug.LogWarning($"[{name}] No environment assets configured (global or per-biome). Drag prefabs into the drop area in the Inspector first.", this);
                return;
            }

            EnsureTerrainData();
            // Height caches can outlive a seed change within one session; the
            // biome gate below samples climate directly, so re-stamp offsets.
            EnsureSamplingState();
            if (zoneBands == null) zoneBands = new TerrainZoneBands();
            zoneBands.Sanitize();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Scatter Environment");
            }
#endif

            ClearEnvironment();

            var root = new GameObject(EnvironmentRootName).transform;
            root.SetParent(transform, false);
            RegisterCreated(root.gameObject);

            // Resolve every footprint first: the occupancy grid's cell size has to
            // cover the largest one, or an overlap could span more cells than a
            // query looks at and slip through.
            float largestFootprint = 0.5f;
            foreach (RuleEntry entry in table)
            {
                EnvironmentAssetRule rule = entry.rule;
                rule.resolvedFootprint = rule.footprintRadius > 0f
                    ? rule.footprintRadius
                    : ScatterRules.EstimateFootprintRadius(rule.prefab);
                largestFootprint = Mathf.Max(largestFootprint, rule.resolvedFootprint);
            }

            // Shared across every rule, which is the whole point: previously each
            // rule only knew about its own instances, so a rock had no idea a tree
            // was already standing there.
            var occupancy = preventAssetOverlap ? new ScatterOccupancy(largestFootprint * 2f) : null;

            // Bigger assets claim their ground first; otherwise a field of grass
            // placed early leaves nowhere legal for a house.
            var order = new List<int>();
            for (int i = 0; i < table.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                float fa = table[a].rule.resolvedFootprint, fb = table[b].rule.resolvedFootprint;
                int cmp = fb.CompareTo(fa);
                return cmp != 0 ? cmp : a.CompareTo(b); // stable, keeps it deterministic
            });

            // One sub-container per biome keeps the hierarchy readable when
            // several biomes each bring their own asset lists.
            var groupRoots = new Dictionary<int, Transform>();

            int total = 0;
            foreach (int tableIndex in order)
            {
                RuleEntry entry = table[tableIndex];
                total += ScatterRule(entry.rule, tableIndex, entry.biomeIndex,
                    GroupRoot(root, groupRoots, entry.biomeIndex), occupancy);
            }

            LastScatterCount = total;
            Debug.Log($"[{name}] Scattered {total} environment instances across {table.Count} asset rule(s).", this);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
#endif
        }

        [ContextMenu("Clear Environment")]
        public void ClearEnvironment()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == EnvironmentRootName)
                    SafeDestroy(child.gameObject);
            }
            LastScatterCount = 0;
        }

        public void RandomizeSeeds()
        {
            seed = UnityEngine.Random.Range(0, 1000000);
            scatterSeed = UnityEngine.Random.Range(0, 1000000);
            biomeSeed = UnityEngine.Random.Range(0, 1000000);
        }

        /// <summary>
        /// Adds a scatter rule for the given prefab with defaults guessed from its
        /// name (trees, rocks, buildings, ground cover and debris get presets).
        /// Pass a biome index to add it to that biome's own list instead of the
        /// global one.
        /// </summary>
        public EnvironmentAssetRule AddEnvironmentAsset(GameObject prefab, int biomeIndex = -1)
        {
            var rule = new EnvironmentAssetRule
            {
                prefab = prefab,
                displayName = prefab != null ? prefab.name : "New Asset",
            };

            ScatterRules.ApplyCategoryDefaults(rule, ScatterRules.GuessCategory(rule.displayName));

            List<EnvironmentAssetRule> target = environmentAssets;
            if (biomeIndex >= 0 && biomes != null && biomeIndex < biomes.Count && biomes[biomeIndex] != null)
                target = biomes[biomeIndex].environmentAssets ??= new List<EnvironmentAssetRule>();

            target.Add(rule);
            return rule;
        }

        void OnValidate()
        {
            zoneBands?.Sanitize();
            BiomeField.Sanitize(biomes);
        }

        // ------------------------------------------------------------------
        // World sampling (biome-aware)
        // ------------------------------------------------------------------

        // Noise is sampled in "sample space": the terrain's local space shifted so
        // coordinates run 0..terrainSize instead of being centered on the pivot.
        // Staying positive keeps Mathf.PerlinNoise away from its mirror at zero,
        // and it is exactly the space the original generator sampled in — which
        // is what keeps old seeds producing their old terrain.

        Vector2 SafeSize => new Vector2(Mathf.Max(1f, terrainSize.x), Mathf.Max(1f, terrainSize.y));

        Vector2 LocalToSample(float localX, float localZ)
        {
            Vector2 size = SafeSize;
            return new Vector2(localX + size.x * 0.5f, localZ + size.y * 0.5f);
        }

        /// <summary>
        /// (Re)builds octave offsets when a seed or offset changed. Cheap enough
        /// to gate every sampler with, so runtime queries (ambience, water) never
        /// read offsets from a previous seed.
        /// </summary>
        void EnsureSamplingState()
        {
            int stamp = TerrainNoise.Hash(seed, biomeSeed, octaves,
                TerrainNoise.Hash(Mathf.RoundToInt(noiseOffset.x * 1000f), Mathf.RoundToInt(noiseOffset.y * 1000f)));
            if (_offsetsBuilt && stamp == _offsetsStamp) return;

            // Offsets from seed, in the original generator's own scheme (origin 0):
            // identical inputs to the old inline loop, so identical terrain.
            _octaveOffsets = TerrainNoise.BuildOctaveOffsets(seed, Mathf.Clamp(octaves, 1, 8), noiseOffset);
            // Two octaves is deliberately soft: biome regions should be big smooth
            // blobs, not as detailed as the terrain inside them.
            _biomeOctaveOffsets = TerrainNoise.BuildOctaveOffsets(biomeSeed, 2, Vector2.zero);
            _warpOffsetsX = TerrainNoise.BuildOctaveOffsets(TerrainNoise.Hash(seed, 7331), 1, Vector2.zero);
            _warpOffsetsY = TerrainNoise.BuildOctaveOffsets(TerrainNoise.Hash(seed, 7333), 1, Vector2.zero);

            _offsetsStamp = stamp;
            _offsetsBuilt = true;
        }

        /// <summary>
        /// Domain warp: bends the coordinate a sample is taken at, by a second
        /// noise field. Applied identically to height AND climate lookups, so
        /// terrain shapes and biome borders wander together instead of the
        /// terrain warping out from under its biome.
        /// </summary>
        void WarpPosition(ref float sampleX, ref float sampleZ)
        {
            if (warpStrength <= 0f) return;
            float x = sampleX, z = sampleZ;
            sampleX += (TerrainNoise.SampleNormalized(x, z, _warpOffsetsX, warpScale, 0.5f, 2f) - 0.5f) * 2f * warpStrength;
            sampleZ += (TerrainNoise.SampleNormalized(x, z, _warpOffsetsY, warpScale, 0.5f, 2f) - 0.5f) * 2f * warpStrength;
        }

        /// <summary>Base terrain noise in 0-1 at a sample-space position. Shared by every biome.</summary>
        float SampleBaseNoise(float sampleX, float sampleZ)
        {
            WarpPosition(ref sampleX, ref sampleZ);
            return TerrainNoise.SampleNormalized(sampleX, sampleZ, _octaveOffsets, noiseScale, persistence, lacunarity);
        }

        /// <summary>
        /// Climate value in 0-1 deciding which biome owns a position. Sampled from
        /// its own, much lower-frequency noise field so regions are far larger than
        /// the hills inside them.
        /// </summary>
        float SampleClimate(float sampleX, float sampleZ)
        {
            WarpPosition(ref sampleX, ref sampleZ);
            return TerrainNoise.SampleNormalized(sampleX, sampleZ, _biomeOctaveOffsets, biomeScale, 0.5f, 2f);
        }

        float[] BiomeWeightsAt(float sampleX, float sampleZ)
        {
            if (_biomeWeights == null || _biomeWeights.Length < biomes.Count)
                _biomeWeights = new float[biomes.Count];
            BiomeField.GetWeights(biomes, SampleClimate(sampleX, sampleZ), biomeBlend, _biomeWeights);
            return _biomeWeights;
        }

        /// <summary>
        /// Height shaped from an already-sampled base noise value. With biomes,
        /// every biome shapes the same base noise through its own curve, multiplier
        /// and offset, and the results blend by biome weight — heights cross a
        /// border smoothly because the weights do.
        /// </summary>
        float HeightFromNormalized(float sampleX, float sampleZ, float normalized)
        {
            if (!UsesBiomes)
                return TerrainNoise.ToWorldHeight(normalized, heightCurve, heightMultiplier);

            float[] weights = BiomeWeightsAt(sampleX, sampleZ);
            float height = 0f;
            for (int i = 0; i < biomes.Count; i++)
            {
                float w = weights[i];
                if (w <= 0f) continue;
                BiomeDefinition biome = biomes[i];
                if (biome == null) continue;
                height += w * (TerrainNoise.ToWorldHeight(normalized, biome.heightCurve, biome.heightMultiplier) + biome.heightOffset);
            }
            return height;
        }

        /// <summary>Ground color at a position: biome gradients blended by weight, so winter whites fade into forest greens.</summary>
        Color SampleVertexColor(float sampleX, float sampleZ, float normalized)
        {
            if (!UsesBiomes) return colorByHeight.Evaluate(normalized);

            float[] weights = BiomeWeightsAt(sampleX, sampleZ);
            Color color = Color.clear;
            for (int i = 0; i < biomes.Count; i++)
            {
                float w = weights[i];
                if (w <= 0f) continue;
                Gradient gradient = biomes[i] != null && biomes[i].colorByHeight != null && biomes[i].colorByHeight.colorKeys.Length > 0
                    ? biomes[i].colorByHeight
                    : colorByHeight;
                color += gradient.Evaluate(normalized) * w;
            }
            color.a = 1f;
            return color;
        }

        /// <summary>
        /// World height of the water surface at a sample-space position: the
        /// biome-blended height that the Water zone threshold maps to. Continuous,
        /// so lakes in a high biome sit higher than lakes in a low one —
        /// consistent with their terrain.
        /// </summary>
        float SampleWaterSurfaceHeight(float sampleX, float sampleZ)
        {
            float threshold = zoneBands != null ? Mathf.Clamp01(zoneBands.waterLevel) : 0.33f;
            return HeightFromNormalized(sampleX, sampleZ, threshold) + waterSurfaceOffset;
        }

        /// <summary>
        /// Dominant biome at a world position, or null when no biomes are defined.
        /// This is the IBiomeSource hook that lets the shared BiomeAmbience
        /// component drive per-biome soundscapes and fog from this terrain.
        /// </summary>
        public BiomeDefinition DominantBiomeAt(Vector3 worldPosition)
        {
            if (!UsesBiomes) return null;
            EnsureSamplingState();

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector2 sample = LocalToSample(local.x, local.z);
            float[] weights = BiomeWeightsAt(sample.x, sample.y);
            int best = 0;
            for (int i = 1; i < biomes.Count; i++)
                if (weights[i] > weights[best]) best = i;
            return biomes[best];
        }

        // ------------------------------------------------------------------
        // Heightmap
        // ------------------------------------------------------------------

        void BuildHeightData()
        {
            int res = Mathf.Clamp(resolution, 2, 1024);
            Vector2 size = SafeSize;

            _cachedResolution = res;
            _normalizedHeights = new float[res + 1, res + 1];
            _worldHeights = new float[res + 1, res + 1];

            EnsureSamplingState();
            if (zoneBands == null) zoneBands = new TerrainZoneBands();
            zoneBands.Sanitize();
            BiomeField.Sanitize(biomes);

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    float nx = x / (float)res;
                    float nz = z / (float)res;
                    float sampleX = nx * size.x;
                    float sampleZ = nz * size.y;

                    float value = SampleBaseNoise(sampleX, sampleZ);

                    if (islandFalloff > 0f)
                        value = Mathf.Clamp01(value - EvaluateFalloff(nx, nz) * islandFalloff);

                    _normalizedHeights[x, z] = value;
                    _worldHeights[x, z] = HeightFromNormalized(sampleX, sampleZ, value);
                }
            }

            ApplyPaths();
        }

        static float EvaluateFalloff(float nx, float nz)
        {
            float fx = Mathf.Abs(nx * 2f - 1f);
            float fz = Mathf.Abs(nz * 2f - 1f);
            float t = Mathf.Max(fx, fz);
            const float a = 3f, b = 2.2f;
            float ta = Mathf.Pow(t, a);
            return ta / (ta + Mathf.Pow(b - b * t, a));
        }

        void EnsureTerrainData()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter.sharedMesh == null)
            {
                // Nothing generated yet: build the whole terrain first.
                GenerateTerrain();
                return;
            }
            if (_worldHeights == null || _cachedResolution != Mathf.Clamp(resolution, 2, 1024))
                BuildHeightData();
        }

        float SampleWorldHeight(float nx, float nz) => SampleBilinear(_worldHeights, nx, nz);
        float SampleNormalizedHeight(float nx, float nz) => SampleBilinear(_normalizedHeights, nx, nz);

        float SampleBilinear(float[,] heights, float nx, float nz)
        {
            int res = _cachedResolution;
            float fx = Mathf.Clamp01(nx) * res;
            float fz = Mathf.Clamp01(nz) * res;
            int x0 = Mathf.Min((int)fx, res);
            int z0 = Mathf.Min((int)fz, res);
            int x1 = Mathf.Min(x0 + 1, res);
            int z1 = Mathf.Min(z0 + 1, res);
            float tx = fx - x0;
            float tz = fz - z0;
            float bottom = Mathf.Lerp(heights[x0, z0], heights[x1, z0], tx);
            float top = Mathf.Lerp(heights[x0, z1], heights[x1, z1], tx);
            return Mathf.Lerp(bottom, top, tz);
        }

        Vector3 SampleLocalNormal(float nx, float nz)
        {
            float e = 1f / _cachedResolution;
            float heightL = SampleWorldHeight(nx - e, nz);
            float heightR = SampleWorldHeight(nx + e, nz);
            float heightD = SampleWorldHeight(nx, nz - e);
            float heightU = SampleWorldHeight(nx, nz + e);
            float dx = (heightR - heightL) / (2f * e * Mathf.Max(1f, terrainSize.x));
            float dz = (heightU - heightD) / (2f * e * Mathf.Max(1f, terrainSize.y));
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        // ------------------------------------------------------------------
        // Paths / roads
        // ------------------------------------------------------------------

        /// <summary>
        /// Bakes every enabled child TerrainPath against the raw heightmap, then
        /// carves their flattened road beds into it and records the paint mask
        /// for the mesh's color pass. Runs inside BuildHeightData so the collider,
        /// the scatterer and the visible mesh all agree on the carved ground.
        /// </summary>
        void ApplyPaths()
        {
            _pathPaintMask = null;
            _pathPaintColor = null;

            _bakedPaths = new List<TerrainPath>();
            foreach (TerrainPath path in GetComponentsInChildren<TerrainPath>(false))
            {
                if (path == null || !path.enabled) continue;

                Vector2 size = SafeSize;
                // Profiles read the pre-carve terrain: every path is baked before
                // any carving happens, so crossing paths sample the same ground.
                path.Bake(transform, (lx, lz) => SampleWorldHeight(lx / size.x + 0.5f, lz / size.y + 0.5f));
                if (path.IsBaked) _bakedPaths.Add(path);
            }
            if (_bakedPaths.Count == 0) return;

            int res = _cachedResolution;
            Vector2 terrain = SafeSize;
            _pathPaintMask = new float[res + 1, res + 1];
            _pathPaintColor = new Color[res + 1, res + 1];

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    var local = new Vector2(
                        (x / (float)res - 0.5f) * terrain.x,
                        (z / (float)res - 0.5f) * terrain.y);

                    // Where routes overlap, the strongest influence wins — both are
                    // near 1 at a junction, so the hand-off stays smooth.
                    float bestFlatten = 0f;
                    float bestCarve = 0f;
                    float bestHeight = 0f;
                    float bestPaint = 0f;
                    Color bestColor = Color.clear;

                    foreach (TerrainPath path in _bakedPaths)
                    {
                        if (!path.SampleInfluence(local, out float flatten, out float paint, out float pathHeight)) continue;
                        if (flatten > bestFlatten)
                        {
                            bestFlatten = flatten;
                            bestCarve = flatten * path.flattenStrength;
                            bestHeight = pathHeight;
                        }
                        if (paint > bestPaint)
                        {
                            bestPaint = paint;
                            bestColor = path.surfaceColor;
                        }
                    }

                    if (bestCarve > 0f)
                        _worldHeights[x, z] = Mathf.Lerp(_worldHeights[x, z], bestHeight, bestCarve);
                    if (bestPaint > 0f)
                    {
                        bestColor.a = 1f;
                        _pathPaintMask[x, z] = bestPaint;
                        _pathPaintColor[x, z] = bestColor;
                    }
                }
            }
        }

        bool PathsBlockScatter(Vector2 localPosition, float footprintRadius)
        {
            if (_bakedPaths == null) return false;
            foreach (TerrainPath path in _bakedPaths)
                if (path.BlocksScatter(localPosition, footprintRadius)) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Water
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds (or hides) the translucent water sheet. One low-resolution grid
        /// covering the whole terrain, with each vertex at the biome-blended
        /// height the Water threshold maps to there. Skipped entirely when the
        /// terrain never dips near the waterline — no overdraw where no water
        /// shows.
        /// </summary>
        void BuildWater()
        {
            Transform existing = transform.Find(WaterRootName);

            Material material = waterEnabled ? TerrainWaterMaterial.Resolve(waterMaterial, ref _autoWaterMaterial) : null;
            if (!waterEnabled || material == null)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            int res = Mathf.Clamp(waterResolution, 2, 128);
            int vertsPerLine = res + 1;
            Vector2 size = SafeSize;

            var vertices = new Vector3[vertsPerLine * vertsPerLine];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            float maxWater = float.NegativeInfinity;

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int i = z * vertsPerLine + x;
                    float nx = x / (float)res;
                    float nz = z / (float)res;
                    float surface = SampleWaterSurfaceHeight(nx * size.x, nz * size.y);
                    if (surface > maxWater) maxWater = surface;
                    vertices[i] = new Vector3((nx - 0.5f) * size.x, surface, (nz - 0.5f) * size.y);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2(nx, nz);
                }
            }

            float minTerrain = float.PositiveInfinity;
            foreach (float height in _worldHeights)
                if (height < minTerrain) minTerrain = height;

            if (minTerrain > maxWater + 0.5f)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            GameObject waterGo;
            if (existing != null)
            {
                waterGo = existing.gameObject;
                waterGo.SetActive(true);
            }
            else
            {
                waterGo = new GameObject(WaterRootName);
                waterGo.transform.SetParent(transform, false);
                RegisterCreated(waterGo);
            }

            var filter = waterGo.GetComponent<MeshFilter>();
            if (filter == null) filter = waterGo.AddComponent<MeshFilter>();
            var renderer = waterGo.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = waterGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            Mesh mesh = filter.sharedMesh;
            bool reusable = mesh != null && mesh.name == GeneratedWaterMeshName;
#if UNITY_EDITOR
            if (reusable && EditorUtility.IsPersistent(mesh)) reusable = false;
#endif
            if (!reusable)
            {
                mesh = new Mesh { name = GeneratedWaterMeshName };
                filter.sharedMesh = mesh;
            }

            var triangles = new int[res * res * 6];
            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * vertsPerLine + x;
                    triangles[t++] = i;
                    triangles[t++] = i + vertsPerLine;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + vertsPerLine;
                    triangles[t++] = i + vertsPerLine + 1;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------
        // Scattering
        // ------------------------------------------------------------------

        struct RuleEntry
        {
            public EnvironmentAssetRule rule;
            public int biomeIndex; // -1 = global rule, appears in every biome
        }

        /// <summary>
        /// Global and per-biome rules flattened into one table. Rule index in this
        /// table seeds each rule's random stream, so with no biomes defined the
        /// table equals the global list and old scatter layouts are reproduced
        /// exactly.
        /// </summary>
        List<RuleEntry> BuildRuleTable()
        {
            var table = new List<RuleEntry>();
            AppendRules(table, environmentAssets, -1);
            if (UsesBiomes)
                for (int b = 0; b < biomes.Count; b++)
                    if (biomes[b] != null) AppendRules(table, biomes[b].environmentAssets, b);
            return table;
        }

        void AppendRules(List<RuleEntry> table, List<EnvironmentAssetRule> rules, int biomeIndex)
        {
            if (rules == null) return;
            foreach (EnvironmentAssetRule rule in rules)
            {
                if (rule == null || rule.prefab == null)
                {
                    if (rule != null)
                        Debug.LogWarning($"[{name}] An environment asset rule has no prefab assigned, skipping.", this);
                    continue;
                }
                table.Add(new RuleEntry { rule = rule, biomeIndex = biomeIndex });
            }
        }

        Transform GroupRoot(Transform root, Dictionary<int, Transform> groupRoots, int biomeIndex)
        {
            if (biomeIndex < 0) return root;
            if (groupRoots.TryGetValue(biomeIndex, out Transform existing)) return existing;

            string biomeName = biomes[biomeIndex] != null && !string.IsNullOrWhiteSpace(biomes[biomeIndex].name)
                ? biomes[biomeIndex].name : $"Biome {biomeIndex}";
            var group = new GameObject(biomeName).transform;
            group.SetParent(root, false);
            RegisterCreated(group.gameObject);
            groupRoots[biomeIndex] = group;
            return group;
        }

        int ScatterRule(EnvironmentAssetRule rule, int tableIndex, int biomeIndex, Transform parent, ScatterOccupancy occupancy)
        {
            int target = Mathf.RoundToInt(rule.density * rule.maxInstances);
            if (target <= 0) return 0;

            string containerName = string.IsNullOrWhiteSpace(rule.displayName) ? rule.prefab.name : rule.displayName.Trim();
            var container = new GameObject(containerName).transform;
            container.SetParent(parent, false);
            RegisterCreated(container.gameObject);

            string tag = PrepareTag(rule.instanceTag);
            // Seed offset by a prime so each rule gets an independent stream from
            // the shared scatter seed.
            var rng = new System.Random(scatterSeed + tableIndex * 7919);
            var placedPositions = new List<Vector2>(target);
            Vector3 prefabScale = rule.prefab.transform.localScale;
            Vector2 size = SafeSize;

            float minHeight = Mathf.Min(rule.minHeight, rule.maxHeight);
            float maxHeight = Mathf.Max(rule.minHeight, rule.maxHeight);
            float spacingSqr = rule.minSpacing * rule.minSpacing;

            // Rejection tallies so a rule that places nothing can say why.
            int rejectedByZone = 0, rejectedByHeight = 0, rejectedBySlope = 0, rejectedBySpacing = 0;
            int rejectedByWeight = 0, rejectedByOverlap = 0, rejectedByBiome = 0, rejectedByPath = 0;

            // Normalizing by the heaviest allowed zone keeps the favourite zone at
            // 100% acceptance, so weighting changes the distribution without
            // silently thinning the total count.
            bool weighted = rule.useZoneWeights && rule.zoneWeights != null;
            float maxWeight = weighted ? rule.zoneWeights.MaxAmong(rule.allowedZones, rule.restrictToZones) : 0f;
            if (weighted && maxWeight <= 0f)
            {
                Debug.LogWarning($"[{name}] '{containerName}': every zone weight is 0, so nothing can spawn. Raise a weight or turn off Use Zone Weights.", this);
                return 0;
            }

            int placed = 0;
            // Weighted sampling discards candidates in the less-favoured zones, and
            // a biome-owned rule discards candidates on foreign ground, so those
            // need a bigger budget to still reach the target count.
            int maxAttempts = target * (weighted ? 30 : 12) * (biomeIndex >= 0 ? 2 : 1);
            for (int attempt = 0; attempt < maxAttempts && placed < target; attempt++)
            {
                float nx = (float)rng.NextDouble();
                float nz = (float)rng.NextDouble();

                // Biome gate: a rule owned by a biome only spawns where that biome
                // holds ground. Acceptance follows the blend weight, so across a
                // border winter trees thin out while forest trees thicken, instead
                // of the two swapping at a hard line.
                if (biomeIndex >= 0)
                {
                    float biomeWeight = BiomeWeightsAt(nx * size.x, nz * size.y)[biomeIndex];
                    if (biomeWeight <= 0.0005f) { rejectedByBiome++; continue; }
                    if (biomeWeight < 0.999f && rng.NextDouble() > biomeWeight) { rejectedByBiome++; continue; }
                }

                float normalizedHeight = SampleNormalizedHeight(nx, nz);

                if (rule.restrictToZones || weighted)
                {
                    TerrainZone zone = zoneBands.GetZone(normalizedHeight);

                    if (rule.restrictToZones && (rule.allowedZones & zone) == 0) { rejectedByZone++; continue; }

                    if (weighted)
                    {
                        float weight = rule.zoneWeights.Get(zone);
                        if (weight <= 0f) { rejectedByWeight++; continue; }
                        // Accept with probability weight/maxWeight -> placement
                        // frequency across zones follows the weight ratios.
                        if (weight < maxWeight && rng.NextDouble() > weight / maxWeight) { rejectedByWeight++; continue; }
                    }
                }

                if (normalizedHeight < minHeight || normalizedHeight > maxHeight) { rejectedByHeight++; continue; }

                Vector3 normal = SampleLocalNormal(nx, nz);
                if (Vector3.Angle(normal, Vector3.up) > rule.maxSlopeAngle) { rejectedBySlope++; continue; }

                float localX = (nx - 0.5f) * size.x;
                float localZ = (nz - 0.5f) * size.y;

                var candidate = new Vector2(localX, localZ);

                if (PathsBlockScatter(candidate, rule.resolvedFootprint)) { rejectedByPath++; continue; }

                if (rule.minSpacing > 0f)
                {
                    bool tooClose = false;
                    for (int p = 0; p < placedPositions.Count; p++)
                    {
                        if ((placedPositions[p] - candidate).sqrMagnitude < spacingSqr) { tooClose = true; break; }
                    }
                    if (tooClose) { rejectedBySpacing++; continue; }
                }

                // Cross-rule check: does anything already placed by ANY rule stand here?
                if (occupancy != null && occupancy.Overlaps(candidate, rule.resolvedFootprint))
                {
                    rejectedByOverlap++;
                    continue;
                }

                GameObject instance = SpawnPrefab(rule.prefab, container);
                instance.name = $"{containerName}_{placed:000}";
                instance.transform.localPosition = new Vector3(localX, SampleWorldHeight(nx, nz) - rule.embedDepth, localZ);

                float yRotation = rule.randomYRotation ? (float)rng.NextDouble() * 360f : 0f;
                Quaternion tilt = Quaternion.Slerp(
                    Quaternion.identity,
                    Quaternion.FromToRotation(Vector3.up, normal),
                    rule.alignToNormal);
                instance.transform.localRotation = tilt * Quaternion.Euler(0f, yRotation, 0f);

                float scaleFactor = Mathf.Lerp(rule.minScale, rule.maxScale, (float)rng.NextDouble());
                instance.transform.localScale = prefabScale * scaleFactor;

                if (tag != null) instance.tag = tag;

                placedPositions.Add(candidate);
                occupancy?.Add(candidate, rule.resolvedFootprint);
                placed++;
            }

            if (placed < target)
            {
                // Name the filter that did the most damage, so the fix is obvious.
                string worst = "slope";
                int worstCount = rejectedBySlope;
                if (rejectedByZone > worstCount) { worst = $"terrain zone (allowed: {rule.allowedZones})"; worstCount = rejectedByZone; }
                if (rejectedByWeight > worstCount) { worst = "zone weights — raise the low ones or turn off Use Zone Weights"; worstCount = rejectedByWeight; }
                if (rejectedByHeight > worstCount) { worst = $"height band ({minHeight:0.##}-{maxHeight:0.##})"; worstCount = rejectedByHeight; }
                if (rejectedBySpacing > worstCount) { worst = $"min spacing ({rule.minSpacing:0.##})"; worstCount = rejectedBySpacing; }
                if (rejectedByOverlap > worstCount) { worst = $"other assets already occupying the ground (footprint {rule.resolvedFootprint:0.##})"; worstCount = rejectedByOverlap; }
                if (rejectedByBiome > worstCount) { worst = "its biome owning too little ground here — grow that biome's Coverage or Biome Scale"; worstCount = rejectedByBiome; }
                if (rejectedByPath > worstCount) { worst = "paths (candidates fell on a roadway)"; worstCount = rejectedByPath; }

                Debug.LogWarning(
                    $"[{name}] '{containerName}': placed {placed}/{target}. Mostly rejected by {worst}. " +
                    $"(zone {rejectedByZone}, weight {rejectedByWeight}, height {rejectedByHeight}, " +
                    $"slope {rejectedBySlope}, spacing {rejectedBySpacing}, overlap {rejectedByOverlap}, " +
                    $"biome {rejectedByBiome}, path {rejectedByPath})", this);
            }

            return placed;
        }

        /// <summary>
        /// Returns a usable tag or null for Untagged. In the Editor, missing tags
        /// are created in the project's Tag Manager; at runtime they can't be, so
        /// unknown tags are dropped with a warning instead of throwing per instance.
        /// </summary>
        string PrepareTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            tag = tag.Trim();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (Array.IndexOf(InternalEditorUtility.tags, tag) < 0)
                {
                    InternalEditorUtility.AddTag(tag);
                    Debug.Log($"[{name}] Added missing tag '{tag}' to the project.", this);
                }
                return tag;
            }
#endif
            try
            {
                GameObject.FindWithTag(tag);
                return tag;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"[{name}] Tag '{tag}' is not defined in the Tag Manager; instances will stay Untagged. Add it in Project Settings > Tags and Layers.", this);
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Editor/runtime shared helpers
        // ------------------------------------------------------------------

        static GameObject SpawnPrefab(GameObject prefab, Transform parent)
        {
            GameObject instance = null;
#if UNITY_EDITOR
            // Keep the prefab connection in the Editor so instances stay editable
            // and update with the prefab. Scene objects fall back to Instantiate.
            if (!Application.isPlaying && EditorUtility.IsPersistent(prefab))
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            if (instance == null)
                instance = Instantiate(prefab, parent);
            RegisterCreated(instance);
            return instance;
        }

        static void RegisterCreated(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(go, "Scatter Environment");
#endif
        }

        static void SafeDestroy(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(go);
                return;
            }
#endif
            Destroy(go);
        }

        /// <summary>
        /// True when the vertex color shader is present in the project. When it is
        /// missing the terrain falls back to URP/Lit, which ignores vertex colors
        /// and renders the height gradient as flat white.
        /// </summary>
        public static bool VertexColorShaderAvailable => Shader.Find(TerrainShaderName) != null;

        /// <summary>
        /// Points the terrain material at the vertex color shader, creating the
        /// material if needed. Returns false if the shader isn't in the project.
        /// </summary>
        public bool ApplyVertexColorShader()
        {
            Shader shader = Shader.Find(TerrainShaderName);
            if (shader == null) return false;

            var meshRenderer = GetComponent<MeshRenderer>();
            Material material = meshRenderer.sharedMaterial;

            if (material == null)
            {
                meshRenderer.sharedMaterial = new Material(shader) { name = GeneratedMaterialName };
                return true;
            }

            if (material.shader != shader) material.shader = shader;
            return true;
        }

        void EnsureMaterial()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            Shader vertexColorShader = Shader.Find(TerrainShaderName);
            Material existing = meshRenderer.sharedMaterial;

            if (existing != null)
            {
                // Upgrade a material this tool created back when the shader was
                // missing (it would have fallen back to URP/Lit). Only touches
                // scene-embedded materials this tool named, never a saved asset or
                // a material the user assigned themselves.
                if (vertexColorShader == null || existing.name != GeneratedMaterialName) return;
                if (existing.shader == vertexColorShader) return;
#if UNITY_EDITOR
                if (EditorUtility.IsPersistent(existing)) return;
#endif
                existing.shader = vertexColorShader;
                Debug.Log($"[{name}] Upgraded '{GeneratedMaterialName}' to the '{TerrainShaderName}' shader so the height gradient shows.", this);
                return;
            }

            Shader shader = vertexColorShader;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;

            if (vertexColorShader == null)
                Debug.LogWarning($"[{name}] Shader '{TerrainShaderName}' not found — falling back to {shader.name}, which ignores vertex colors, so the terrain will render untinted. Add TerrainVertexColor.shader to the project and press 'Fix Terrain Material'.", this);

            meshRenderer.sharedMaterial = new Material(shader) { name = GeneratedMaterialName };
        }

        static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.13f, 0.30f, 0.48f), 0.00f), // deep water
                    new GradientColorKey(new Color(0.24f, 0.50f, 0.62f), 0.28f), // shallows
                    new GradientColorKey(new Color(0.80f, 0.72f, 0.46f), 0.35f), // sand
                    new GradientColorKey(new Color(0.30f, 0.52f, 0.26f), 0.45f), // grass
                    new GradientColorKey(new Color(0.42f, 0.38f, 0.33f), 0.72f), // rock
                    new GradientColorKey(new Color(0.93f, 0.94f, 0.96f), 0.90f), // snow
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            return gradient;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(
                new Vector3(0f, heightMultiplier * 0.5f, 0f),
                new Vector3(terrainSize.x, Mathf.Max(0.01f, heightMultiplier), terrainSize.y));
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Endless chunk-streaming terrain.
    ///
    /// Two problems make streaming worlds hard, and both are solved by the same
    /// property rather than by storage:
    ///
    /// 1. Walking far enough would build a world too heavy to render. Chunks
    ///    outside the view radius are therefore unloaded, and their props pooled,
    ///    so cost stays proportional to view distance rather than to distance
    ///    travelled. Props also use a shorter radius than terrain, because a
    ///    forest 500m away costs a fortune and reads as a green smudge anyway.
    ///
    /// 2. Walking back would show a different world, since regenerating usually
    ///    means re-randomising. Here every chunk's terrain AND its props derive
    ///    from hash(worldSeed, chunkCoord), so rebuilding a chunk reproduces it
    ///    exactly. The world is not saved, it is *recomputed* — which is why it
    ///    can be effectively infinite and still feel like a real place.
    ///
    /// Nothing is written to disk, so this only preserves *generated* state. If
    /// the game later lets players change the world (fell a tree, build a wall),
    /// those edits are deltas and do need storing — see the notes on WorldEdits
    /// in the README.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Otherwise Labs/Infinite Terrain Streamer")]
    public class InfiniteTerrainStreamer : MonoBehaviour
    {
        [Header("Viewer")]
        [Tooltip("Transform the world streams around — usually the player. Falls back to the main camera.")]
        public Transform viewer;

        [Tooltip("How far the viewer must move before chunk visibility is recalculated. Avoids doing the work every frame.")]
        [Min(0.1f)] public float viewerMoveThreshold = 12f;

        [Header("Chunks")]
        [Tooltip("Size of one chunk in world units.")]
        [Min(8f)] public float chunkSize = 120f;

        [Tooltip("Quads per chunk side. Higher = finer terrain, more triangles per chunk.")]
        [Range(4, 254)] public int chunkResolution = 48;

        [Tooltip("Radius, in chunks, of terrain kept loaded around the viewer. 3 = a 7x7 block.")]
        [Range(1, 12)] public int viewDistanceInChunks = 4;

        [Tooltip("Radius, in chunks, within which props are spawned. Keep below View Distance: " +
                 "distant terrain is cheap, distant forests are not.")]
        [Range(0, 12)] public int assetDistanceInChunks = 2;

        [Tooltip("Radius, in chunks, that gets mesh colliders. Only chunks the player can reach need them.")]
        [Range(0, 12)] public int colliderDistanceInChunks = 1;

        [Tooltip("Milliseconds of chunk-building work per frame. The vertex grid is computed a few rows " +
                 "at a time under this budget, so building never hitches a frame — the async fix.")]
        [Range(0.5f, 8f)] public float buildBudgetMs = 3f;

        [Header("Level of Detail")]
        [Tooltip("Chunks within this ring build at full resolution. Held at or above Collider Distance, because colliders must match the visual mesh.")]
        [Range(1, 12)] public int lod0Radius = 2;

        [Tooltip("Chunks out to this ring build at half resolution; beyond it, quarter. LOD is what makes a wide horizon affordable — view cost grows with the square of distance.")]
        [Range(1, 12)] public int lod1Radius = 3;

        [Tooltip("Depth of the vertical skirt hung from every chunk edge, hiding hairline cracks where different LODs meet.")]
        [Min(0.5f)] public float lodSkirtDepth = 3f;

        [Header("World Seed")]
        [Tooltip("Seed for terrain shape. Same seed = same world, forever.")]
        public int seed = 12345;

        [Tooltip("Seed for prop placement.")]
        public int scatterSeed = 54321;

        [Header("Perlin Noise")]
        [Min(0.01f)] public float noiseScale = 90f;
        [Range(1, 8)] public int octaves = 4;
        [Range(0f, 1f)] public float persistence = 0.5f;
        [Range(1f, 4f)] public float lacunarity = 2f;
        public Vector2 noiseOffset;

        [Header("Height Shaping")]
        [Min(0f)] public float heightMultiplier = 30f;
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Rendering")]
        [Tooltip("Material for chunk meshes. Use the vertex colour terrain shader to see the height gradient.")]
        public Material terrainMaterial;

        public Gradient colorByHeight = DefaultGradient();

        [Header("Terrain Zones")]
        public TerrainZoneBands zoneBands = new TerrainZoneBands();

        [Header("Environment Assets")]
        [Tooltip("Prefabs scattered across the world. Max Instances is per chunk here, not per world.")]
        public List<EnvironmentAssetRule> environmentAssets = new List<EnvironmentAssetRule>();

        [Tooltip("Stop assets overlapping each other, across chunk borders too.")]
        public bool preventAssetOverlap = true;

        [Header("Biomes")]
        [Tooltip("Region types with their own assets, heights and colors, laid out along a low-frequency " +
                 "climate noise. A biome only borders its list neighbours, so order the list like a climate " +
                 "gradient (e.g. Winter, Forest, Desert). Empty = one uniform world using the settings above.")]
        public List<BiomeDefinition> biomes = new List<BiomeDefinition>();

        [Tooltip("Size of biome regions in world units. Bigger = larger continents of a single type. " +
                 "Keep well above Noise Scale so regions are much larger than individual hills.")]
        [Min(50f)] public float biomeScale = 600f;

        [Tooltip("Width of the crossfade between neighbouring biomes, as a fraction of the climate range. " +
                 "Bigger = wider, softer borders; smaller = crisper region edges.")]
        [Range(0.01f, 0.4f)] public float biomeBlend = 0.12f;

        [Tooltip("Seed for the biome layout. Re-roll to rearrange which region lands where without changing the terrain detail inside them.")]
        public int biomeSeed = 13579;

        [Header("Water")]
        [Tooltip("Spawn a translucent surface where terrain dips below the Water zone threshold, making lakes real instead of blue-painted ground. Follows biome height shaping, so it stays seamless across chunks.")]
        public bool waterEnabled = false;

        [Tooltip("Material for the water surface. Empty = auto-created from the 'OtherwiseLabs/Terrain Water' shader.")]
        public Material waterMaterial;

        [Tooltip("Drops the surface slightly below the zone threshold so shorelines don't z-fight the sand.")]
        public float waterSurfaceOffset = -0.2f;

        [Header("Domain Warp")]
        [Tooltip("Distorts sample positions with a second noise field before every height and biome lookup, breaking up Perlin's characteristic round blobs. 0 = OFF, and existing worlds keep their exact shape — any other value generates a DIFFERENT world for the same seed.")]
        [Range(0f, 80f)] public float warpStrength = 0f;

        [Tooltip("Feature size of the distortion field in world units.")]
        [Min(10f)] public float warpScale = 250f;

        [Header("Memory")]
        [Tooltip("Pooled instances kept per prefab. Extras are destroyed when trimming.")]
        [Min(0)] public int maxPooledPerPrefab = 256;

        [Header("Scene View Preview")]
        [Tooltip("Build chunks in the Scene view while not playing, so the world can be composed without " +
                 "entering Play mode. Preview objects are never saved into the scene.")]
        public bool previewInScene = false;

        [Tooltip("Preview radius in chunks. Kept small deliberately — the preview builds synchronously.")]
        [Range(0, 4)] public int previewRadiusInChunks = 1;

        [Tooltip("Centre the preview on the Scene view camera, so panning around reveals new terrain. " +
                 "Off centres it on this object instead.")]
        public bool previewFollowsSceneCamera = true;

        [Tooltip("Include props in the preview. Turn off for a faster terrain-only preview.")]
        public bool previewIncludesProps = true;

        // --- runtime state -------------------------------------------------
        readonly Dictionary<Vector2Int, TerrainChunk> _active = new Dictionary<Vector2Int, TerrainChunk>();
        readonly Stack<TerrainChunk> _chunkPool = new Stack<TerrainChunk>();
        readonly Queue<Vector2Int> _buildQueue = new Queue<Vector2Int>();
        readonly HashSet<Vector2Int> _queued = new HashSet<Vector2Int>();

        // Candidate lists are reused by neighbouring chunks' collision checks, so
        // caching them avoids regenerating each chunk's candidates up to 9 times.
        readonly Dictionary<Vector2Int, List<ScatterCandidate>> _candidateCache = new Dictionary<Vector2Int, List<ScatterCandidate>>();

        // Global and per-biome rules flattened into one indexed table. Candidate
        // identity stores indices into this, so its order is part of determinism.
        struct RuleEntry
        {
            public EnvironmentAssetRule rule;
            public int biomeIndex; // -1 = global rule, appears in every biome
        }
        readonly List<RuleEntry> _ruleTable = new List<RuleEntry>();

        PrefabPool _pool;
        Transform _chunkParent;
        Transform _parkingLot;
        Vector2[] _octaveOffsets;
        Vector2[] _biomeOctaveOffsets;
        Vector2[] _warpOffsetsX;
        Vector2[] _warpOffsetsY;
        float[] _biomeWeights;
        Material _autoWaterMaterial;

        // In-flight incremental build (one at a time; the queue feeds it).
        IEnumerator _buildSteps;
        TerrainChunk _buildingChunk;
        Vector2Int _buildingCoord;
        bool _buildingIsNew;
        readonly System.Diagnostics.Stopwatch _buildTimer = new System.Diagnostics.Stopwatch();
        Vector3 _lastViewerPosition;
        bool _initialised;
        Coroutine _buildLoop;

        public int ActiveChunkCount => _active.Count;
        public int PooledChunkCount => _chunkPool.Count;
        public int QueuedChunkCount => _buildQueue.Count;

        void OnEnable()
        {
            Initialise();
            UpdateVisibleChunks(force: true);
            _buildLoop = StartCoroutine(BuildLoop());
        }

        void OnDisable()
        {
            if (_buildLoop != null) StopCoroutine(_buildLoop);
            _buildLoop = null;
        }

        void Initialise()
        {
            if (_initialised) return;

            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;

            // A script recompile wipes the managed dictionaries but leaves the
            // GameObjects behind, so anything from a previous session would be
            // orphaned. Clear it out before building new roots.
            DestroyOrphanedRoots();

            _chunkParent = new GameObject("Chunks").transform;
            _chunkParent.SetParent(transform, false);
            TerrainObjects.MarkTransient(_chunkParent.gameObject);

            _parkingLot = new GameObject("Pool (inactive)").transform;
            _parkingLot.SetParent(transform, false);
            _parkingLot.gameObject.SetActive(false);
            TerrainObjects.MarkTransient(_parkingLot.gameObject);

            _pool = new PrefabPool(_parkingLot);
            RebuildOctaveOffsets();
            RebuildRuleTable();

            _lastViewerPosition = viewer != null ? viewer.position : Vector3.zero;
            _initialised = true;
        }

        void RebuildOctaveOffsets()
        {
            // The world origin shift keeps every sample in positive space, where
            // Perlin doesn't mirror. Without it the world is symmetric about (0,0).
            _octaveOffsets = TerrainNoise.BuildOctaveOffsets(seed, octaves, noiseOffset, TerrainNoise.NoiseOrigin);
            // Two octaves is deliberately soft: biome regions should be big smooth
            // blobs, not as detailed as the terrain inside them.
            _biomeOctaveOffsets = TerrainNoise.BuildOctaveOffsets(biomeSeed, 2, Vector2.zero, TerrainNoise.NoiseOrigin);
            _warpOffsetsX = TerrainNoise.BuildOctaveOffsets(TerrainNoise.Hash(seed, 7331), 1, Vector2.zero, TerrainNoise.NoiseOrigin);
            _warpOffsetsY = TerrainNoise.BuildOctaveOffsets(TerrainNoise.Hash(seed, 7333), 1, Vector2.zero, TerrainNoise.NoiseOrigin);
        }

        /// <summary>
        /// Domain warp: bends the coordinate a sample is taken at, by a second
        /// noise field. Applied identically to height AND climate lookups, so
        /// terrain shapes and biome borders wander together instead of the
        /// terrain warping out from under its biome.
        /// </summary>
        void WarpPosition(ref float worldX, ref float worldZ)
        {
            if (warpStrength <= 0f) return;
            float x = worldX, z = worldZ;
            worldX += (TerrainNoise.SampleNormalized(x, z, _warpOffsetsX, warpScale, 0.5f, 2f) - 0.5f) * 2f * warpStrength;
            worldZ += (TerrainNoise.SampleNormalized(x, z, _warpOffsetsY, warpScale, 0.5f, 2f) - 0.5f) * 2f * warpStrength;
        }

        // ------------------------------------------------------------------
        // World sampling (biome-aware)
        // ------------------------------------------------------------------

        public bool UsesBiomes => biomes != null && biomes.Count > 0;

        /// <summary>Base terrain noise in 0-1 at a world position. Shared by every biome.</summary>
        public float SampleBaseNoise(float worldX, float worldZ)
        {
            if (_octaveOffsets == null) RebuildOctaveOffsets();
            WarpPosition(ref worldX, ref worldZ);
            return TerrainNoise.SampleNormalized(worldX, worldZ, _octaveOffsets, noiseScale, persistence, lacunarity);
        }

        /// <summary>
        /// Climate value in 0-1 deciding which biome owns a position. Sampled from
        /// its own, much lower-frequency noise field so regions are far larger than
        /// the hills inside them.
        /// </summary>
        public float SampleClimate(float worldX, float worldZ)
        {
            if (_biomeOctaveOffsets == null) RebuildOctaveOffsets();
            WarpPosition(ref worldX, ref worldZ);
            return TerrainNoise.SampleNormalized(worldX, worldZ, _biomeOctaveOffsets, biomeScale, 0.5f, 2f);
        }

        float[] BiomeWeightsAt(float worldX, float worldZ)
        {
            if (_biomeWeights == null || _biomeWeights.Length < biomes.Count)
                _biomeWeights = new float[biomes.Count];
            BiomeField.GetWeights(biomes, SampleClimate(worldX, worldZ), biomeBlend, _biomeWeights);
            return _biomeWeights;
        }

        /// <summary>
        /// Dominant biome at a world position, or null when no biomes are defined.
        /// Useful gameplay hook: switch ambience, music or fog when this changes.
        /// </summary>
        public BiomeDefinition DominantBiomeAt(Vector3 worldPosition)
        {
            if (!UsesBiomes) return null;
            float[] weights = BiomeWeightsAt(worldPosition.x, worldPosition.z);
            int best = 0;
            for (int i = 1; i < biomes.Count; i++)
                if (weights[i] > weights[best]) best = i;
            return biomes[best];
        }

        /// <summary>
        /// Height shaped from an already-sampled base noise value. With biomes,
        /// every biome shapes the same base noise through its own curve, multiplier
        /// and offset, and the results blend by biome weight — heights cross a
        /// border smoothly because the weights do.
        /// </summary>
        public float HeightFromNormalized(float worldX, float worldZ, float normalized)
        {
            if (!UsesBiomes)
                return TerrainNoise.ToWorldHeight(normalized, heightCurve, heightMultiplier);

            float[] weights = BiomeWeightsAt(worldX, worldZ);
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

        /// <summary>Final terrain height at a world position, biome blending included.</summary>
        public float SampleWorldHeight(float worldX, float worldZ)
            => HeightFromNormalized(worldX, worldZ, SampleBaseNoise(worldX, worldZ));

        /// <summary>Surface normal by central differences of the final height field, so it stays seamless across chunk AND biome borders.</summary>
        public Vector3 SampleTerrainNormal(float worldX, float worldZ, float step)
        {
            float heightL = SampleWorldHeight(worldX - step, worldZ);
            float heightR = SampleWorldHeight(worldX + step, worldZ);
            float heightD = SampleWorldHeight(worldX, worldZ - step);
            float heightU = SampleWorldHeight(worldX, worldZ + step);
            float dx = (heightR - heightL) / (2f * step);
            float dz = (heightU - heightD) / (2f * step);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        /// <summary>Ground color at a position: biome gradients blended by weight, so winter whites fade into forest greens.</summary>
        public Color SampleVertexColor(float worldX, float worldZ, float normalized)
        {
            if (!UsesBiomes) return colorByHeight.Evaluate(normalized);

            float[] weights = BiomeWeightsAt(worldX, worldZ);
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
        /// World height of the water surface at a position: the biome-blended
        /// height that the Water zone threshold maps to. A continuous function,
        /// so chunk water meshes meet seamlessly, and lakes in a high biome sit
        /// higher than lakes in a low one — consistent with their terrain.
        /// </summary>
        public float SampleWaterSurfaceHeight(float worldX, float worldZ)
        {
            float threshold = zoneBands != null ? Mathf.Clamp01(zoneBands.waterLevel) : 0.33f;
            return HeightFromNormalized(worldX, worldZ, threshold) + waterSurfaceOffset;
        }

        public Material ResolveWaterMaterial()
        {
            if (waterMaterial != null) return waterMaterial;
            if (_autoWaterMaterial == null)
            {
                Shader shader = Shader.Find("OtherwiseLabs/Terrain Water");
                if (shader != null) _autoWaterMaterial = new Material(shader) { name = "Terrain Water (auto)" };
            }
            return _autoWaterMaterial;
        }

        /// <summary>
        /// Flattens the global and per-biome asset rules into one indexed table
        /// and resolves their footprints. Candidate identity stores indices into
        /// this table, so its order is part of the world's determinism.
        /// </summary>
        void RebuildRuleTable()
        {
            SanitizeBiomes();
            _ruleTable.Clear();
            AppendRules(environmentAssets, -1);
            if (UsesBiomes)
                for (int b = 0; b < biomes.Count; b++)
                    if (biomes[b] != null) AppendRules(biomes[b].environmentAssets, b);
        }

        void AppendRules(List<EnvironmentAssetRule> rules, int biomeIndex)
        {
            if (rules == null) return;
            foreach (EnvironmentAssetRule rule in rules)
            {
                if (rule == null || rule.prefab == null) continue;
                float footprint = rule.footprintRadius > 0f
                    ? rule.footprintRadius
                    : ProceduralTerrainGenerator.EstimateFootprintRadius(rule.prefab);

                // Folding same-rule spacing into the radius means one collision pass
                // handles both "not inside another asset" and "not too close to my
                // own kind" — and handles them across chunk borders for free.
                rule.resolvedFootprint = Mathf.Max(footprint, rule.minSpacing * 0.5f);
                _ruleTable.Add(new RuleEntry { rule = rule, biomeIndex = biomeIndex });
            }
        }

        /// <summary>
        /// A list element freshly added in the Inspector arrives zeroed rather
        /// than with field-initializer defaults, which would mean a flat black
        /// biome. Treat that state as "give me sensible defaults".
        /// </summary>
        void SanitizeBiomes()
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
                    biome.colorByHeight = BiomeField.DefaultBiomeGradient();
                if (biome.heightMultiplier <= 0f && biome.heightOffset == 0f)
                    biome.heightMultiplier = 30f;
                biome.environmentAssets ??= new List<EnvironmentAssetRule>();
            }
        }

        void Update()
        {
            if (viewer == null) return;

            if ((viewer.position - _lastViewerPosition).sqrMagnitude >= viewerMoveThreshold * viewerMoveThreshold)
            {
                _lastViewerPosition = viewer.position;
                UpdateVisibleChunks(force: false);
            }
        }

        public Vector2Int ChunkCoordOf(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPosition.x / Mathf.Max(1f, chunkSize)),
                Mathf.FloorToInt(worldPosition.z / Mathf.Max(1f, chunkSize)));
        }

        /// <summary>
        /// Decides which chunks should exist, queues missing ones nearest-first and
        /// retires the ones the viewer has left behind.
        /// </summary>
        public void UpdateVisibleChunks(bool force)
        {
            Initialise();
            if (viewer == null) return;

            Vector2Int center = ChunkCoordOf(viewer.position);
            int radius = Mathf.Max(1, viewDistanceInChunks);

            // Retire anything outside the radius (with one chunk of hysteresis, so
            // standing on a border doesn't thrash load/unload every step).
            var toRelease = new List<Vector2Int>();
            foreach (var kv in _active)
            {
                Vector2Int offset = kv.Key - center;
                if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > radius + 1)
                    toRelease.Add(kv.Key);
            }
            foreach (Vector2Int coord in toRelease) ReleaseChunk(coord);

            // Queue missing chunks, nearest first so the ground under the player
            // appears before the horizon does.
            var wanted = new List<Vector2Int>();
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var coord = new Vector2Int(center.x + dx, center.y + dz);
                    if (_active.ContainsKey(coord) || _queued.Contains(coord)) continue;
                    wanted.Add(coord);
                }
            }
            wanted.Sort((a, b) =>
                (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));

            foreach (Vector2Int coord in wanted)
            {
                // On the first pass, build the chunks that carry colliders right
                // away rather than a few per frame. Otherwise the player spends
                // the first second falling through terrain that does not exist
                // yet, which is the startup drop players actually notice.
                if (force)
                {
                    Vector2Int offset = coord - center;
                    if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) <= colliderDistanceInChunks)
                    {
                        BuildChunkImmediate(coord, 0);
                        continue;
                    }
                }

                _buildQueue.Enqueue(coord);
                _queued.Add(coord);
            }

            // Props and colliders track the viewer independently of the mesh.
            RefreshChunkDetail(center);
            TrimCandidateCache(center, radius + 2);
        }

        int DesiredLod(Vector2Int coord, Vector2Int center)
        {
            int ring = Mathf.Max(Mathf.Abs(coord.x - center.x), Mathf.Abs(coord.y - center.y));
            // The collider ring is pinned to LOD 0: physics must match visuals.
            if (ring <= Mathf.Max(lod0Radius, colliderDistanceInChunks)) return 0;
            return ring <= Mathf.Max(lod1Radius, lod0Radius) ? 1 : 2;
        }

        IEnumerator BuildLoop()
        {
            while (true)
            {
                _buildTimer.Restart();
                double budget = Mathf.Clamp(buildBudgetMs, 0.5f, 8f);
                Vector2Int center = viewer != null ? ChunkCoordOf(viewer.position) : Vector2Int.zero;

                // Pump the in-flight build one vertex row at a time until the
                // frame's budget is spent. One chunk is in flight at a time; the
                // queue feeds the next as soon as it finishes.
                while (_buildTimer.Elapsed.TotalMilliseconds < budget)
                {
                    if (_buildSteps == null && !TryStartNextBuild(center)) break;
                    if (_buildSteps != null && !_buildSteps.MoveNext()) FinishCurrentBuild();
                }

                if (_buildSteps == null && _buildQueue.Count == 0 && viewer != null)
                    RefreshChunkDetail(center);

                yield return null;
            }
        }

        bool TryStartNextBuild(Vector2Int center)
        {
            while (_buildQueue.Count > 0)
            {
                Vector2Int coord = _buildQueue.Dequeue();
                _queued.Remove(coord);

                int lod = DesiredLod(coord, center);
                bool isRebuild = _active.TryGetValue(coord, out TerrainChunk existing);
                if (isRebuild && existing.CurrentLod == lod) continue; // rings moved while queued

                TerrainChunk chunk = existing;
                if (chunk == null)
                    chunk = _chunkPool.Count > 0 ? _chunkPool.Pop() : new TerrainChunk(_chunkParent, terrainMaterial);

                _buildingChunk = chunk;
                _buildingCoord = coord;
                _buildingIsNew = !isRebuild;
                _buildSteps = chunk.BuildSteps(coord, this, lod);
                return true;
            }
            return false;
        }

        void FinishCurrentBuild()
        {
            TerrainChunk chunk = _buildingChunk;
            _buildSteps = null;
            _buildingChunk = null;
            if (chunk == null) return;

            if (_buildingIsNew)
            {
                chunk.SetActive(true);
                _active[_buildingCoord] = chunk;
            }

            if (viewer != null) RefreshChunkDetail(ChunkCoordOf(viewer.position));
        }

        void BuildChunkImmediate(Vector2Int coord, int lod)
        {
            TerrainChunk chunk = _active.TryGetValue(coord, out TerrainChunk existing)
                ? existing
                : _chunkPool.Count > 0 ? _chunkPool.Pop() : new TerrainChunk(_chunkParent, terrainMaterial);
            chunk.SetActive(true);
            chunk.BuildMesh(coord, this, lod);
            _active[coord] = chunk;
        }

        /// <summary>
        /// Adds or removes props and colliders as chunks move through the detail
        /// radii. Terrain stays loaded further out than either.
        /// </summary>
        void RefreshChunkDetail(Vector2Int center)
        {
            foreach (var kv in _active)
            {
                Vector2Int offset = kv.Key - center;
                int ring = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));

                kv.Value.SetCollider(ring <= colliderDistanceInChunks);

                // A chunk whose ring changed gets rebuilt at its new resolution.
                // It keeps showing the old mesh until the rebuild applies, so
                // there is never a hole while the player walks toward it.
                int desiredLod = DesiredLod(kv.Key, center);
                if (kv.Value.CurrentLod >= 0 && kv.Value.CurrentLod != desiredLod
                    && kv.Value != _buildingChunk && !_queued.Contains(kv.Key))
                {
                    _buildQueue.Enqueue(kv.Key);
                    _queued.Add(kv.Key);
                }

                bool wantsProps = ring <= assetDistanceInChunks;
                if (wantsProps && !kv.Value.HasScatter) ScatterChunk(kv.Value);
                else if (!wantsProps && kv.Value.HasScatter) kv.Value.ReleaseProps(_pool);
            }
        }

        void ReleaseChunk(Vector2Int coord)
        {
            // Abandon a half-built pass for a chunk the viewer left behind.
            if (_buildingChunk != null && _buildingCoord == coord)
            {
                _buildSteps = null;
                _buildingChunk = null;
            }

            if (!_active.TryGetValue(coord, out TerrainChunk chunk)) return;
            chunk.ReleaseProps(_pool);
            chunk.SetActive(false);
            _active.Remove(coord);
            _chunkPool.Push(chunk);
            _pool.TrimTo(maxPooledPerPrefab);
        }

        // ------------------------------------------------------------------
        // Deterministic scatter
        // ------------------------------------------------------------------

        /// <summary>
        /// Candidate placements for a chunk, before cross-chunk collision. Pure
        /// function of (scatterSeed, coord) so it can be recomputed by neighbours.
        /// </summary>
        List<ScatterCandidate> GetCandidates(Vector2Int coord)
        {
            if (_candidateCache.TryGetValue(coord, out List<ScatterCandidate> cached)) return cached;

            var candidates = new List<ScatterCandidate>();
            float size = Mathf.Max(1f, chunkSize);
            Vector2 origin = new Vector2(coord.x * size, coord.y * size);
            zoneBands?.Sanitize();

            for (int ruleIndex = 0; ruleIndex < _ruleTable.Count; ruleIndex++)
            {
                RuleEntry entry = _ruleTable[ruleIndex];
                EnvironmentAssetRule rule = entry.rule;

                int target = Mathf.RoundToInt(rule.density * rule.maxInstances);
                if (target <= 0) continue;

                var rng = new System.Random(TerrainNoise.Hash(scatterSeed, coord.x, coord.y, ruleIndex));

                bool weighted = rule.useZoneWeights && rule.zoneWeights != null;
                float maxWeight = weighted ? rule.zoneWeights.MaxAmong(rule.allowedZones, rule.restrictToZones) : 0f;
                if (weighted && maxWeight <= 0f) continue;

                int accepted = 0;
                int maxAttempts = target * (weighted ? 20 : 8);

                for (int attempt = 0; attempt < maxAttempts && accepted < target; attempt++)
                {
                    float worldX = origin.x + (float)rng.NextDouble() * size;
                    float worldZ = origin.y + (float)rng.NextDouble() * size;

                    // Biome gate: a rule owned by a biome only spawns where that
                    // biome holds ground. Acceptance follows the blend weight, so
                    // across a border winter trees thin out while forest trees
                    // thicken, instead of the two swapping at a hard line.
                    if (entry.biomeIndex >= 0)
                    {
                        float biomeWeight = BiomeWeightsAt(worldX, worldZ)[entry.biomeIndex];
                        if (biomeWeight <= 0.0005f) continue;
                        if (biomeWeight < 0.999f && rng.NextDouble() > biomeWeight) continue;
                    }

                    float normalized = SampleBaseNoise(worldX, worldZ);

                    if (rule.restrictToZones || weighted)
                    {
                        TerrainZone zone = zoneBands.GetZone(normalized);
                        if (rule.restrictToZones && (rule.allowedZones & zone) == 0) continue;
                        if (weighted)
                        {
                            float weight = rule.zoneWeights.Get(zone);
                            if (weight <= 0f) continue;
                            if (weight < maxWeight && rng.NextDouble() > weight / maxWeight) continue;
                        }
                    }

                    if (normalized < Mathf.Min(rule.minHeight, rule.maxHeight)) continue;
                    if (normalized > Mathf.Max(rule.minHeight, rule.maxHeight)) continue;

                    float step = size / Mathf.Max(2, chunkResolution);
                    Vector3 normal = SampleTerrainNormal(worldX, worldZ, step);
                    if (Vector3.Angle(normal, Vector3.up) > rule.maxSlopeAngle) continue;

                    candidates.Add(new ScatterCandidate
                    {
                        position = new Vector2(worldX, worldZ),
                        radius = Mathf.Max(0.05f, rule.resolvedFootprint),
                        normalizedHeight = normalized,
                        ruleIndex = ruleIndex,
                        chunkX = coord.x,
                        chunkZ = coord.y,
                        candidateIndex = accepted,
                        order = TerrainNoise.Hash(scatterSeed, coord.x, coord.y, TerrainNoise.Hash(ruleIndex, accepted)),
                    });
                    accepted++;
                }
            }

            _candidateCache[coord] = candidates;
            return candidates;
        }

        void ScatterChunk(TerrainChunk chunk)
        {
            RebuildRuleTable();

            float largest = 1f;
            for (int i = 0; i < _ruleTable.Count; i++)
                largest = Mathf.Max(largest, _ruleTable[i].rule.resolvedFootprint);

            // A one-chunk halo is enough as long as no footprint exceeds a chunk,
            // because conflicts can only reach as far as two radii.
            var field = new CandidateField(largest * 2f);
            var mine = new List<int>();

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var neighbour = new Vector2Int(chunk.Coord.x + dx, chunk.Coord.y + dz);
                    List<ScatterCandidate> candidates = GetCandidates(neighbour);
                    bool isCentre = dx == 0 && dz == 0;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (isCentre) mine.Add(field.Count);
                        field.Add(candidates[i]);
                    }
                }
            }

            var spawned = new List<GameObject>();

            foreach (int index in mine)
            {
                ScatterCandidate candidate = field[index];
                if (preventAssetOverlap && field.IsBlocked(index)) continue;
                if (candidate.ruleIndex >= _ruleTable.Count) continue;

                // Player edits are deltas over the deterministic baseline: a
                // felled tree stays felled when its chunk streams back in.
                long candidateId = WorldEdits.CandidateId(candidate.chunkX, candidate.chunkZ, candidate.ruleIndex, candidate.candidateIndex);
                if (WorldEdits.IsRemoved(chunk.Coord, candidateId)) continue;

                EnvironmentAssetRule rule = _ruleTable[candidate.ruleIndex].rule;
                if (rule == null || rule.prefab == null) continue;

                GameObject instance = _pool.Acquire(rule.prefab, chunk.PropRoot);
                WorldEdits.Tag(instance, chunk.Coord, candidateId);

                float height = HeightFromNormalized(candidate.position.x, candidate.position.y, candidate.normalizedHeight);
                var worldPosition = new Vector3(candidate.position.x, height - rule.embedDepth, candidate.position.y);
                instance.transform.position = worldPosition;

                // Transform variation is rederived from the candidate's identity, so
                // it survives unload/reload without being stored.
                int variationHash = TerrainNoise.Hash(candidate.order, candidate.candidateIndex, candidate.ruleIndex);
                float yaw = rule.randomYRotation ? TerrainNoise.HashToUnit(variationHash) * 360f : 0f;
                float scaleT = TerrainNoise.HashToUnit(TerrainNoise.Hash(variationHash, 7919));

                float step = Mathf.Max(1f, chunkSize) / Mathf.Max(2, chunkResolution);
                Vector3 normal = SampleTerrainNormal(candidate.position.x, candidate.position.y, step);

                Quaternion tilt = Quaternion.Slerp(
                    Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), rule.alignToNormal);
                instance.transform.rotation = tilt * Quaternion.Euler(0f, yaw, 0f);
                instance.transform.localScale = rule.prefab.transform.localScale *
                    Mathf.Lerp(rule.minScale, rule.maxScale, scaleT);

                spawned.Add(instance);
            }

            SpawnRecordedAdditions(chunk, spawned);
            chunk.AdoptProps(spawned);
        }

        /// <summary>Respawns player-placed props recorded for this chunk in WorldEdits.</summary>
        void SpawnRecordedAdditions(TerrainChunk chunk, List<GameObject> spawned)
        {
            foreach (WorldEdits.AddedProp added in WorldEdits.AdditionsFor(chunk.Coord))
            {
                if (added.ruleIndex < 0 || added.ruleIndex >= _ruleTable.Count) continue;
                EnvironmentAssetRule rule = _ruleTable[added.ruleIndex].rule;
                if (rule == null || rule.prefab == null) continue;

                GameObject instance = _pool.Acquire(rule.prefab, chunk.PropRoot);
                instance.transform.position = added.position;
                instance.transform.rotation = Quaternion.Euler(0f, added.yaw, 0f);
                instance.transform.localScale = rule.prefab.transform.localScale * added.scale;
                WorldEdits.Tag(instance, chunk.Coord, added.id);
                spawned.Add(instance);
            }
        }

        /// <summary>
        /// Deterministic fingerprint of one chunk: heights, climate and scatter
        /// candidates, quantized and folded into a hash. Two runs of the same
        /// code and settings must produce identical signatures — the editor's
        /// determinism test records these and compares after code changes, so a
        /// refactor that silently breaks walk-back persistence is caught at once.
        /// </summary>
        public long ComputeChunkSignature(Vector2Int coord)
        {
            RebuildOctaveOffsets();
            RebuildRuleTable();
            zoneBands?.Sanitize();

            float size = Mathf.Max(1f, chunkSize);
            unchecked
            {
                long hash = 1469598103934665603L;
                void Fold(int value) => hash = (hash ^ value) * 1099511628211L;

                for (int gz = 0; gz <= 8; gz++)
                {
                    for (int gx = 0; gx <= 8; gx++)
                    {
                        float worldX = (coord.x + gx / 8f) * size;
                        float worldZ = (coord.y + gz / 8f) * size;
                        Fold(Mathf.RoundToInt(SampleWorldHeight(worldX, worldZ) * 512f));
                        Fold(Mathf.RoundToInt(SampleClimate(worldX, worldZ) * 4096f));
                    }
                }

                List<ScatterCandidate> candidates = GetCandidates(coord);
                Fold(candidates.Count);
                foreach (ScatterCandidate candidate in candidates)
                {
                    Fold(Mathf.RoundToInt(candidate.position.x * 128f));
                    Fold(Mathf.RoundToInt(candidate.position.y * 128f));
                    Fold(candidate.ruleIndex);
                    Fold(candidate.order);
                }
                return hash;
            }
        }

        void TrimCandidateCache(Vector2Int center, int radius)
        {
            if (_candidateCache.Count < 256) return;

            var stale = new List<Vector2Int>();
            foreach (var kv in _candidateCache)
            {
                Vector2Int offset = kv.Key - center;
                if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > radius) stale.Add(kv.Key);
            }
            foreach (Vector2Int coord in stale) _candidateCache.Remove(coord);
        }

        // ------------------------------------------------------------------
        // Scene view preview
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a bounded block of chunks around a point while the editor is not
        /// playing, so the world can be composed in the Scene view.
        ///
        /// Because generation is deterministic, this is not an approximation: what
        /// appears here is exactly what the player will walk through at runtime.
        /// Preview objects carry HideFlags.DontSave, so saving the scene never
        /// bakes them in.
        /// </summary>
        public void BuildScenePreview(Vector3 center)
        {
            if (Application.isPlaying) return;

            Initialise();
            RebuildOctaveOffsets();
            RebuildRuleTable();
            zoneBands?.Sanitize();

            Vector2Int centerCoord = ChunkCoordOf(center);
            int radius = Mathf.Clamp(previewRadiusInChunks, 0, 4);

            var stale = new List<Vector2Int>();
            foreach (var kv in _active)
            {
                Vector2Int offset = kv.Key - centerCoord;
                if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > radius) stale.Add(kv.Key);
            }
            foreach (Vector2Int coord in stale) ReleaseChunk(coord);

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var coord = new Vector2Int(centerCoord.x + dx, centerCoord.y + dz);
                    if (_active.ContainsKey(coord)) continue;

                    TerrainChunk chunk = _chunkPool.Count > 0
                        ? _chunkPool.Pop()
                        : new TerrainChunk(_chunkParent, terrainMaterial);
                    chunk.SetActive(true);
                    chunk.BuildMesh(coord, this, 0);
                    chunk.SetCollider(false); // nothing walks on a preview
                    _active[coord] = chunk;
                }
            }

            foreach (var kv in _active)
            {
                if (previewIncludesProps && !kv.Value.HasScatter) ScatterChunk(kv.Value);
                else if (!previewIncludesProps && kv.Value.HasScatter) kv.Value.ReleaseProps(_pool);
            }

            TrimCandidateCache(centerCoord, radius + 2);
        }

        /// <summary>Tears the preview down and returns the object to a clean state.</summary>
        public void ClearScenePreview()
        {
            var coords = new List<Vector2Int>(_active.Keys);
            foreach (Vector2Int coord in coords) ReleaseChunk(coord);

            while (_chunkPool.Count > 0) _chunkPool.Pop().Destroy();

            _pool?.Clear();
            _candidateCache.Clear();
            _buildQueue.Clear();
            _queued.Clear();

            if (_chunkParent != null) TerrainObjects.DestroyObject(_chunkParent.gameObject);
            if (_parkingLot != null) TerrainObjects.DestroyObject(_parkingLot.gameObject);

            _chunkParent = null;
            _parkingLot = null;
            _pool = null;
            _initialised = false;
        }

        /// <summary>
        /// Removes generated roots left over from a previous editor session. They
        /// survive script recompiles even though the dictionaries tracking them do
        /// not, so without this the scene slowly fills with orphans.
        /// </summary>
        void DestroyOrphanedRoots()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "Chunks" || child.name == "Pool (inactive)")
                    TerrainObjects.DestroyObject(child.gameObject);
            }
        }

        // ------------------------------------------------------------------
        // Editor helpers
        // ------------------------------------------------------------------

        /// <summary>Drops everything and rebuilds. Use after changing world settings.</summary>
        public void RegenerateWorld()
        {
            var coords = new List<Vector2Int>(_active.Keys);
            foreach (Vector2Int coord in coords) ReleaseChunk(coord);

            _buildQueue.Clear();
            _queued.Clear();
            _candidateCache.Clear();

            RebuildOctaveOffsets();
            RebuildRuleTable();
            UpdateVisibleChunks(force: true);
        }

        public void RandomizeSeeds()
        {
            seed = Random.Range(0, 1000000);
            scatterSeed = Random.Range(0, 1000000);
            biomeSeed = Random.Range(0, 1000000);
        }

        static Gradient DefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.13f, 0.30f, 0.48f), 0.00f),
                    new GradientColorKey(new Color(0.24f, 0.50f, 0.62f), 0.28f),
                    new GradientColorKey(new Color(0.80f, 0.72f, 0.46f), 0.35f),
                    new GradientColorKey(new Color(0.30f, 0.52f, 0.26f), 0.45f),
                    new GradientColorKey(new Color(0.42f, 0.38f, 0.33f), 0.72f),
                    new GradientColorKey(new Color(0.93f, 0.94f, 0.96f), 0.90f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        void OnValidate()
        {
            // These radii only make sense nested; silently clamp rather than let a
            // typo spawn props on chunks that have no collider under them.
            assetDistanceInChunks = Mathf.Min(assetDistanceInChunks, viewDistanceInChunks);
            colliderDistanceInChunks = Mathf.Min(colliderDistanceInChunks, viewDistanceInChunks);
            lod0Radius = Mathf.Clamp(lod0Radius, Mathf.Max(1, colliderDistanceInChunks), viewDistanceInChunks);
            lod1Radius = Mathf.Clamp(lod1Radius, lod0Radius, viewDistanceInChunks);
            SanitizeBiomes();
        }

        void OnDrawGizmosSelected()
        {
            if (viewer == null) return;
            float size = Mathf.Max(1f, chunkSize);
            Vector2Int center = ChunkCoordOf(viewer.position);

            DrawRadius(center, size, viewDistanceInChunks, new Color(0.4f, 0.8f, 1f, 0.5f));
            DrawRadius(center, size, assetDistanceInChunks, new Color(0.4f, 1f, 0.5f, 0.6f));
            DrawRadius(center, size, colliderDistanceInChunks, new Color(1f, 0.8f, 0.3f, 0.7f));
        }

        static void DrawRadius(Vector2Int center, float size, int radius, Color color)
        {
            Gizmos.color = color;
            float span = (radius * 2 + 1) * size;
            var middle = new Vector3((center.x + 0.5f) * size, 0f, (center.y + 0.5f) * size);
            Gizmos.DrawWireCube(middle, new Vector3(span, 1f, span));
        }
    }
}

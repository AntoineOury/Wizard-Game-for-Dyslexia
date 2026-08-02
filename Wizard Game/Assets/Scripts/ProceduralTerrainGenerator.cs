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
    /// One scatterable environment asset (tree, rock, building, ...) and the rules
    /// that control where and how it gets placed on the generated terrain.
    /// </summary>
    [Serializable]
    public class EnvironmentAssetRule
    {
        [Tooltip("Prefab to scatter across the terrain.")]
        public GameObject prefab;

        [Tooltip("Name used for the container object and spawned instances (e.g. \"Trees\").")]
        public string displayName = "New Asset";

        [Tooltip("Tag applied to every spawned instance. Missing tags are added to the project automatically in the Editor. Leave empty for Untagged.")]
        public string instanceTag = "";

        [Tooltip("Randomization amount, 0-1. Multiplied by Max Instances to get the target count (0 = none, 1 = Max Instances).")]
        [Range(0f, 1f)] public float density = 0.5f;

        [Tooltip("Instance count when Density is 1.")]
        [Min(1)] public int maxInstances = 150;

        [Header("Transform Randomization")]
        [Tooltip("Random uniform scale range applied on top of the prefab's own scale.")]
        [Min(0.01f)] public float minScale = 0.85f;
        [Min(0.01f)] public float maxScale = 1.2f;

        [Tooltip("Give each instance a random rotation around its Y axis.")]
        public bool randomYRotation = true;

        [Tooltip("How much instances tilt to match the ground slope. 0 = always upright (buildings), 1 = fully aligned to the surface (rocks).")]
        [Range(0f, 1f)] public float alignToNormal = 0.25f;

        [Tooltip("How deep instances sink into the ground, in world units. Useful so rocks and trunks don't float on slopes.")]
        public float embedDepth = 0.1f;

        [Header("Placement Filters")]
        [Tooltip("Reject spots steeper than this angle in degrees (e.g. keep buildings on flat ground).")]
        [Range(0f, 90f)] public float maxSlopeAngle = 45f;

        [Tooltip("Only place on terrain whose normalized height (0 = lowest, 1 = highest) is at or above this value. Use to keep assets out of lakes/beaches.")]
        [Range(0f, 1f)] public float minHeight = 0f;

        [Tooltip("Only place on terrain whose normalized height is at or below this value. Use to keep assets off mountain peaks.")]
        [Range(0f, 1f)] public float maxHeight = 1f;

        [Tooltip("Minimum distance in world units between two instances of this rule. 0 = no spacing check.")]
        [Min(0f)] public float minSpacing = 2f;
    }

    /// <summary>
    /// Procedural terrain + environment generator for Unity 2022.3.
    /// Builds a terrain mesh from multi-octave Perlin noise and scatters prefabs
    /// (trees, rocks, buildings, ...) over it using per-asset placement rules.
    /// Use the custom inspector buttons, or call GenerateAll() at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Otherwise Labs/Procedural Terrain Generator")]
    public class ProceduralTerrainGenerator : MonoBehaviour
    {
        public const string EnvironmentRootName = "-- Environment --";
        const string GeneratedMeshName = "Procedural Terrain Mesh";

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
        [Tooltip("World-space height of the highest terrain point.")]
        [Min(0f)] public float heightMultiplier = 25f;

        [Tooltip("Remaps normalized noise (X: 0-1) to height (Y: 0-1). Flatten the low end for plains/lakes, steepen the top for peaks.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Fades heights down toward the terrain edges to make an island. 0 = off.")]
        [Range(0f, 1f)] public float islandFalloff = 0f;

        [Header("Rendering")]
        [Tooltip("Vertex colors by normalized height (water > sand > grass > rock > snow). Rendered by the included 'OtherwiseLabs/Terrain Vertex Color' shader.")]
        public Gradient colorByHeight = CreateDefaultGradient();

        [Tooltip("If the MeshRenderer has no material, assign one automatically using the included vertex color shader.")]
        public bool autoAssignMaterial = true;

        [Header("Environment Assets")]
        [Tooltip("Prefabs to scatter and their placement rules. Use the drag & drop area below to add entries quickly.")]
        public List<EnvironmentAssetRule> environmentAssets = new List<EnvironmentAssetRule>();

        [Tooltip("Seed for asset placement. Same seed = same layout.")]
        public int scatterSeed = 54321;

        [Header("Editor Behaviour")]
        [Tooltip("Regenerate the terrain mesh automatically whenever a setting changes in the Inspector (Editor only). Scattering still requires a button press.")]
        public bool autoRebuild = true;

        // Height caches from the last generate, used by the scatterer so placement
        // always matches the visible mesh. Not serialized: rebuilt on demand.
        [NonSerialized] float[,] _normalizedHeights;
        [NonSerialized] float[,] _worldHeights;
        [NonSerialized] int _cachedResolution;

        public int LastVertexCount { get; private set; }
        public int LastTriangleCount { get; private set; }
        public double LastGenerateMilliseconds { get; private set; }
        public int LastScatterCount { get; private set; }

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
                        (nx - 0.5f) * terrainSize.x,
                        _worldHeights[x, z],
                        (nz - 0.5f) * terrainSize.y);
                    uvs[i] = new Vector2(nx, nz);
                    colors[i] = colorByHeight.Evaluate(_normalizedHeights[x, z]);
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

            stopwatch.Stop();
            LastVertexCount = vertexCount;
            LastTriangleCount = triangles.Length / 3;
            LastGenerateMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        [ContextMenu("Scatter Environment")]
        public void ScatterEnvironment()
        {
            if (environmentAssets == null || environmentAssets.Count == 0)
            {
                Debug.LogWarning($"[{name}] No environment assets configured. Drag prefabs into the drop area in the Inspector first.", this);
                return;
            }

            EnsureTerrainData();

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

            int total = 0;
            for (int ruleIndex = 0; ruleIndex < environmentAssets.Count; ruleIndex++)
            {
                EnvironmentAssetRule rule = environmentAssets[ruleIndex];
                if (rule == null || rule.prefab == null)
                {
                    Debug.LogWarning($"[{name}] Environment asset #{ruleIndex} has no prefab assigned, skipping.", this);
                    continue;
                }
                total += ScatterRule(rule, ruleIndex, root);
            }

            LastScatterCount = total;
            Debug.Log($"[{name}] Scattered {total} environment instances across {environmentAssets.Count} asset rule(s).", this);

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
        }

        /// <summary>
        /// Adds a scatter rule for the given prefab with defaults guessed from its
        /// name (trees, rocks, buildings and grass get sensible presets).
        /// </summary>
        public EnvironmentAssetRule AddEnvironmentAsset(GameObject prefab)
        {
            var rule = new EnvironmentAssetRule
            {
                prefab = prefab,
                displayName = prefab != null ? prefab.name : "New Asset",
            };

            string n = rule.displayName.ToLowerInvariant();
            if (n.Contains("tree") || n.Contains("pine") || n.Contains("palm"))
            {
                rule.maxSlopeAngle = 32f;
                rule.alignToNormal = 0.15f;
                rule.minSpacing = 4f;
                rule.minHeight = 0.15f;
                rule.maxHeight = 0.8f;
            }
            else if (n.Contains("rock") || n.Contains("stone") || n.Contains("boulder"))
            {
                rule.maxSlopeAngle = 60f;
                rule.alignToNormal = 1f;
                rule.embedDepth = 0.25f;
                rule.minSpacing = 1.5f;
            }
            else if (n.Contains("build") || n.Contains("house") || n.Contains("hut") || n.Contains("tower") || n.Contains("ruin"))
            {
                rule.maxSlopeAngle = 10f;
                rule.alignToNormal = 0f;
                rule.minScale = 1f;
                rule.maxScale = 1f;
                rule.maxInstances = 30;
                rule.minSpacing = 15f;
                rule.minHeight = 0.2f;
                rule.maxHeight = 0.6f;
            }
            else if (n.Contains("grass") || n.Contains("bush") || n.Contains("fern") || n.Contains("flower"))
            {
                rule.maxInstances = 600;
                rule.minSpacing = 0.5f;
                rule.embedDepth = 0.05f;
                rule.alignToNormal = 0.6f;
            }

            environmentAssets.Add(rule);
            return rule;
        }

        // ------------------------------------------------------------------
        // Heightmap
        // ------------------------------------------------------------------

        void BuildHeightData()
        {
            int res = Mathf.Clamp(resolution, 2, 1024);
            int oct = Mathf.Clamp(octaves, 1, 8);
            float pers = Mathf.Clamp01(persistence);
            float lac = Mathf.Max(1f, lacunarity);
            float scale = Mathf.Max(0.01f, noiseScale);
            Vector2 size = new Vector2(Mathf.Max(1f, terrainSize.x), Mathf.Max(1f, terrainSize.y));

            _cachedResolution = res;
            _normalizedHeights = new float[res + 1, res + 1];
            _worldHeights = new float[res + 1, res + 1];

            // Per-octave offsets from the seed. Kept in positive range because
            // Mathf.PerlinNoise mirrors around zero and produces seams there.
            var rng = new System.Random(seed);
            var octaveOffsets = new Vector2[oct];
            for (int o = 0; o < oct; o++)
                octaveOffsets[o] = new Vector2(rng.Next(0, 10000) + noiseOffset.x, rng.Next(0, 10000) + noiseOffset.y);

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    float nx = x / (float)res;
                    float nz = z / (float)res;
                    float worldX = nx * size.x;
                    float worldZ = nz * size.y;

                    float amplitude = 1f;
                    float frequency = 1f;
                    float value = 0f;
                    float amplitudeSum = 0f;

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

                    if (islandFalloff > 0f)
                        value = Mathf.Clamp01(value - EvaluateFalloff(nx, nz) * islandFalloff);

                    _normalizedHeights[x, z] = value;
                    _worldHeights[x, z] = heightCurve.Evaluate(value) * heightMultiplier;
                }
            }
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
        // Scattering
        // ------------------------------------------------------------------

        int ScatterRule(EnvironmentAssetRule rule, int ruleIndex, Transform root)
        {
            int target = Mathf.RoundToInt(rule.density * rule.maxInstances);
            if (target <= 0) return 0;

            string containerName = string.IsNullOrWhiteSpace(rule.displayName) ? rule.prefab.name : rule.displayName.Trim();
            var container = new GameObject(containerName).transform;
            container.SetParent(root, false);
            RegisterCreated(container.gameObject);

            string tag = PrepareTag(rule.instanceTag);
            // Seed offset by a prime so each rule gets an independent stream from
            // the shared scatter seed.
            var rng = new System.Random(scatterSeed + ruleIndex * 7919);
            var placedPositions = new List<Vector2>(target);
            Vector3 prefabScale = rule.prefab.transform.localScale;

            float minHeight = Mathf.Min(rule.minHeight, rule.maxHeight);
            float maxHeight = Mathf.Max(rule.minHeight, rule.maxHeight);
            float spacingSqr = rule.minSpacing * rule.minSpacing;

            int placed = 0;
            int maxAttempts = target * 12;
            for (int attempt = 0; attempt < maxAttempts && placed < target; attempt++)
            {
                float nx = (float)rng.NextDouble();
                float nz = (float)rng.NextDouble();

                float normalizedHeight = SampleNormalizedHeight(nx, nz);
                if (normalizedHeight < minHeight || normalizedHeight > maxHeight) continue;

                Vector3 normal = SampleLocalNormal(nx, nz);
                if (Vector3.Angle(normal, Vector3.up) > rule.maxSlopeAngle) continue;

                float localX = (nx - 0.5f) * terrainSize.x;
                float localZ = (nz - 0.5f) * terrainSize.y;

                if (rule.minSpacing > 0f)
                {
                    bool tooClose = false;
                    var candidate = new Vector2(localX, localZ);
                    for (int p = 0; p < placedPositions.Count; p++)
                    {
                        if ((placedPositions[p] - candidate).sqrMagnitude < spacingSqr) { tooClose = true; break; }
                    }
                    if (tooClose) continue;
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

                placedPositions.Add(new Vector2(localX, localZ));
                placed++;
            }

            if (placed < target)
                Debug.LogWarning($"[{name}] '{containerName}': placed {placed}/{target} instances. Filters (slope/height/spacing) rejected the rest — relax them or lower Min Spacing.", this);

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

        void EnsureMaterial()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial != null) return;

            Shader shader = Shader.Find("OtherwiseLabs/Terrain Vertex Color");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;

            meshRenderer.sharedMaterial = new Material(shader) { name = "Procedural Terrain Material" };
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
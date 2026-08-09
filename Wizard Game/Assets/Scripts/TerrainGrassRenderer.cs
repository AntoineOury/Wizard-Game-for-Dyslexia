using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Ground cover as GPU instances instead of GameObjects: thousands of grass
    /// blades drawn with DrawMeshInstanced, costing a few draw calls rather than
    /// thousands of transforms.
    ///
    /// Placement is deterministic — hash(seed, chunk, blade index) — and blades
    /// only appear in the Grass zone on walkable slopes, so it reads as meadow
    /// rather than confetti. Density falls off by ring: chunks further from the
    /// viewer draw half, then a quarter, of their blades.
    ///
    /// Standalone: drop it on any GameObject and point it at the streamer. Only
    /// public sampling APIs are used, so nothing in the streaming core changes.
    /// </summary>
    [AddComponentMenu("Otherwise Labs/Terrain Grass Renderer")]
    public class TerrainGrassRenderer : MonoBehaviour
    {
        const int MaxPerBatch = 1023; // DrawMeshInstanced hard limit

        [Tooltip("Streamer to sample. Auto-found when empty.")]
        public InfiniteTerrainStreamer streamer;

        [Tooltip("Grass appears around this transform. Falls back to the streamer's viewer.")]
        public Transform viewer;

        [Tooltip("Material for blades. Empty = auto-created from the 'OtherwiseLabs/Terrain Grass' shader. Instancing is forced on either way.")]
        public Material grassMaterial;

        [Header("Coverage")]
        [Range(0, 4)] public int grassRadiusInChunks = 1;

        [Tooltip("Blades attempted per chunk at ring 0. Each ring outward halves this.")]
        [Range(0, 4000)] public int bladesPerChunk = 1200;

        [Tooltip("Reject slopes steeper than this, matching where grass grows.")]
        [Range(5f, 60f)] public float maxSlopeAngle = 38f;

        [Header("Blade Shape")]
        [Min(0.05f)] public float bladeWidth = 0.35f;
        [Min(0.05f)] public float bladeHeight = 0.55f;
        [Tooltip("Random scale spread, so the meadow isn't a uniform carpet.")]
        [Range(0f, 1f)] public float scaleVariation = 0.4f;

        public int grassSeed = 7777;

        readonly Dictionary<Vector2Int, List<Matrix4x4[]>> _batches = new Dictionary<Vector2Int, List<Matrix4x4[]>>();
        readonly List<Vector2Int> _evict = new List<Vector2Int>();
        Mesh _bladeMesh;
        Material _material;

        void OnEnable()
        {
            if (streamer == null) streamer = FindObjectOfType<InfiniteTerrainStreamer>();
        }

        void OnDisable()
        {
            _batches.Clear();
        }

        void Update()
        {
            if (streamer == null) return;
            Transform anchor = viewer != null ? viewer : streamer.viewer;
            if (anchor == null) return;

            if (_bladeMesh == null) _bladeMesh = BuildBladeMesh();
            Material material = ResolveMaterial();
            if (material == null) return;

            Vector2Int center = streamer.ChunkCoordOf(anchor.position);

            // Drop chunks the viewer left; build at most one new chunk per frame
            // so entering a meadow never hitches.
            _evict.Clear();
            foreach (var kv in _batches)
            {
                Vector2Int offset = kv.Key - center;
                if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > grassRadiusInChunks + 1)
                    _evict.Add(kv.Key);
            }
            foreach (Vector2Int coord in _evict) _batches.Remove(coord);

            bool builtOne = false;
            for (int dz = -grassRadiusInChunks; dz <= grassRadiusInChunks; dz++)
            {
                for (int dx = -grassRadiusInChunks; dx <= grassRadiusInChunks; dx++)
                {
                    var coord = new Vector2Int(center.x + dx, center.y + dz);
                    if (_batches.ContainsKey(coord)) continue;
                    if (builtOne) continue;
                    _batches[coord] = BuildChunkBatches(coord, center);
                    builtOne = true;
                }
            }

            foreach (var kv in _batches)
            {
                foreach (Matrix4x4[] batch in kv.Value)
                {
                    Graphics.DrawMeshInstanced(_bladeMesh, 0, material, batch, batch.Length, null,
                        ShadowCastingMode.Off, false);
                }
            }
        }

        Material ResolveMaterial()
        {
            if (grassMaterial != null)
            {
                grassMaterial.enableInstancing = true;
                return grassMaterial;
            }
            if (_material == null)
            {
                Shader shader = Shader.Find("OtherwiseLabs/Terrain Grass");
                if (shader == null) return null;
                _material = new Material(shader) { name = "Terrain Grass (auto)", enableInstancing = true };
            }
            return _material;
        }

        List<Matrix4x4[]> BuildChunkBatches(Vector2Int coord, Vector2Int center)
        {
            var matrices = new List<Matrix4x4>();
            float size = Mathf.Max(1f, streamer.chunkSize);
            Vector2 origin = new Vector2(coord.x * size, coord.y * size);

            int ring = Mathf.Max(Mathf.Abs(coord.x - center.x), Mathf.Abs(coord.y - center.y));
            int target = bladesPerChunk >> Mathf.Clamp(ring, 0, 8);
            float slopeCos = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);

            for (int i = 0; i < target; i++)
            {
                // Pure function of (seed, chunk, index): the same meadow every visit.
                int h = TerrainNoise.Hash(grassSeed, coord.x, coord.y, i);
                float worldX = origin.x + TerrainNoise.HashToUnit(h) * size;
                float worldZ = origin.y + TerrainNoise.HashToUnit(TerrainNoise.Hash(h, 31)) * size;

                float normalized = streamer.SampleBaseNoise(worldX, worldZ);
                if (streamer.zoneBands == null || streamer.zoneBands.GetZone(normalized) != TerrainZone.Grass) continue;

                Vector3 normal = streamer.SampleTerrainNormal(worldX, worldZ, 2f);
                if (normal.y < slopeCos) continue;

                float height = streamer.HeightFromNormalized(worldX, worldZ, normalized);
                float yaw = TerrainNoise.HashToUnit(TerrainNoise.Hash(h, 97)) * 360f;
                float scale = 1f + (TerrainNoise.HashToUnit(TerrainNoise.Hash(h, 131)) - 0.5f) * 2f * scaleVariation;

                matrices.Add(Matrix4x4.TRS(
                    new Vector3(worldX, height, worldZ),
                    Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(scale, scale, scale)));
            }

            var batches = new List<Matrix4x4[]>();
            for (int start = 0; start < matrices.Count; start += MaxPerBatch)
            {
                int count = Mathf.Min(MaxPerBatch, matrices.Count - start);
                var batch = new Matrix4x4[count];
                matrices.CopyTo(start, batch, 0, count);
                batches.Add(batch);
            }
            return batches;
        }

        /// <summary>
        /// Two crossed quads, the classic billboard-free grass card. Vertex color
        /// carries what the shader needs: R = 0 root / 1 tip for the color
        /// gradient, A = bend weight so only tips sway.
        /// </summary>
        Mesh BuildBladeMesh()
        {
            float w = bladeWidth * 0.5f;
            float h = bladeHeight;

            var vertices = new Vector3[]
            {
                new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(-w, h, 0f), new Vector3(w, h, 0f),
                new Vector3(0f, 0f, -w), new Vector3(0f, 0f, w), new Vector3(0f, h, -w), new Vector3(0f, h, w),
            };
            var colors = new Color[]
            {
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f),
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f),
            };
            // Both windings per quad: the shader culls off anyway, but this keeps
            // the mesh correct even with a single-sided material swapped in.
            var triangles = new int[]
            {
                0, 2, 1, 1, 2, 3, 1, 2, 0, 3, 2, 1,
                4, 6, 5, 5, 6, 7, 5, 6, 4, 7, 6, 5,
            };

            var mesh = new Mesh { name = "Grass Blade" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

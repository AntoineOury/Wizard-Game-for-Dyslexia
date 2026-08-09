using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// One square tile of the streaming world.
    ///
    /// A chunk holds no authored data. Its mesh and its scattered props are pure
    /// functions of (world seed, chunk coordinate), so unloading a chunk loses
    /// nothing: walking back rebuilds exactly what was there before. That is what
    /// lets the world feel persistent without storing it.
    ///
    /// Chunks are pooled rather than destroyed, because a player crossing borders
    /// repeatedly would otherwise churn GameObjects and thrash the GC.
    /// </summary>
    public class TerrainChunk
    {
        public Vector2Int Coord { get; private set; }
        public GameObject Root { get; private set; }
        public bool HasScatter { get; private set; }

        /// <summary>LOD the current mesh was built at (0 = full). -1 = never built.</summary>
        public int CurrentLod { get; private set; } = -1;

        public bool IsActive => Root != null && Root.activeSelf;

        readonly MeshFilter _meshFilter;
        readonly MeshRenderer _meshRenderer;
        readonly MeshCollider _meshCollider;
        readonly Transform _propRoot;
        readonly Mesh _mesh;

        GameObject _waterGo;
        Mesh _waterMesh;
        MeshRenderer _waterRenderer;

        // Instances are pooled per prefab so re-entering a chunk reuses objects.
        readonly List<GameObject> _spawned = new List<GameObject>();

        public TerrainChunk(Transform parent, Material material)
        {
            Root = new GameObject("Chunk");
            Root.transform.SetParent(parent, false);
            TerrainObjects.MarkTransient(Root);

            _meshFilter = Root.AddComponent<MeshFilter>();
            _meshRenderer = Root.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = material;

            _meshCollider = Root.AddComponent<MeshCollider>();

            _mesh = new Mesh { name = "Chunk Mesh" };
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;

            _propRoot = new GameObject("Props").transform;
            _propRoot.SetParent(Root.transform, false);
            TerrainObjects.MarkTransient(_propRoot.gameObject);
        }

        public void SetActive(bool active)
        {
            if (Root != null) Root.SetActive(active);
        }

        public void SetCollider(bool enabled)
        {
            if (_meshCollider != null) _meshCollider.enabled = enabled;
        }

        /// <summary>Synchronous build — used by the Scene preview and the startup collider ring.</summary>
        public void BuildMesh(Vector2Int coord, InfiniteTerrainStreamer settings, int lod = 0)
        {
            IEnumerator steps = BuildSteps(coord, settings, lod);
            while (steps.MoveNext()) { }
        }

        /// <summary>
        /// Incremental build: yields once per vertex row so the streamer can
        /// spread the work across frames under a millisecond budget. The old
        /// per-chunk hitch was a full vertex grid — heights, biome blends and a
        /// four-tap normal per vertex — computed in one frame.
        ///
        /// The chunk keeps displaying its previous mesh until the final step
        /// applies the new one, so LOD swaps never flash a hole in the ground.
        ///
        /// Heights come from absolute world position, so neighbouring chunks
        /// sampling a shared edge produce the same vertex height and the seam
        /// closes exactly at equal LOD. Where LODs differ, skirts (below) hide
        /// the T-junction cracks.
        /// </summary>
        public IEnumerator BuildSteps(Vector2Int coord, InfiniteTerrainStreamer settings, int lod)
        {
            Coord = coord;

            int baseRes = Mathf.Clamp(settings.chunkResolution, 8, 254);
            // Each LOD halves the grid; clamped so the far horizon keeps its shape.
            int res = Mathf.Max(8, baseRes >> Mathf.Clamp(lod, 0, 3));
            float chunkSize = Mathf.Max(1f, settings.chunkSize);
            int w = res + 1;
            int gridCount = w * w;
            int skirtCount = 4 * w;
            int vertexCount = gridCount + skirtCount;

            Vector3 origin = new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);

            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            var normals = new Vector3[vertexCount];

            float step = chunkSize / res;
            float minHeight = float.PositiveInfinity;

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int i = z * w + x;

                    // Absolute world position is what makes chunks line up.
                    float worldX = origin.x + x * step;
                    float worldZ = origin.z + z * step;

                    // All sampling goes through the streamer so biome blending
                    // (heights, colors) applies identically to mesh and props.
                    float normalized = settings.SampleBaseNoise(worldX, worldZ);
                    float height = settings.HeightFromNormalized(worldX, worldZ, normalized);
                    if (height < minHeight) minHeight = height;

                    // Local to the chunk, since the chunk root carries the offset.
                    vertices[i] = new Vector3(x * step, height, z * step);
                    uvs[i] = new Vector2(x / (float)res, z / (float)res);
                    colors[i] = settings.SampleVertexColor(worldX, worldZ, normalized);

                    // Analytic normals from the continuous height field rather than
                    // RecalculateNormals, which would only see this chunk's
                    // triangles and leave a lighting seam at every border.
                    normals[i] = settings.SampleTerrainNormal(worldX, worldZ, step);
                }

                yield return null;
            }

            // Skirts: every border vertex duplicated and dropped straight down,
            // hanging a short curtain off each chunk edge. Where a full-res chunk
            // meets a half-res neighbour their edges disagree between shared
            // points (T-junctions); the curtain hides those hairline cracks.
            float skirtDepth = Mathf.Max(0.5f, settings.lodSkirtDepth);
            for (int k = 0; k < w; k++)
            {
                CopySkirtVertex(vertices, uvs, colors, normals, gridCount + k, k, skirtDepth);                    // south, z = 0
                CopySkirtVertex(vertices, uvs, colors, normals, gridCount + w + k, res * w + k, skirtDepth);      // north, z = res
                CopySkirtVertex(vertices, uvs, colors, normals, gridCount + 2 * w + k, k * w, skirtDepth);        // west,  x = 0
                CopySkirtVertex(vertices, uvs, colors, normals, gridCount + 3 * w + k, k * w + res, skirtDepth);  // east,  x = res
            }

            int[] triangles = BuildTriangles(res, w, gridCount);

            // ---- apply: the only step that touches live scene state ----
            Root.name = $"Chunk {coord.x},{coord.y}";
            Root.transform.localPosition = origin;

            _mesh.Clear();
            _mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.colors = colors;
            _mesh.normals = normals;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();

            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;

            BuildWater(settings, origin, chunkSize, minHeight);

            CurrentLod = lod;
        }

        static void CopySkirtVertex(Vector3[] vertices, Vector2[] uvs, Color[] colors, Vector3[] normals,
            int destination, int source, float depth)
        {
            Vector3 dropped = vertices[source];
            dropped.y -= depth;
            vertices[destination] = dropped;
            uvs[destination] = uvs[source];
            colors[destination] = colors[source];
            // Border normal reused so the curtain shades like the ground above it.
            normals[destination] = normals[source];
        }

        static int[] BuildTriangles(int res, int w, int gridCount)
        {
            // Skirt quads are emitted with both windings: they are only ever seen
            // edge-on through cracks, and double-siding them costs almost nothing
            // while guaranteeing no camera angle sees through a back-face.
            var triangles = new int[res * res * 6 + 4 * res * 12];
            int t = 0;

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * w + x;
                    triangles[t++] = i;
                    triangles[t++] = i + w;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + w;
                    triangles[t++] = i + w + 1;
                }
            }

            for (int k = 0; k < res; k++)
            {
                AddSkirtQuad(triangles, ref t, k, k + 1, gridCount + k, gridCount + k + 1);                                   // south
                AddSkirtQuad(triangles, ref t, res * w + k, res * w + k + 1, gridCount + w + k, gridCount + w + k + 1);       // north
                AddSkirtQuad(triangles, ref t, k * w, (k + 1) * w, gridCount + 2 * w + k, gridCount + 2 * w + k + 1);         // west
                AddSkirtQuad(triangles, ref t, k * w + res, (k + 1) * w + res, gridCount + 3 * w + k, gridCount + 3 * w + k + 1); // east
            }

            return triangles;
        }

        static void AddSkirtQuad(int[] triangles, ref int t, int borderA, int borderB, int skirtA, int skirtB)
        {
            triangles[t++] = borderA; triangles[t++] = borderB; triangles[t++] = skirtA;
            triangles[t++] = borderB; triangles[t++] = skirtB; triangles[t++] = skirtA;
            triangles[t++] = borderB; triangles[t++] = borderA; triangles[t++] = skirtA;
            triangles[t++] = skirtB; triangles[t++] = borderB; triangles[t++] = skirtA;
        }

        /// <summary>
        /// A translucent surface where the terrain dips below the Water zone.
        /// The surface height is sampled from the same biome-blended pipeline as
        /// the ground, so neighbouring chunks agree and lakes stay level with
        /// their local terrain rules. Skipped entirely for chunks whose lowest
        /// point sits above the waterline — no overdraw where no water shows.
        /// </summary>
        void BuildWater(InfiniteTerrainStreamer settings, Vector3 origin, float chunkSize, float minTerrainHeight)
        {
            if (!settings.waterEnabled)
            {
                if (_waterGo != null) _waterGo.SetActive(false);
                return;
            }

            const int res = 8;
            int w = res + 1;
            float step = chunkSize / res;

            var vertices = new Vector3[w * w];
            var normals = new Vector3[w * w];
            var uvs = new Vector2[w * w];
            float maxWater = float.NegativeInfinity;

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int i = z * w + x;
                    float worldX = origin.x + x * step;
                    float worldZ = origin.z + z * step;
                    float surface = settings.SampleWaterSurfaceHeight(worldX, worldZ);
                    if (surface > maxWater) maxWater = surface;
                    vertices[i] = new Vector3(x * step, surface, z * step);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2(x / (float)res, z / (float)res);
                }
            }

            if (minTerrainHeight > maxWater + 0.5f)
            {
                if (_waterGo != null) _waterGo.SetActive(false);
                return;
            }

            if (_waterGo == null)
            {
                _waterGo = new GameObject("Water");
                _waterGo.transform.SetParent(Root.transform, false);
                TerrainObjects.MarkTransient(_waterGo);
                var filter = _waterGo.AddComponent<MeshFilter>();
                _waterRenderer = _waterGo.AddComponent<MeshRenderer>();
                _waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _waterMesh = new Mesh { name = "Chunk Water" };
                filter.sharedMesh = _waterMesh;
            }

            _waterGo.SetActive(true);
            _waterRenderer.sharedMaterial = settings.ResolveWaterMaterial();

            var triangles = new int[res * res * 6];
            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * w + x;
                    triangles[t++] = i;
                    triangles[t++] = i + w;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + w;
                    triangles[t++] = i + w + 1;
                }
            }

            _waterMesh.Clear();
            _waterMesh.vertices = vertices;
            _waterMesh.normals = normals;
            _waterMesh.uv = uvs;
            _waterMesh.triangles = triangles;
            _waterMesh.RecalculateBounds();
        }

        /// <summary>Attaches already-resolved prop instances to this chunk.</summary>
        public void AdoptProps(List<GameObject> instances)
        {
            _spawned.AddRange(instances);
            HasScatter = true;
        }

        public Transform PropRoot => _propRoot;

        /// <summary>
        /// Returns props to the pool. The chunk keeps its mesh object for reuse.
        /// </summary>
        public void ReleaseProps(PrefabPool pool)
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) pool.Release(_spawned[i]);
            }
            _spawned.Clear();
            HasScatter = false;
        }

        public void Destroy()
        {
            TerrainObjects.DestroyObject(Root);
            TerrainObjects.DestroyObject(_mesh);
            TerrainObjects.DestroyObject(_waterMesh);
        }
    }
}

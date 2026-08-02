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
        public bool IsActive => Root != null && Root.activeSelf;

        readonly MeshFilter _meshFilter;
        readonly MeshRenderer _meshRenderer;
        readonly MeshCollider _meshCollider;
        readonly Transform _propRoot;
        readonly Mesh _mesh;

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

        /// <summary>
        /// Rebuilds the mesh for a chunk coordinate. Heights come from absolute
        /// world position, so neighbouring chunks sampling the same edge produce
        /// the same vertex height and the seam closes exactly.
        /// </summary>
        public void BuildMesh(Vector2Int coord, InfiniteTerrainStreamer settings, Vector2[] octaveOffsets)
        {
            Coord = coord;
            Root.name = $"Chunk {coord.x},{coord.y}";

            int res = Mathf.Clamp(settings.chunkResolution, 2, 254);
            float chunkSize = Mathf.Max(1f, settings.chunkSize);
            int vertsPerLine = res + 1;
            int vertexCount = vertsPerLine * vertsPerLine;

            Vector3 origin = new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);
            Root.transform.localPosition = origin;

            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            var normals = new Vector3[vertexCount];
            var triangles = new int[res * res * 6];

            float step = chunkSize / res;

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int i = z * vertsPerLine + x;

                    // Absolute world position is what makes chunks line up.
                    float worldX = origin.x + x * step;
                    float worldZ = origin.z + z * step;

                    float normalized = TerrainNoise.SampleNormalized(
                        worldX, worldZ, octaveOffsets,
                        settings.noiseScale, settings.persistence, settings.lacunarity);
                    float height = TerrainNoise.ToWorldHeight(normalized, settings.heightCurve, settings.heightMultiplier);

                    // Local to the chunk, since the chunk root carries the offset.
                    vertices[i] = new Vector3(x * step, height, z * step);
                    uvs[i] = new Vector2(x / (float)res, z / (float)res);
                    colors[i] = settings.colorByHeight.Evaluate(normalized);

                    // Analytic normals from the continuous noise rather than
                    // RecalculateNormals, which would only see this chunk's
                    // triangles and leave a visible lighting seam at every border.
                    normals[i] = TerrainNoise.SampleNormal(
                        worldX, worldZ, step, octaveOffsets,
                        settings.noiseScale, settings.persistence, settings.lacunarity,
                        settings.heightCurve, settings.heightMultiplier);
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
        }
    }
}

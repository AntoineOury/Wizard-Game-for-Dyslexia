using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Identity tag the streamer places on every scattered prop. Gameplay code
    /// passes the tagged object to WorldEdits.RemoveProp to make its removal
    /// permanent.
    /// </summary>
    public class SpawnedPropId : MonoBehaviour
    {
        public Vector2Int chunk;
        public long candidateId;

        public void Assign(Vector2Int owningChunk, long id)
        {
            chunk = owningChunk;
            candidateId = id;
        }
    }

    /// <summary>
    /// Player changes to the generated world, stored as deltas.
    ///
    /// The streamed world is a pure function of the seed, which is what makes it
    /// infinite — but it also means a felled tree grows back the moment its chunk
    /// streams out and in again. This store is the Minecraft trick: the
    /// deterministic layout stays the baseline, and only the differences are
    /// kept. Removals are consulted after scatter candidates resolve; additions
    /// are respawned after them. Only chunks the player actually changed occupy
    /// any memory or disk.
    ///
    /// Saved as JSON in Application.persistentDataPath. Loading is automatic on
    /// first use; saving happens on quit and whenever Save() is called.
    /// </summary>
    public static class WorldEdits
    {
        public struct AddedProp
        {
            public int ruleIndex;
            public Vector3 position;
            public float yaw;
            public float scale;
            public long id;
        }

        [Serializable]
        class AddedPropDto { public int ruleIndex; public Vector3 position; public float yaw; public float scale; public long id; }

        [Serializable]
        class ChunkDto { public int x; public int z; public List<long> removed = new List<long>(); public List<AddedPropDto> added = new List<AddedPropDto>(); }

        [Serializable]
        class SaveDto { public List<ChunkDto> chunks = new List<ChunkDto>(); }

        static Dictionary<Vector2Int, HashSet<long>> _removed;
        static Dictionary<Vector2Int, List<AddedProp>> _added;
        static readonly AddedProp[] Empty = new AddedProp[0];
        static bool _dirty;
        static long _nextAdditionId = 1;

        static string SavePath => Path.Combine(Application.persistentDataPath, "worldedits.json");

        /// <summary>
        /// Stable identity for a scatter candidate, derived purely from what makes
        /// it deterministic: its chunk, rule and index. Survives sessions, builds
        /// and platforms — GetHashCode would not.
        /// </summary>
        public static long CandidateId(int chunkX, int chunkZ, int ruleIndex, int candidateIndex)
        {
            return ((long)(uint)TerrainNoise.Hash(chunkX, chunkZ, ruleIndex, candidateIndex) << 32)
                 | (uint)TerrainNoise.Hash(candidateIndex, ruleIndex, chunkZ, chunkX);
        }

        /// <summary>Attach or refresh the identity tag on a spawned (possibly pooled) instance.</summary>
        public static void Tag(GameObject instance, Vector2Int chunk, long id)
        {
            if (instance == null) return;
            SpawnedPropId tag = instance.GetComponent<SpawnedPropId>();
            if (tag == null) tag = instance.AddComponent<SpawnedPropId>();
            tag.Assign(chunk, id);
        }

        public static bool IsRemoved(Vector2Int chunk, long id)
        {
            EnsureLoaded();
            return _removed.TryGetValue(chunk, out HashSet<long> set) && set.Contains(id);
        }

        /// <summary>
        /// Permanently removes a scattered prop: records the delta and hides the
        /// instance. The chunk's next rebuild simply never spawns it again.
        /// </summary>
        public static void RemoveProp(GameObject instance)
        {
            if (instance == null) return;
            SpawnedPropId tag = instance.GetComponent<SpawnedPropId>();
            if (tag == null)
            {
                Debug.LogWarning($"WorldEdits.RemoveProp: '{instance.name}' carries no SpawnedPropId — not a scattered prop, nothing recorded.", instance);
                return;
            }

            RecordRemoval(tag.chunk, tag.candidateId);
            instance.SetActive(false);
        }

        public static void RecordRemoval(Vector2Int chunk, long id)
        {
            EnsureLoaded();
            if (!_removed.TryGetValue(chunk, out HashSet<long> set))
            {
                set = new HashSet<long>();
                _removed[chunk] = set;
            }
            if (set.Add(id)) _dirty = true;
        }

        /// <summary>
        /// Records a player-placed prop. ruleIndex selects the prefab from the
        /// streamer's rule table; the streamer respawns it whenever the chunk
        /// streams back in. Returns the addition's id (usable with
        /// RecordRemoval to take it back down).
        /// </summary>
        public static long RecordAddition(Vector2Int chunk, int ruleIndex, Vector3 worldPosition, float yaw, float scale)
        {
            EnsureLoaded();
            if (!_added.TryGetValue(chunk, out List<AddedProp> list))
            {
                list = new List<AddedProp>();
                _added[chunk] = list;
            }

            var entry = new AddedProp
            {
                ruleIndex = ruleIndex,
                position = worldPosition,
                yaw = yaw,
                scale = Mathf.Max(0.01f, scale),
                id = -(_nextAdditionId++),   // negative ids can never collide with candidate hashes' space
            };
            list.Add(entry);
            _dirty = true;
            return entry.id;
        }

        public static IReadOnlyList<AddedProp> AdditionsFor(Vector2Int chunk)
        {
            EnsureLoaded();
            // Removals apply to additions too, so a placed-then-removed prop stays gone.
            if (!_added.TryGetValue(chunk, out List<AddedProp> list)) return Empty;
            _removed.TryGetValue(chunk, out HashSet<long> removed);
            if (removed == null) return list;

            var filtered = new List<AddedProp>(list.Count);
            foreach (AddedProp entry in list)
                if (!removed.Contains(entry.id)) filtered.Add(entry);
            return filtered;
        }

        public static void Save()
        {
            if (_removed == null || !_dirty) return;

            var dto = new SaveDto();
            var chunks = new Dictionary<Vector2Int, ChunkDto>();
            ChunkDto For(Vector2Int c)
            {
                if (!chunks.TryGetValue(c, out ChunkDto d))
                {
                    d = new ChunkDto { x = c.x, z = c.y };
                    chunks[c] = d;
                    dto.chunks.Add(d);
                }
                return d;
            }

            foreach (var kv in _removed)
                For(kv.Key).removed.AddRange(kv.Value);
            foreach (var kv in _added)
                foreach (AddedProp entry in kv.Value)
                    For(kv.Key).added.Add(new AddedPropDto { ruleIndex = entry.ruleIndex, position = entry.position, yaw = entry.yaw, scale = entry.scale, id = entry.id });

            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(dto));
                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WorldEdits: save failed — {e.Message}");
            }
        }

        /// <summary>Wipes all recorded edits, in memory and on disk. For testing.</summary>
        public static void ResetAll()
        {
            _removed = new Dictionary<Vector2Int, HashSet<long>>();
            _added = new Dictionary<Vector2Int, List<AddedProp>>();
            _dirty = false;
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning($"WorldEdits: reset failed — {e.Message}"); }
        }

        static void EnsureLoaded()
        {
            if (_removed != null) return;
            _removed = new Dictionary<Vector2Int, HashSet<long>>();
            _added = new Dictionary<Vector2Int, List<AddedProp>>();
            Application.quitting += Save;

            try
            {
                if (!File.Exists(SavePath)) return;
                var dto = JsonUtility.FromJson<SaveDto>(File.ReadAllText(SavePath));
                if (dto?.chunks == null) return;

                foreach (ChunkDto chunk in dto.chunks)
                {
                    var coord = new Vector2Int(chunk.x, chunk.z);
                    if (chunk.removed != null && chunk.removed.Count > 0)
                        _removed[coord] = new HashSet<long>(chunk.removed);
                    if (chunk.added != null)
                    {
                        foreach (AddedPropDto entry in chunk.added)
                        {
                            if (!_added.TryGetValue(coord, out List<AddedProp> list))
                            {
                                list = new List<AddedProp>();
                                _added[coord] = list;
                            }
                            list.Add(new AddedProp { ruleIndex = entry.ruleIndex, position = entry.position, yaw = entry.yaw, scale = entry.scale, id = entry.id });
                            // Keep new addition ids clear of everything loaded.
                            _nextAdditionId = Math.Max(_nextAdditionId, Math.Abs(entry.id) + 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WorldEdits: load failed, starting clean — {e.Message}");
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Object lifetime helpers that behave correctly in both play mode and the
    /// editor. Streaming creates and destroys objects constantly, and the two
    /// contexts disagree about how that is done.
    /// </summary>
    public static class TerrainObjects
    {
        /// <summary>
        /// Object.Destroy is deferred to end of frame, which never arrives while
        /// the editor is not playing — the object would linger. DestroyImmediate
        /// is the edit-mode equivalent, and is illegal during play.
        /// </summary>
        public static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        /// <summary>
        /// Marks generated objects as throwaway while in the editor, so a Scene
        /// view preview never gets serialized into the scene file. Without this,
        /// previewing and saving would bake thousands of chunk objects into the
        /// scene — which is exactly what a streaming world must not do.
        /// </summary>
        public static void MarkTransient(GameObject go)
        {
            if (go == null || Application.isPlaying) return;
            go.hideFlags = HideFlags.DontSave;
        }
    }

    /// <summary>
    /// Reuses prop instances instead of instantiating and destroying them as the
    /// player crosses chunk borders.
    ///
    /// This matters more than it looks: a player pacing back and forth over one
    /// border would otherwise create and destroy hundreds of trees per crossing,
    /// and the resulting GC spikes are exactly the stutter that makes streaming
    /// feel bad.
    /// </summary>
    public class PrefabPool
    {
        readonly Dictionary<GameObject, Stack<GameObject>> _free = new Dictionary<GameObject, Stack<GameObject>>();
        readonly Dictionary<GameObject, GameObject> _sourceOf = new Dictionary<GameObject, GameObject>();
        readonly Transform _parkingLot;

        public PrefabPool(Transform parkingLot)
        {
            _parkingLot = parkingLot;
        }

        public int PooledCount
        {
            get
            {
                int total = 0;
                foreach (var kv in _free) total += kv.Value.Count;
                return total;
            }
        }

        public GameObject Acquire(GameObject prefab, Transform parent)
        {
            GameObject instance = null;

            if (_free.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                while (stack.Count > 0 && instance == null)
                {
                    instance = stack.Pop();  // may be null if destroyed externally
                }
            }

            if (instance == null)
            {
                instance = Object.Instantiate(prefab, parent);
                TerrainObjects.MarkTransient(instance);
                _sourceOf[instance] = prefab;
            }
            else
            {
                instance.transform.SetParent(parent, false);
                instance.SetActive(true);
            }

            return instance;
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;

            if (!_sourceOf.TryGetValue(instance, out GameObject prefab))
            {
                // Not ours; destroy rather than leak it into the pool.
                TerrainObjects.DestroyObject(instance);
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(_parkingLot, false);

            if (!_free.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                _free[prefab] = stack;
            }
            stack.Push(instance);
        }

        /// <summary>Destroys pooled instances, e.g. when trimming memory.</summary>
        public void TrimTo(int maxPerPrefab)
        {
            foreach (var kv in _free)
            {
                Stack<GameObject> stack = kv.Value;
                while (stack.Count > maxPerPrefab)
                {
                    GameObject instance = stack.Pop();
                    if (instance == null) continue;
                    _sourceOf.Remove(instance);
                    TerrainObjects.DestroyObject(instance);
                }
            }
        }

        public void Clear()
        {
            foreach (var kv in _free)
            {
                while (kv.Value.Count > 0)
                {
                    GameObject instance = kv.Value.Pop();
                    TerrainObjects.DestroyObject(instance);
                }
            }
            _free.Clear();
            _sourceOf.Clear();
        }
    }
}

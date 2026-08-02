using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
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
                Object.Destroy(instance);
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
                    Object.Destroy(instance);
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
                    if (instance != null) Object.Destroy(instance);
                }
            }
            _free.Clear();
            _sourceOf.Clear();
        }
    }
}

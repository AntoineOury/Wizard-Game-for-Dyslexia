using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// A placed footprint: where an instance stands and how much room it takes.
    /// Two instances clip if the distance between them is less than the sum of
    /// their radii, which is what stops a rock spawning inside a tree trunk.
    /// </summary>
    public struct ScatterFootprint
    {
        public Vector2 position;  // world XZ
        public float radius;

        public ScatterFootprint(Vector2 position, float radius)
        {
            this.position = position;
            this.radius = radius;
        }
    }

    /// <summary>
    /// Spatial hash of placed footprints. Brute-force distance checks are O(n²)
    /// and get painful past a few hundred instances; bucketing by cell keeps each
    /// query to a handful of comparisons regardless of world size.
    /// </summary>
    public class ScatterOccupancy
    {
        readonly Dictionary<Vector2Int, List<ScatterFootprint>> _buckets = new Dictionary<Vector2Int, List<ScatterFootprint>>();
        readonly float _cellSize;

        /// <param name="cellSize">
        /// Should be at least twice the largest footprint radius, so an overlap can
        /// never span more than the 3x3 block of cells a query inspects.
        /// </param>
        public ScatterOccupancy(float cellSize)
        {
            _cellSize = Mathf.Max(0.5f, cellSize);
        }

        public void Clear() => _buckets.Clear();

        Vector2Int CellOf(Vector2 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / _cellSize),
            Mathf.FloorToInt(position.y / _cellSize));

        /// <summary>True if placing a footprint here would clip an existing one.</summary>
        public bool Overlaps(Vector2 position, float radius)
        {
            Vector2Int center = CellOf(position);

            // A footprint can straddle a cell border, so the neighbours matter too.
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var cell = new Vector2Int(center.x + dx, center.y + dz);
                    if (!_buckets.TryGetValue(cell, out List<ScatterFootprint> bucket)) continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        float minDistance = bucket[i].radius + radius;
                        if ((bucket[i].position - position).sqrMagnitude < minDistance * minDistance)
                            return true;
                    }
                }
            }
            return false;
        }

        public void Add(Vector2 position, float radius)
        {
            Vector2Int cell = CellOf(position);
            if (!_buckets.TryGetValue(cell, out List<ScatterFootprint> bucket))
            {
                bucket = new List<ScatterFootprint>();
                _buckets[cell] = bucket;
            }
            bucket.Add(new ScatterFootprint(position, radius));
        }

        /// <summary>Places the footprint if it fits, and reports whether it did.</summary>
        public bool TryAdd(Vector2 position, float radius)
        {
            if (Overlaps(position, radius)) return false;
            Add(position, radius);
            return true;
        }
    }

    /// <summary>
    /// A candidate placement before it has been accepted. Chunks resolve conflicts
    /// against candidates from their neighbours as well as their own, which is how
    /// a tree at the very edge of one chunk avoids a rock just across the border.
    /// </summary>
    public struct ScatterCandidate
    {
        public Vector2 position;
        public float radius;
        public float normalizedHeight;
        public int ruleIndex;

        // Identity, and the deterministic ordering derived from it.
        public int chunkX, chunkZ, candidateIndex;
        public int order;

        /// <summary>
        /// Strict total order over candidates, derived only from identity — never
        /// from generation order or array position. Two chunks computing the same
        /// candidate therefore agree on which of a conflicting pair wins.
        /// </summary>
        public int CompareTo(in ScatterCandidate other)
        {
            if (order != other.order) return order < other.order ? -1 : 1;
            if (chunkX != other.chunkX) return chunkX < other.chunkX ? -1 : 1;
            if (chunkZ != other.chunkZ) return chunkZ < other.chunkZ ? -1 : 1;
            if (ruleIndex != other.ruleIndex) return ruleIndex < other.ruleIndex ? -1 : 1;
            if (candidateIndex != other.candidateIndex) return candidateIndex < other.candidateIndex ? -1 : 1;
            return 0;
        }
    }

    /// <summary>
    /// Set of candidates covering a chunk and its neighbours, used to decide which
    /// survive.
    ///
    /// The rule is deliberately simple: a candidate loses if ANY lower-ordered
    /// candidate overlaps it — whether or not that one survived itself. Greedy
    /// acceptance would be denser, but it makes a candidate's fate depend on its
    /// neighbour's fate, which in turn depends on candidates outside the halo. This
    /// rule needs nothing beyond footprint range, so every chunk reaches the same
    /// verdict independently. Slightly sparser, always consistent.
    /// </summary>
    public class CandidateField
    {
        readonly Dictionary<Vector2Int, List<int>> _buckets = new Dictionary<Vector2Int, List<int>>();
        readonly List<ScatterCandidate> _candidates = new List<ScatterCandidate>();
        readonly float _cellSize;

        public CandidateField(float cellSize)
        {
            _cellSize = Mathf.Max(0.5f, cellSize);
        }

        public int Count => _candidates.Count;
        public ScatterCandidate this[int index] => _candidates[index];

        Vector2Int CellOf(Vector2 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / _cellSize),
            Mathf.FloorToInt(position.y / _cellSize));

        public void Add(in ScatterCandidate candidate)
        {
            Vector2Int cell = CellOf(candidate.position);
            if (!_buckets.TryGetValue(cell, out List<int> bucket))
            {
                bucket = new List<int>();
                _buckets[cell] = bucket;
            }
            bucket.Add(_candidates.Count);
            _candidates.Add(candidate);
        }

        /// <summary>True if a lower-ordered candidate overlaps this one.</summary>
        public bool IsBlocked(int index)
        {
            ScatterCandidate candidate = _candidates[index];
            Vector2Int center = CellOf(candidate.position);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var cell = new Vector2Int(center.x + dx, center.y + dz);
                    if (!_buckets.TryGetValue(cell, out List<int> bucket)) continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int otherIndex = bucket[i];
                        if (otherIndex == index) continue;

                        ScatterCandidate other = _candidates[otherIndex];
                        if (other.CompareTo(candidate) >= 0) continue; // only lower-ordered candidates win

                        float minDistance = other.radius + candidate.radius;
                        if ((other.position - candidate.position).sqrMagnitude < minDistance * minDistance)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}

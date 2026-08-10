using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// A path or road across generated terrain, authored as child waypoint
    /// objects: add empties under this component and drag them around in the
    /// Scene view — their order in the hierarchy is the order of travel.
    ///
    /// The terrain generator does three things with a path when it rebuilds:
    /// flattens the ground toward a smoothed route profile (cutting through
    /// bumps and filling dips, like a real road bed), tints the surface with the
    /// path color, and keeps scattered props off the roadway.
    ///
    /// Deliberately independent of any terrain system: baking is handed a
    /// height-sampling function, so the component never needs to know who is
    /// asking. Waypoint heights are ignored — the route reads its elevation from
    /// the terrain and smooths it, which is why paths hug the landscape instead
    /// of floating over it.
    /// </summary>
    [AddComponentMenu("Otherwise Labs/Terrain Path")]
    public class TerrainPath : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Width of the fully flattened, painted roadway in world units.")]
        [Min(0.1f)] public float width = 3f;

        [Tooltip("Extra band beyond the roadway where flattening fades back into raw terrain — the embankment. Bigger = softer, wider shoulders.")]
        [Min(0f)] public float shoulderWidth = 2.5f;

        [Tooltip("How strongly terrain is pulled to the route profile. 1 = a level road bed, lower values only soften the ground under the path.")]
        [Range(0f, 1f)] public float flattenStrength = 1f;

        [Tooltip("Smoothing passes over the route's elevation profile. More passes = gentler grades through rough terrain (and deeper cuttings/embankments).")]
        [Range(0, 8)] public int smoothingPasses = 3;

        [Tooltip("Curve samples between two waypoints. Higher = smoother bends, slightly slower rebuilds.")]
        [Range(1, 32)] public int subdivisionsPerSegment = 8;

        [Tooltip("Connect the last waypoint back to the first, for a ring road or race loop.")]
        public bool closedLoop = false;

        [Header("Surface")]
        [Tooltip("Tint the terrain vertex colors along the roadway (packed dirt by default). Off = flatten only, e.g. for an invisible cleared strip.")]
        public bool paintSurface = true;

        public Color surfaceColor = new Color(0.42f, 0.33f, 0.24f);

        [Tooltip("How opaque the paint is at the path center. It always fades out across the shoulder.")]
        [Range(0f, 1f)] public float surfaceOpacity = 0.85f;

        [Header("Scatter")]
        [Tooltip("Extra margin beyond the roadway kept free of scattered props, so branches and boulders don't crowd the verge.")]
        [Min(0f)] public float scatterClearance = 1.5f;

        // Baked route in the terrain's local space. Rebuilt by the generator on
        // every generate and intentionally not serialized: it is derived data.
        [System.NonSerialized] Vector2[] _points;
        [System.NonSerialized] float[] _heights;
        [System.NonSerialized] bool _bakedClosed;
        [System.NonSerialized] Vector2 _boundsMin;
        [System.NonSerialized] Vector2 _boundsMax;
        [System.NonSerialized] Transform _bakedSpace;

        public bool IsBaked => _points != null && _points.Length >= 2;
        public float HalfWidth => Mathf.Max(0.05f, width * 0.5f);
        public float InfluenceRadius => HalfWidth + Mathf.Max(0f, shoulderWidth);
        public int WaypointCount => transform.childCount;

        /// <summary>
        /// Rebuilds the route from the current waypoints: subdivides a Catmull-Rom
        /// spline through them, reads the terrain height at every route point via
        /// <paramref name="sampleHeight"/> (x, z in <paramref name="space"/>'s
        /// local coordinates), then smooths that profile so the road grades
        /// gently instead of copying every bump it crosses.
        /// </summary>
        public void Bake(Transform space, System.Func<float, float, float> sampleHeight)
        {
            _points = null;
            _bakedSpace = space;

            var waypoints = new List<Vector2>();
            for (int i = 0; i < transform.childCount; i++)
            {
                Vector3 local = space.InverseTransformPoint(transform.GetChild(i).position);
                waypoints.Add(new Vector2(local.x, local.z));
            }
            if (waypoints.Count < 2) return;

            bool closed = closedLoop && waypoints.Count >= 3;
            int segments = closed ? waypoints.Count : waypoints.Count - 1;
            int sub = Mathf.Clamp(subdivisionsPerSegment, 1, 32);

            var points = new List<Vector2>(segments * sub + 1);
            for (int s = 0; s < segments; s++)
            {
                Vector2 p0 = waypoints[WaypointIndex(s - 1, waypoints.Count, closed)];
                Vector2 p1 = waypoints[WaypointIndex(s, waypoints.Count, closed)];
                Vector2 p2 = waypoints[WaypointIndex(s + 1, waypoints.Count, closed)];
                Vector2 p3 = waypoints[WaypointIndex(s + 2, waypoints.Count, closed)];

                for (int k = 0; k < sub; k++)
                    points.Add(CatmullRom(p0, p1, p2, p3, k / (float)sub));
            }
            // A closed route wraps implicitly; an open one still needs its final
            // waypoint as an explicit terminal point.
            if (!closed) points.Add(waypoints[waypoints.Count - 1]);

            _points = points.ToArray();
            _bakedClosed = closed;

            _heights = new float[_points.Length];
            for (int i = 0; i < _points.Length; i++)
                _heights[i] = sampleHeight(_points[i].x, _points[i].y);

            // Box-blur the elevation profile. Open ends stay pinned to the ground
            // so the road emerges from the terrain instead of hovering at a
            // smoothed height that no longer matches its surroundings.
            for (int pass = 0; pass < smoothingPasses; pass++)
            {
                var smoothed = (float[])_heights.Clone();
                for (int i = 0; i < _heights.Length; i++)
                {
                    if (!_bakedClosed && (i == 0 || i == _heights.Length - 1)) continue;
                    float previous = _heights[Wrap(i - 1, _heights.Length)];
                    float next = _heights[Wrap(i + 1, _heights.Length)];
                    smoothed[i] = previous * 0.25f + _heights[i] * 0.5f + next * 0.25f;
                }
                _heights = smoothed;
            }

            _boundsMin = _boundsMax = _points[0];
            for (int i = 1; i < _points.Length; i++)
            {
                _boundsMin = Vector2.Min(_boundsMin, _points[i]);
                _boundsMax = Vector2.Max(_boundsMax, _points[i]);
            }
        }

        /// <summary>
        /// The path's effect at a position (same local space it was baked in).
        /// <paramref name="flatten"/> is 1 on the roadway fading to 0 across the
        /// shoulder — NOT yet scaled by Flatten Strength, so the caller stays in
        /// charge of how hard to carve. <paramref name="paint"/> is the surface
        /// tint mask with opacity already applied. <paramref name="pathHeight"/>
        /// is the smoothed road-bed elevation here. False = out of reach.
        /// </summary>
        public bool SampleInfluence(Vector2 position, out float flatten, out float paint, out float pathHeight)
        {
            flatten = 0f;
            paint = 0f;
            pathHeight = 0f;

            float radius = InfluenceRadius;
            if (!WithinBounds(position, radius)) return false;

            float distance = NearestOnRoute(position, out int segment, out float t);
            if (distance >= radius) return false;

            if (distance <= HalfWidth)
            {
                flatten = 1f;
            }
            else
            {
                float u = (distance - HalfWidth) / Mathf.Max(0.0001f, shoulderWidth);
                flatten = 1f - (u * u * (3f - 2f * u)); // smoothstep out across the shoulder
            }

            pathHeight = Mathf.Lerp(_heights[segment], _heights[Wrap(segment + 1, _heights.Length)], t);
            // Squaring keeps the paint tucked inside the flattened band, so bare
            // shoulders frame the dirt instead of tint bleeding onto raw terrain.
            if (paintSurface) paint = flatten * flatten * surfaceOpacity;
            return true;
        }

        /// <summary>
        /// True when a prop with the given footprint would stand on or crowd the
        /// roadway. Used by the scatterer to keep routes walkable.
        /// </summary>
        public bool BlocksScatter(Vector2 position, float footprintRadius)
        {
            float radius = HalfWidth + scatterClearance + Mathf.Max(0f, footprintRadius);
            if (!IsBaked || !WithinBounds(position, radius)) return false;
            return NearestOnRoute(position, out _, out _) < radius;
        }

        bool WithinBounds(Vector2 position, float radius)
        {
            if (!IsBaked) return false;
            return position.x >= _boundsMin.x - radius && position.x <= _boundsMax.x + radius
                && position.y >= _boundsMin.y - radius && position.y <= _boundsMax.y + radius;
        }

        float NearestOnRoute(Vector2 position, out int segment, out float t)
        {
            segment = 0;
            t = 0f;
            float bestSqr = float.PositiveInfinity;

            int segments = _bakedClosed ? _points.Length : _points.Length - 1;
            for (int j = 0; j < segments; j++)
            {
                Vector2 a = _points[j];
                Vector2 b = _points[Wrap(j + 1, _points.Length)];
                Vector2 ab = b - a;
                float lengthSqr = ab.sqrMagnitude;
                float along = lengthSqr > 1e-6f ? Mathf.Clamp01(Vector2.Dot(position - a, ab) / lengthSqr) : 0f;
                Vector2 nearest = a + ab * along;
                float sqr = (position - nearest).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    segment = j;
                    t = along;
                }
            }
            return Mathf.Sqrt(bestSqr);
        }

        static int WaypointIndex(int index, int count, bool closed)
        {
            if (closed) return Wrap(index, count);
            return Mathf.Clamp(index, 0, count - 1); // clamped ends give the spline natural end tangents
        }

        static int Wrap(int index, int count) => ((index % count) + count) % count;

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1
                + (p2 - p0) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (3f * p1 - 3f * p2 + p3 - p0) * t3);
        }

        // Drawn unselected too: waypoints are the objects being dragged, and the
        // ribbon needs to stay visible while a child (not this component) holds
        // the selection.
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.62f, 0.30f, 0.9f);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform waypoint = transform.GetChild(i);
                Gizmos.DrawWireSphere(waypoint.position, 0.4f);
                if (i > 0) Gizmos.DrawLine(transform.GetChild(i - 1).position, waypoint.position);
            }
            if (closedLoop && transform.childCount >= 3)
                Gizmos.DrawLine(transform.GetChild(transform.childCount - 1).position, transform.GetChild(0).position);

            // After a bake, show the real smoothed route and its edges at road-bed
            // height, so cut and fill are visible before the next full rebuild.
            if (!IsBaked || _bakedSpace == null) return;
            Gizmos.color = new Color(0.95f, 0.80f, 0.40f, 0.9f);
            int segments = _bakedClosed ? _points.Length : _points.Length - 1;
            for (int j = 0; j < segments; j++)
            {
                int next = Wrap(j + 1, _points.Length);
                Vector2 direction = (_points[next] - _points[j]).normalized;
                var side = new Vector2(-direction.y, direction.x) * HalfWidth;

                Gizmos.DrawLine(BakedToWorld(_points[j], _heights[j]), BakedToWorld(_points[next], _heights[next]));
                Gizmos.DrawLine(BakedToWorld(_points[j] + side, _heights[j]), BakedToWorld(_points[next] + side, _heights[next]));
                Gizmos.DrawLine(BakedToWorld(_points[j] - side, _heights[j]), BakedToWorld(_points[next] - side, _heights[next]));
            }
        }

        Vector3 BakedToWorld(Vector2 xz, float height)
            => _bakedSpace.TransformPoint(new Vector3(xz.x, height + 0.15f, xz.y));
    }
}

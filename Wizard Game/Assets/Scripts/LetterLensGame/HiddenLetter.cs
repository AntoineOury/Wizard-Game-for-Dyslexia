using System.Collections;
using System.Collections.Generic;
using OtherwiseLabs.CreatureGame;
using OtherwiseLabs.TerrainTools;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    public enum LetterSize
    {
        Bush = 0,      // a few steps away: pebbles, mushrooms, little plants
        House = 1,     // across a dip: rocks, stumps, bushes, small trees
        Building = 2,  // across a valley from a hilltop: boulders and trees
    }

    /// <summary>
    /// One letter hidden in the world by forced perspective. Its dots are
    /// ordinary scenery — rocks, mushrooms, plants, stumps, trees — standing
    /// on the ground at DIFFERENT distances along the sight rays from one
    /// vantage point. Each object is sized so its top lands exactly on its
    /// ray: a pebble where the ray meets the ground, a bush a little nearer,
    /// a tree nearer still whose canopy reaches up into the ray. From the
    /// vantage they line up into the letter; a few steps away near tops slide
    /// past far ones and the letter falls apart into an ordinary meadow.
    ///
    /// Nothing floats, so every sight ray must come down onto the ground:
    /// letters lie on low ground seen from a rise, or on a slope facing the
    /// viewer. Spawned letters find such spots themselves (LetterLensGame).
    ///
    /// This object's transform IS the vantage: position = the eye point,
    /// forward = the sight line through the letter's middle (tilted down at
    /// the ground). A hand-placed one (empty at eye height, aimed at the spot)
    /// builds itself on Start.
    /// </summary>
    public class HiddenLetter : MonoBehaviour
    {
        struct SizePreset
        {
            public float angularHeight; // radians the letter spans from the vantage
            public float dotDiameter;   // apparent dot size, in letter units
            public float dotSpacing;    // between dots, in letter units
            public float ringRadius;    // vantage ring on the ground
        }

        static SizePreset Preset(LetterSize size)
        {
            switch (size)
            {
                case LetterSize.Bush:
                    return new SizePreset { angularHeight = 0.30f, dotDiameter = 0.12f, dotSpacing = 0.1f, ringRadius = 0.45f };
                case LetterSize.House:
                    return new SizePreset { angularHeight = 0.24f, dotDiameter = 0.15f, dotSpacing = 0.12f, ringRadius = 0.8f };
                default:
                    return new SizePreset { angularHeight = 0.22f, dotDiameter = 0.2f, dotSpacing = 0.16f, ringRadius = 1.3f };
            }
        }

        /// <summary>Where along the middle sight line the letter's ground sits, per size (meters).</summary>
        public static Vector2 DistanceRange(LetterSize size)
        {
            switch (size)
            {
                case LetterSize.Bush: return new Vector2(3f, 7f);
                case LetterSize.House: return new Vector2(12f, 28f);
                default: return new Vector2(35f, 85f);
            }
        }

        /// <summary>Angular height a letter of this size spans from its vantage (radians).</summary>
        public static float AngularHeight(LetterSize size) => Preset(size).angularHeight;

        /// <summary>The farthest any of its sight rays may travel before meeting the ground.</summary>
        public static float MaxReach(LetterSize size) => DistanceRange(size).y * 2.5f;

        /// <summary>
        /// The sight ray through a point of the letter (unit box, -0.5..0.5)
        /// for a vantage looking along <paramref name="forward"/>.
        /// </summary>
        public static Vector3 SightRay(Vector3 forward, Vector2 unit, LetterSize size)
        {
            Basis(forward, out Vector3 right, out Vector3 up);
            float k = Mathf.Tan(Preset(size).angularHeight * 0.5f) * 2f; // letter units -> tangent-plane units
            return (forward + right * (unit.x * k) + up * (unit.y * k)).normalized;
        }

        static void Basis(Vector3 forward, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;
            up = Vector3.Cross(forward, right);
        }

        public char letter = 'A';
        public LetterSize size = LetterSize.House;

        [Tooltip("Hand-placed letters build themselves on Start. Spawned ones are built by LetterLensGame.")]
        public bool buildOnStart = true;

        [Tooltip("A faint ring of dots on the ground where the player should stand. Shows when they come near.")]
        public bool showVantageRing = true;

        [Tooltip("Seed for prop choice and dot depths. 0 = derived from position.")]
        public int seed;

        public bool IsBuilt { get; private set; }
        public bool IsFound { get; private set; }

        /// <summary>Where the eye must be for the dots to line up.</summary>
        public Vector3 EyePoint => transform.position;

        /// <summary>A point in the middle of the letter as seen from the vantage (on its middle sight line).</summary>
        public Vector3 Center { get; private set; }

        /// <summary>Distance from the vantage to where the middle sight line meets the ground.</summary>
        public float CenterDistance { get; private set; }

        readonly List<Transform> _dots = new List<Transform>();
        readonly List<Vector2> _units = new List<Vector2>();
        Vector3 _fwd, _right, _up;
        Transform _propRoot;
        Transform _ring;
        float _ringRevealDistance;

        void Start()
        {
            if (buildOnStart && !IsBuilt) Build();
        }

        public void Build()
        {
            if (IsBuilt) return;
            IsBuilt = true;

            int buildSeed = seed != 0 ? seed
                : Mathf.RoundToInt(transform.position.x * 73.1f) ^ Mathf.RoundToInt(transform.position.z * 19.7f) ^ letter;
            var rng = new System.Random(buildSeed);
            SizePreset preset = Preset(size);

            Vector3 eye = transform.position;
            _fwd = transform.forward;
            Basis(_fwd, out _right, out _up);

            float reach = MaxReach(size);
            CenterDistance = RaycastGround(new Ray(eye, _fwd), reach, out float centerHit) ? centerHit : DistanceRange(size).x;
            Center = eye + _fwd * (CenterDistance * 0.8f);

            _propRoot = new GameObject($"Letter {letter} props").transform;
            _propRoot.SetParent(transform, true);

            List<LensProp> props = UsableProps();
            float dotAngle = preset.dotDiameter * preset.angularHeight;  // apparent dot size, radians

            foreach (Vector2 unit in LetterShapes.SamplePath(letter, preset.dotSpacing))
            {
                Vector3 dir = SightRay(_fwd, unit, size);
                if (!RaycastGround(new Ray(eye, dir), reach, out float groundHit)) continue; // sky: nothing can stand there

                if (!TryPlaceDot(eye, dir, groundHit, dotAngle, props, rng, out Transform dot)) continue;
                _dots.Add(dot);
                _units.Add(unit);
            }

            if (showVantageRing) BuildRing(eye, preset.ringRadius);
            _ringRevealDistance = Mathf.Max(6f, CenterDistance * 0.6f);
        }

        /// <summary>
        /// Picks a distance along the ray and a prop whose top lands on the
        /// ray there, at about the dot's apparent size, at a height that prop
        /// could naturally have. Random distances are what make the letter
        /// fall apart from anywhere else; the height ranges are what keep the
        /// scene believable (tall things near, low things where the ray
        /// touches down). Gives up on a dot rather than place something odd.
        /// </summary>
        bool TryPlaceDot(Vector3 eye, Vector3 dir, float groundHit, float dotAngle, List<LensProp> props,
                         System.Random rng, out Transform dot)
        {
            dot = null;
            if (props.Count == 0) props = FallbackProps;

            for (int pass = 0; pass < 2; pass++)
            {
                // First pass: the dot's size. Second: a looser fit, so a few
                // awkward rays still get a dot instead of a gap.
                float minSize = pass == 0 ? 0.6f : 0.4f;
                float maxSize = pass == 0 ? 2.4f : 3.5f;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    float along = 1f - 0.7f * Mathf.Pow((float)rng.NextDouble(), 1.3f);
                    float t = groundHit * along;
                    Vector3 point = eye + dir * t;
                    if (!TryGroundY(point.x, point.z, out float groundY)) continue;
                    float rise = point.y - groundY;
                    if (rise < 0.02f) continue; // the ray is at the ground: nothing's top can be here

                    // Every prop that could naturally have its top right here.
                    _fits.Clear();
                    _fitScales.Clear();
                    foreach (LensProp prop in props)
                    {
                        LensPropFactory.Metrics metrics = LensPropFactory.Measure(prop.prefab);
                        if (!metrics.Valid) continue;
                        float scale = rise / metrics.headCenter;
                        float height = metrics.height * scale;
                        if (height < prop.minHeight || height > prop.maxHeight) continue;
                        float apparent = metrics.headWidth * scale / t;
                        if (apparent < dotAngle * minSize || apparent > dotAngle * maxSize) continue;
                        _fits.Add(prop);
                        _fitScales.Add(scale);
                    }
                    if (_fits.Count == 0) continue;

                    int pick = rng.Next(_fits.Count);
                    dot = LensPropFactory.CreateStanding(_fits[pick].prefab, point, groundY, _fitScales[pick],
                                                         (float)rng.NextDouble() * 360f, _propRoot);
                    if (dot != null) return true;
                }
            }
            return false;
        }

        static readonly List<LensProp> _fits = new List<LensProp>();
        static readonly List<float> _fitScales = new List<float>();

        static readonly List<LensProp> FallbackProps = new List<LensProp>
        {
            new LensProp { prefab = null, minHeight = 0.05f, maxHeight = 6f },
        };

        List<LensProp> UsableProps()
        {
            var usable = new List<LensProp>();
            LetterLensPalette palette = LetterLensPalette.Load();
            if (palette == null || palette.props == null) return usable;
            foreach (LensProp prop in palette.props)
                if (prop != null && prop.prefab != null && prop.maxHeight > prop.minHeight) usable.Add(prop);
            return usable;
        }

        void BuildRing(Vector3 eye, float radius)
        {
            if (!TryGroundY(eye.x, eye.z, out float ground)) return;

            _ring = new GameObject("Vantage Ring").transform;
            _ring.SetParent(transform, true);
            _ring.position = new Vector3(eye.x, ground + 0.03f, eye.z);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = new Color(1f, 0.9f, 0.55f) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(1f, 0.9f, 0.55f));

            const int Count = 14;
            for (int i = 0; i < Count; i++)
            {
                float angle = i * Mathf.PI * 2f / Count;
                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Collider collider = mark.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                mark.name = "Ring Dot";
                mark.transform.SetParent(_ring, false);
                mark.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                mark.transform.localScale = new Vector3(0.09f, 0.01f, 0.09f);
                mark.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            _ring.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!IsBuilt || _ring == null) return;
            Camera camera = Camera.main;
            bool show = !IsFound && camera != null
                && (camera.transform.position - EyePoint).sqrMagnitude < _ringRevealDistance * _ringRevealDistance;
            if (_ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
        }

        /// <summary>
        /// Caught: the letter's pieces leave the world. They hop, then fly one
        /// after another — in stroke order, so the letter unravels the way it
        /// is written — into the player's bag (just below the view), shrinking
        /// as they go. The whole object is destroyed when the last one lands.
        /// </summary>
        public void Collect(Transform bag)
        {
            if (IsFound) return;
            IsFound = true;
            StartCoroutine(CollectRoutine(bag));
        }

        /// <summary>True once every piece has flown into the bag (just before this object goes away).</summary>
        public bool IsCollected { get; private set; }

        IEnumerator CollectRoutine(Transform bag)
        {
            if (_ring != null) _ring.gameObject.SetActive(false);
            foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;

            const float Hop = 0.3f, Flight = 0.8f;
            int count = _dots.Count;
            float stagger = count > 0 ? Mathf.Min(0.035f, 1.2f / count) : 0f;
            var starts = new Vector3[count];
            var hops = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (_dots[i] == null) continue;
                starts[i] = _dots[i].position;
                float distance = Vector3.Distance(starts[i], EyePoint);
                hops[i] = 0.2f + distance * 0.04f;
            }

            float begin = Time.time;
            while (true)
            {
                Vector3 target = bag != null ? bag.position + bag.forward * 1.2f - bag.up * 0.7f : EyePoint;
                bool flying = false;
                for (int i = 0; i < count; i++)
                {
                    Transform dot = _dots[i];
                    if (dot == null || !dot.gameObject.activeSelf) continue;
                    float elapsed = Time.time - begin - i * stagger;
                    flying = true;
                    if (elapsed < 0f) continue;

                    if (elapsed < Hop)
                    {
                        float h = elapsed / Hop;
                        dot.position = starts[i] + Vector3.up * (hops[i] * Mathf.Sin(h * Mathf.PI * 0.5f));
                        dot.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(h * Mathf.PI));
                        continue;
                    }

                    float f = (elapsed - Hop) / Flight;
                    if (f >= 1f) { dot.gameObject.SetActive(false); continue; }
                    float eased = f * f * (3f - 2f * f);
                    Vector3 from = starts[i] + Vector3.up * hops[i];
                    dot.position = Vector3.Lerp(from, target, eased) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * hops[i] * 2f);
                    dot.localScale = Vector3.one * Mathf.Lerp(1f, 0.02f, eased);
                }
                if (!flying) break;
                yield return null;
            }

            IsCollected = true;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------
        // Alignment
        // ------------------------------------------------------------------

        /// <summary>
        /// How badly the dots miss the letter as seen from <paramref name="eye"/>,
        /// in letter units (0 = perfect; the game accepts about 0.07). Measured
        /// in the vantage's own view basis, so where the player happens to be
        /// looking does not matter — only where they stand.
        /// </summary>
        public float Misalignment(Vector3 eye)
            => Fit(eye, _fwd, _right, _up, out _, out _, out float residual) ? residual : float.PositiveInfinity;

        /// <summary>
        /// Projects every dot from <paramref name="eye"/> onto the tangent
        /// plane of the given view basis (x = right/forward, y = up/forward) and
        /// fits the letter's template onto them with a uniform scale plus an
        /// offset — no rotation, players cannot roll their head. The leftover
        /// error, divided back into letter units, is the misalignment.
        /// <paramref name="offset"/>/<paramref name="scale"/> map letter units
        /// to tangent coordinates: tangent = unit * scale + offset.
        /// </summary>
        public bool Fit(Vector3 eye, Vector3 forward, Vector3 right, Vector3 up,
                        out Vector2 offset, out float scale, out float residual)
        {
            offset = Vector2.zero;
            scale = 0f;
            residual = float.PositiveInfinity;
            int n = _dots.Count;
            if (n < 3) return false;

            var projected = new Vector2[n];
            Vector2 meanUnit = Vector2.zero, meanProjected = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                if (_dots[i] == null) return false;
                Vector3 d = _dots[i].position - eye;
                float z = Vector3.Dot(d, forward);
                if (z < 0.1f) return false; // a dot behind the eye: not looking at this letter at all
                projected[i] = new Vector2(Vector3.Dot(d, right) / z, Vector3.Dot(d, up) / z);
                meanUnit += _units[i];
                meanProjected += projected[i];
            }
            meanUnit /= n;
            meanProjected /= n;

            float numerator = 0f, denominator = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 u = _units[i] - meanUnit;
                numerator += Vector2.Dot(u, projected[i] - meanProjected);
                denominator += u.sqrMagnitude;
            }
            if (denominator < 1e-6f || numerator <= 0f) return false;

            scale = numerator / denominator;
            offset = meanProjected - meanUnit * scale;

            float error = 0f;
            for (int i = 0; i < n; i++)
                error += (_units[i] * scale + offset - projected[i]).sqrMagnitude;
            residual = Mathf.Sqrt(error / n) / scale;
            return true;
        }

        // ------------------------------------------------------------------
        // Ground queries
        // ------------------------------------------------------------------

        static Collider _terrainCollider;
        static bool _searchedTerrain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _terrainCollider = null;
            _searchedTerrain = false;
        }

        /// <summary>
        /// The terrain's own collider when the scene has a generated terrain,
        /// so trees and other scenery never count as "ground".
        /// </summary>
        static Collider TerrainCollider
        {
            get
            {
                if (_terrainCollider == null && !_searchedTerrain)
                {
                    _searchedTerrain = true;
                    var generator = FindObjectOfType<ProceduralTerrainGenerator>();
                    if (generator != null) _terrainCollider = generator.GetComponent<MeshCollider>();
                }
                return _terrainCollider;
            }
        }

        public static void ForgetTerrain()
        {
            _terrainCollider = null;
            _searchedTerrain = false;
        }

        public static bool TryGroundY(float x, float z, out float groundY)
        {
            groundY = 0f;
            var ray = new Ray(new Vector3(x, 2000f, z), Vector3.down);
            if (!RaycastGround(ray, 4000f, out float distance)) return false;
            groundY = 2000f - distance;
            return true;
        }

        public static bool RaycastGround(Ray ray, float maxDistance, out float distance)
        {
            distance = 0f;
            Collider terrain = TerrainCollider;
            if (terrain != null)
            {
                if (!terrain.Raycast(ray, out RaycastHit terrainHit, maxDistance)) return false;
                distance = terrainHit.distance;
                return true;
            }
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore)) return false;
            distance = hit.distance;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawRay(transform.position, transform.forward * DistanceRange(size).y);
        }
    }
}

using System.Collections.Generic;
using OtherwiseLabs.CreatureGame;
using OtherwiseLabs.TerrainTools;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    public enum LetterSize
    {
        Bush = 0,      // about a meter tall, a few steps away
        House = 1,     // about 5 m tall
        Building = 2,  // about 14 m tall, far off
    }

    /// <summary>
    /// One letter hidden in the world by forced perspective. Its dots are
    /// ordinary props (mushrooms, rocks, crystals...) scattered at DIFFERENT
    /// distances along the sight rays from one vantage point, each scaled so
    /// it looks the same size from there. From that one spot they line up
    /// into the letter; a few steps away the near dots slide past the far
    /// ones and the letter falls apart into loose scenery.
    ///
    /// This object's transform IS the vantage: position = the eye point the
    /// player must look from, forward = roughly toward the letter. Spawned
    /// letters are placed by LetterLensGame; a hand-placed one (drop this
    /// component on an empty at eye height, facing where the letter should
    /// stand) builds itself on Start.
    ///
    /// Dots whose sight ray reaches the ground before their chosen distance
    /// rest on the ground there; the rest float and turn slowly in place,
    /// which keeps them exactly on their rays.
    /// </summary>
    public class HiddenLetter : MonoBehaviour
    {
        struct SizePreset
        {
            public float height;       // world height of the letter's unit box, at its center distance
            public float distance;     // eye to letter center, along the ground
            public float nearFactor;   // dot depth range, as fractions of the center distance
            public float farFactor;
            public float ringRadius;   // vantage ring on the ground
        }

        static SizePreset Preset(LetterSize size)
        {
            switch (size)
            {
                case LetterSize.Bush:
                    return new SizePreset { height = 1.2f, distance = 5.5f, nearFactor = 0.7f, farFactor = 1.45f, ringRadius = 0.45f };
                case LetterSize.House:
                    return new SizePreset { height = 5f, distance = 17f, nearFactor = 0.6f, farFactor = 1.5f, ringRadius = 0.8f };
                default:
                    return new SizePreset { height = 14f, distance = 42f, nearFactor = 0.5f, farFactor = 1.6f, ringRadius = 1.3f };
            }
        }

        const float DotSpacing = 0.1f;    // between dots, in letter units (letter height = 1)
        const float DotDiameter = 0.12f;  // apparent dot size, in letter units — slight overlap reads as a line

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

        /// <summary>Letter center in the world (on the vantage's center sight line).</summary>
        public Vector3 Center { get; private set; }

        /// <summary>Distance from the vantage to the letter's center.</summary>
        public float CenterDistance { get; private set; }

        readonly List<Transform> _dots = new List<Transform>();
        readonly List<Vector2> _units = new List<Vector2>();
        readonly List<float> _spin = new List<float>();   // deg/s; 0 = grounded, stays still
        Vector3 _fwd, _right, _up;
        Transform _propRoot;
        Transform _ring;
        float _ringRevealDistance;
        float _popUntil;

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
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            flatForward.Normalize();

            // The letter stands on the ground at its center distance.
            Vector3 foot = eye + flatForward * preset.distance;
            float groundY = TryGroundY(foot.x, foot.z, out float y) ? y : eye.y - 1.6f;
            Center = new Vector3(foot.x, groundY + preset.height * 0.55f, foot.z);

            Vector3 toCenter = Center - eye;
            CenterDistance = toCenter.magnitude;
            _fwd = toCenter / CenterDistance;
            _right = Vector3.Cross(Vector3.up, _fwd).normalized;
            _up = Vector3.Cross(_fwd, _right);
            float k = preset.height / CenterDistance; // letter units -> tangent-plane units

            _propRoot = new GameObject($"Letter {letter} props").transform;
            _propRoot.SetParent(transform, true);

            List<GameObject> props = PickProps(rng);

            foreach (Vector2 unit in LetterShapes.SamplePath(letter, DotSpacing))
            {
                Vector3 dir = (_fwd + _right * (unit.x * k) + _up * (unit.y * k)).normalized;
                float planeDistance = CenterDistance / Vector3.Dot(dir, _fwd);
                float depth = planeDistance * Mathf.Lerp(preset.nearFactor, preset.farFactor, (float)rng.NextDouble());
                float diameter = DotDiameter * preset.height * depth / CenterDistance;

                bool grounded = false;
                if (RaycastGround(new Ray(eye, dir), depth, out float hitDistance))
                {
                    depth = SettleOnGround(eye, dir, hitDistance, diameter);
                    diameter = DotDiameter * preset.height * depth / CenterDistance;
                    grounded = true;
                }

                GameObject prefab = props.Count > 0 ? props[rng.Next(props.Count)] : null;
                Transform dot = LensPropFactory.Create(prefab, eye + dir * depth, diameter, (float)rng.NextDouble() * 360f, _propRoot);
                _dots.Add(dot);
                _units.Add(unit);
                _spin.Add(grounded ? 0f : (8f + (float)rng.NextDouble() * 14f) * (rng.Next(2) == 0 ? 1f : -1f));
            }

            if (showVantageRing) BuildRing(eye, preset.ringRadius);
            _ringRevealDistance = Mathf.Max(6f, CenterDistance * 0.6f);
        }

        /// <summary>
        /// A couple of prop kinds per letter, so each letter has a character
        /// (a mushroom W, a crystal A) instead of a random jumble.
        /// </summary>
        List<GameObject> PickProps(System.Random rng)
        {
            var picked = new List<GameObject>();
            LetterLensPalette palette = LetterLensPalette.Load();
            if (palette == null) return picked;

            var pool = new List<GameObject>();
            foreach (GameObject prefab in palette.PropsFor(size))
                if (prefab != null) pool.Add(prefab);
            int kinds = Mathf.Min(pool.Count, 2);
            for (int i = 0; i < kinds; i++)
            {
                int index = rng.Next(pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return picked;
        }

        /// <summary>
        /// Walks back toward the eye from where the sight ray meets the ground
        /// until the dot clears it, so a resting dot is not half buried.
        /// </summary>
        static float SettleOnGround(Vector3 eye, Vector3 dir, float hitDistance, float diameter)
        {
            float clearance = diameter * 0.3f;
            float step = Mathf.Max(0.02f, diameter * 0.2f);
            float depth = hitDistance;
            for (int i = 0; i < 20; i++)
            {
                Vector3 point = eye + dir * depth;
                if (!TryGroundY(point.x, point.z, out float ground) || point.y - ground >= clearance) break;
                depth -= step;
            }
            return Mathf.Max(depth, hitDistance * 0.5f);
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
            if (!IsBuilt) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _dots.Count; i++)
                if (_spin[i] != 0f && _dots[i] != null)
                    _dots[i].Rotate(0f, _spin[i] * dt, 0f, Space.World);

            if (_popUntil > 0f)
            {
                // Found: every dot swells and settles — about its own pivot, so the letter stays whole.
                float remaining = _popUntil - Time.time;
                float t = 1f - Mathf.Clamp01(remaining / 0.8f);
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.45f;
                foreach (Transform dot in _dots)
                    if (dot != null) dot.localScale = Vector3.one * scale;
                if (remaining <= 0f) _popUntil = 0f;
            }

            if (_ring != null)
            {
                Camera camera = Camera.main;
                bool show = !IsFound && camera != null
                    && (camera.transform.position - EyePoint).sqrMagnitude < _ringRevealDistance * _ringRevealDistance;
                if (_ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
            }
        }

        public void MarkFound()
        {
            IsFound = true;
            _popUntil = Time.time + 0.8f;
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
            Gizmos.DrawRay(transform.position, transform.forward * Preset(size).distance);
        }
    }
}

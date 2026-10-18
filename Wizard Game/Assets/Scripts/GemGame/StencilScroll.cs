using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OtherwiseLabs.CreatureGame;

namespace OtherwiseLabs.GemGame
{
    /// <summary>
    /// The letter stencil: a parchment scroll that unrolls flat on the gem
    /// table with one letter inked on it and an empty ring on every VERTEX of
    /// the letter's shape (W has five). The player sets a gem in each ring.
    /// Built entirely from primitives at runtime — parchment, two rollers,
    /// ink lines, rings — and carries no colliders, so taps go straight
    /// through to the table and the gems.
    ///
    /// The ink and the vertices both come from <see cref="LetterShapes"/>, the
    /// same table the tracing game grades against, so the stencil, the
    /// creature tracing and the brewing letters all agree on what a W is.
    /// </summary>
    public class StencilScroll : MonoBehaviour
    {
        static readonly Color Parchment = new Color(0.93f, 0.86f, 0.68f, 1f);
        static readonly Color RollerWood = new Color(0.45f, 0.29f, 0.16f, 1f);
        static readonly Color Ink = new Color(0.24f, 0.16f, 0.1f, 1f);
        static readonly Color RingInk = new Color(0.42f, 0.28f, 0.16f, 1f);

        /// <summary>The letter's vertices in letter-unit space (-0.5..0.5).</summary>
        public IReadOnlyList<Vector2> Vertices => _vertices;

        public char Letter { get; private set; }

        GemStencilController _game;
        Transform _sheet;
        Transform _rollerTop;
        Transform _rollerBottom;
        Transform _inkRoot;
        readonly List<LineRenderer> _inkLines = new List<LineRenderer>();
        readonly List<Transform> _rings = new List<Transform>();
        List<Vector2> _vertices = new List<Vector2>();
        Material _inkMaterial;
        float _width;
        float _length;
        float _glow;
        Color _glowColor = Color.white;
        Coroutine _glowRoutine;

        public void Setup(GemStencilController game)
        {
            _game = game;
            _width = game.Side * 1.2f;
            _length = game.Side * 1.25f;
            transform.position = game.UnitToWorld(Vector2.zero, 0f);
            transform.rotation = Quaternion.LookRotation(game.UpAxis, Vector3.up);

            _sheet = MakePrimitive(PrimitiveType.Cube, "Parchment", Parchment).transform;
            _sheet.SetParent(transform, false);
            _sheet.localPosition = Vector3.up * 0.002f;

            float rollerDiameter = Mathf.Max(game.Side * 0.07f, 0.03f);
            _rollerTop = MakeRoller(rollerDiameter);
            _rollerBottom = MakeRoller(rollerDiameter);

            _inkMaterial = MakeMaterial(Ink);
            _inkRoot = new GameObject("Ink").transform;
            _inkRoot.SetParent(transform, false);

            SetUnrolled(0f);
        }

        Transform MakeRoller(float diameter)
        {
            Transform roller = MakePrimitive(PrimitiveType.Cylinder, "Roller", RollerWood).transform;
            roller.SetParent(transform, false);
            // A cylinder's axis is its local Y: lay it across the sheet's width.
            roller.localRotation = Quaternion.Euler(0f, 0f, 90f);
            roller.localScale = new Vector3(diameter, _width * 0.5f + diameter * 0.6f, diameter);
            return roller;
        }

        // ------------------------------------------------------------------
        // Letter

        /// <summary>Inks a new letter and lays out its empty rings (hidden until unrolled).</summary>
        public void SetLetter(char letter)
        {
            Letter = char.ToUpperInvariant(letter);
            _vertices = VerticesFor(Letter);

            foreach (LineRenderer line in _inkLines) if (line != null) Destroy(line.gameObject);
            _inkLines.Clear();
            foreach (Transform ring in _rings) if (ring != null) Destroy(ring.gameObject);
            _rings.Clear();

            float inkWidth = _game.Side * 0.03f;
            foreach (Vector2[] stroke in LetterShapes.StrokesFor(Letter))
            {
                var lineGo = new GameObject("Stroke");
                lineGo.transform.SetParent(_inkRoot, false);
                var line = lineGo.AddComponent<LineRenderer>();
                line.sharedMaterial = _inkMaterial;
                line.useWorldSpace = true;
                line.widthMultiplier = inkWidth;
                line.numCapVertices = 4;
                line.numCornerVertices = 4;
                line.alignment = LineAlignment.TransformZ;
                lineGo.transform.rotation = Quaternion.LookRotation(Vector3.down, _game.UpAxis);
                line.positionCount = stroke.Length;
                for (int i = 0; i < stroke.Length; i++)
                    line.SetPosition(i, _game.UnitToWorld(stroke[i], 0.006f));
                _inkLines.Add(line);
            }

            float ringDiameter = _game.Side * 0.11f;
            for (int i = 0; i < _vertices.Count; i++)
            {
                Transform ring = MakePrimitive(PrimitiveType.Cylinder, $"Vertex {i + 1}", RingInk).transform;
                ring.SetParent(_inkRoot, true);
                ring.position = _game.UnitToWorld(_vertices[i], 0.008f);
                ring.localScale = new Vector3(ringDiameter, 0.0015f, ringDiameter);
                _rings.Add(ring);
            }

            if (_glowRoutine != null) StopCoroutine(_glowRoutine);
            _glow = 0f;
            _inkMaterial.color = Ink;
            _inkRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The letter's corners and turning points: each stroke's polyline
        /// simplified (so a coarse arc keeps only the points that shape it),
        /// then merged where strokes meet so a shared corner gets one ring.
        /// Straight letters come out exactly as drawn — W gives its 5 points,
        /// A gives 5 (3 for the peak and feet, 2 for the bar's ends).
        /// </summary>
        public static List<Vector2> VerticesFor(char letter)
        {
            float tolerance = 0.06f;
            List<Vector2> result = null;
            for (int attempt = 0; attempt < 5; attempt++, tolerance *= 1.4f)
            {
                result = new List<Vector2>();
                foreach (Vector2[] stroke in LetterShapes.StrokesFor(letter))
                    foreach (Vector2 point in Simplify(stroke, tolerance))
                        AddUnique(result, point, 0.09f);
                if (result.Count <= 10) break;   // more than ten gems stops being fun
            }
            return result;
        }

        static void AddUnique(List<Vector2> points, Vector2 point, float minGap)
        {
            foreach (Vector2 existing in points)
                if ((existing - point).sqrMagnitude < minGap * minGap) return;
            points.Add(point);
        }

        /// <summary>Douglas–Peucker: keeps the points that bend the line by more than the tolerance.</summary>
        static List<Vector2> Simplify(Vector2[] stroke, float tolerance)
        {
            var keep = new bool[stroke.Length];
            keep[0] = keep[stroke.Length - 1] = true;
            SimplifyRange(stroke, 0, stroke.Length - 1, tolerance, keep);
            var result = new List<Vector2>();
            for (int i = 0; i < stroke.Length; i++) if (keep[i]) result.Add(stroke[i]);
            return result;
        }

        static void SimplifyRange(Vector2[] stroke, int first, int last, float tolerance, bool[] keep)
        {
            if (last - first < 2) return;
            float farthest = 0f;
            int index = -1;
            for (int i = first + 1; i < last; i++)
            {
                float distance = DistanceToSegment(stroke[i], stroke[first], stroke[last]);
                if (distance > farthest) { farthest = distance; index = i; }
            }
            if (index < 0 || farthest <= tolerance) return;
            keep[index] = true;
            SimplifyRange(stroke, first, index, tolerance, keep);
            SimplifyRange(stroke, index, last, tolerance, keep);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-6f) return Vector2.Distance(p, a);   // closed loops (O) start where they end
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------
        // Unroll / roll up / glow

        public IEnumerator Unroll(float seconds)
        {
            _inkRoot.gameObject.SetActive(false);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                SetUnrolled(Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            SetUnrolled(1f);
            _inkRoot.gameObject.SetActive(true);
        }

        public IEnumerator RollUp(float seconds)
        {
            _inkRoot.gameObject.SetActive(false);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                SetUnrolled(1f - Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            SetUnrolled(0f);
        }

        /// <summary>0 = rolled shut at the center, 1 = lying fully open. Both rollers travel outward.</summary>
        void SetUnrolled(float amount)
        {
            float open = Mathf.Max(amount * _length, 0.001f);
            _sheet.localScale = new Vector3(_width, 0.003f, open);
            float rollerY = _rollerTop.localScale.x * 0.5f;
            _rollerTop.localPosition = new Vector3(0f, rollerY, open * 0.5f);
            _rollerBottom.localPosition = new Vector3(0f, rollerY, -open * 0.5f);
        }

        /// <summary>Completed letter: the ink brightens to the gems' glow color.</summary>
        public void SetGlow(bool on, Color color)
        {
            _glowColor = Color.Lerp(color, new Color(1f, 0.9f, 0.45f, 1f), 0.5f);
            if (_glowRoutine != null) StopCoroutine(_glowRoutine);
            _glowRoutine = StartCoroutine(GlowRoutine(on ? 1f : 0f));
        }

        IEnumerator GlowRoutine(float target)
        {
            while (!Mathf.Approximately(_glow, target))
            {
                _glow = Mathf.MoveTowards(_glow, target, Time.deltaTime * 2.5f);
                _inkMaterial.color = Color.Lerp(Ink, _glowColor, _glow);
                yield return null;
            }
        }

        // ------------------------------------------------------------------

        static GameObject MakePrimitive(PrimitiveType type, string name, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);
            return go;
        }

        internal static Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            return new Material(shader) { color = color };
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// Traceable stroke paths for every uppercase letter, in a unit box
    /// (x and y in -0.5..0.5, y up). Each letter is a set of strokes; each
    /// stroke a polyline of key points. The tracing panel scales these to its
    /// rect, shows them as a dotted guide, and scores the player's drawing
    /// against the same points — guide and grader can never disagree, which is
    /// what makes the tolerance forgiving without feeling arbitrary.
    ///
    /// Shapes are simple school-style block capitals: straight segments plus
    /// coarse arcs, matching how letter formation is taught rather than any
    /// particular font's flourishes.
    /// </summary>
    public static class LetterShapes
    {
        static Dictionary<char, Vector2[][]> _shapes;

        /// <summary>Strokes for a letter. Unknown characters fall back to O.</summary>
        public static Vector2[][] StrokesFor(char letter)
        {
            EnsureBuilt();
            letter = char.ToUpperInvariant(letter);
            return _shapes.TryGetValue(letter, out Vector2[][] strokes) ? strokes : _shapes['O'];
        }

        /// <summary>
        /// The letter as a dense, evenly-walked point list (all strokes), for
        /// dotted guides and overlap scoring. <paramref name="spacing"/> is the
        /// gap between points in unit-box units.
        /// </summary>
        public static List<Vector2> SamplePath(char letter, float spacing = 0.05f)
        {
            var points = new List<Vector2>();
            foreach (Vector2[] stroke in StrokesFor(letter))
            {
                for (int i = 0; i < stroke.Length - 1; i++)
                {
                    Vector2 a = stroke[i], b = stroke[i + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / spacing));
                    for (int s = 0; s < steps; s++)
                        points.Add(Vector2.Lerp(a, b, s / (float)steps));
                }
                points.Add(stroke[stroke.Length - 1]);
            }
            return points;
        }

        // ------------------------------------------------------------------
        // Shape table
        // ------------------------------------------------------------------

        static void EnsureBuilt()
        {
            if (_shapes != null) return;
            _shapes = new Dictionary<char, Vector2[][]>
            {
                ['A'] = new[] { P(-0.35f, -0.5f, 0f, 0.5f, 0.35f, -0.5f), P(-0.2f, -0.12f, 0.2f, -0.12f) },
                ['B'] = new[]
                {
                    P(-0.3f, -0.5f, -0.3f, 0.5f),
                    Join(P(-0.3f, 0.5f), Arc(-0.3f, 0.27f, 0.42f, 0.23f, 90f, -90f),
                         Arc(-0.3f, -0.23f, 0.48f, 0.27f, 90f, -90f)),
                },
                ['C'] = new[] { Arc(0.05f, 0f, 0.4f, 0.46f, 60f, 300f) },
                ['D'] = new[]
                {
                    P(-0.3f, -0.5f, -0.3f, 0.5f),
                    Join(P(-0.3f, 0.5f), Arc(-0.3f, 0f, 0.62f, 0.5f, 90f, -90f)),
                },
                ['E'] = new[] { P(0.3f, 0.5f, -0.3f, 0.5f, -0.3f, -0.5f, 0.3f, -0.5f), P(-0.3f, 0f, 0.22f, 0f) },
                ['F'] = new[] { P(0.3f, 0.5f, -0.3f, 0.5f, -0.3f, -0.5f), P(-0.3f, 0.02f, 0.2f, 0.02f) },
                ['G'] = new[]
                {
                    Arc(0.02f, 0f, 0.42f, 0.47f, 55f, 300f),
                    P(0.1f, -0.05f, 0.44f, -0.05f, 0.44f, -0.3f),
                },
                ['H'] = new[] { P(-0.3f, 0.5f, -0.3f, -0.5f), P(0.3f, 0.5f, 0.3f, -0.5f), P(-0.3f, 0f, 0.3f, 0f) },
                ['I'] = new[] { P(0f, 0.5f, 0f, -0.5f), P(-0.2f, 0.5f, 0.2f, 0.5f), P(-0.2f, -0.5f, 0.2f, -0.5f) },
                ['J'] = new[] { Join(P(0.25f, 0.5f, 0.25f, -0.2f), Arc(0f, -0.2f, 0.25f, 0.28f, 0f, -180f)) },
                ['K'] = new[] { P(-0.3f, 0.5f, -0.3f, -0.5f), P(0.3f, 0.5f, -0.3f, -0.02f, 0.3f, -0.5f) },
                ['L'] = new[] { P(-0.25f, 0.5f, -0.25f, -0.5f, 0.3f, -0.5f) },
                ['M'] = new[] { P(-0.4f, -0.5f, -0.4f, 0.5f, 0f, -0.05f, 0.4f, 0.5f, 0.4f, -0.5f) },
                ['N'] = new[] { P(-0.3f, -0.5f, -0.3f, 0.5f, 0.3f, -0.5f, 0.3f, 0.5f) },
                ['O'] = new[] { Arc(0f, 0f, 0.4f, 0.48f, 90f, 450f) },
                ['P'] = new[]
                {
                    P(-0.3f, -0.5f, -0.3f, 0.5f),
                    Join(P(-0.3f, 0.5f), Arc(-0.3f, 0.24f, 0.5f, 0.26f, 90f, -90f)),
                },
                ['Q'] = new[] { Arc(0f, 0f, 0.4f, 0.48f, 90f, 450f), P(0.12f, -0.28f, 0.42f, -0.52f) },
                ['R'] = new[]
                {
                    P(-0.3f, -0.5f, -0.3f, 0.5f),
                    Join(P(-0.3f, 0.5f), Arc(-0.3f, 0.24f, 0.5f, 0.26f, 90f, -90f)),
                    P(-0.3f, -0.02f, 0.32f, -0.5f),
                },
                ['S'] = new[]
                {
                    P(0.35f, 0.45f, 0f, 0.48f, -0.35f, 0.36f, -0.35f, 0.12f, 0f, 0f,
                      0.35f, -0.12f, 0.35f, -0.36f, 0f, -0.48f, -0.35f, -0.45f),
                },
                ['T'] = new[] { P(-0.35f, 0.5f, 0.35f, 0.5f), P(0f, 0.5f, 0f, -0.5f) },
                ['U'] = new[] { Join(P(-0.3f, 0.5f, -0.3f, -0.15f), Arc(0f, -0.15f, 0.3f, 0.33f, 180f, 360f), P(0.3f, 0.5f)) },
                ['V'] = new[] { P(-0.35f, 0.5f, 0f, -0.5f, 0.35f, 0.5f) },
                ['W'] = new[] { P(-0.45f, 0.5f, -0.22f, -0.5f, 0f, 0.1f, 0.22f, -0.5f, 0.45f, 0.5f) },
                ['X'] = new[] { P(-0.3f, 0.5f, 0.3f, -0.5f), P(0.3f, 0.5f, -0.3f, -0.5f) },
                ['Y'] = new[] { P(-0.3f, 0.5f, 0f, 0.05f), P(0.3f, 0.5f, 0f, 0.05f, 0f, -0.5f) },
                ['Z'] = new[] { P(-0.3f, 0.5f, 0.3f, 0.5f, -0.3f, -0.5f, 0.3f, -0.5f) },
            };
        }

        /// <summary>Polyline from flat x,y pairs.</summary>
        static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }

        /// <summary>Elliptical arc as a polyline. Degrees may run backwards for clockwise strokes.</summary>
        static Vector2[] Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg, int steps = 14)
        {
            var points = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                points[i] = new Vector2(cx + Mathf.Cos(angle) * rx, cy + Mathf.Sin(angle) * ry);
            }
            return points;
        }

        static Vector2[] Join(params Vector2[][] parts)
        {
            var joined = new List<Vector2>();
            foreach (Vector2[] part in parts)
                foreach (Vector2 point in part)
                    if (joined.Count == 0 || (joined[joined.Count - 1] - point).sqrMagnitude > 1e-6f)
                        joined.Add(point);
            return joined.ToArray();
        }
    }
}

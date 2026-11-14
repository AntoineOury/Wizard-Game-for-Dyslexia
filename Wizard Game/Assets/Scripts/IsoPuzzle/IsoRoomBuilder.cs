using System.Collections.Generic;
using OtherwiseLabs.CreatureGame;
using UnityEngine;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>One generated room: its objects, the hidden letter, and the one view that reveals it.</summary>
    public class IsoRoom
    {
        public Transform Root;
        public char Letter;
        public float TargetYaw;
        public float TargetPitch;
        public Vector3 LetterCenter;   // world point the letter is centred on, seen from the target view
        public float LetterHeight;     // world height of the letter's unit box
        public Color Background;
        public IsoPotion Potion;       // null when this room has none
    }

    /// <summary>Per-room difficulty knobs, picked by the game from how many rooms are cleared.</summary>
    public struct IsoRoomRecipe
    {
        public char Letter;
        public float TargetYaw;
        public float TargetPitch;
        public int PropCount;
        public bool WithPotion;
    }

    /// <summary>
    /// Builds a floating diorama room out of ProBuilder shapes, with a letter
    /// hidden in it Viewfinder-style.
    ///
    /// The trick: the camera is orthographic, so sliding an object straight
    /// along the view direction does not move it on screen at all. The letter
    /// is laid out flat (as stroke pieces from LetterShapes, the same paths
    /// the tracer grades against), then every piece is pushed a random
    /// distance toward or away from the camera, staying inside the room. From
    /// the target angle the pieces line up into the letter; from anywhere
    /// else they are a scattered sculpture of blocks, rods and wedges.
    ///
    /// Decoy props are only placed where they do NOT cover the letter's
    /// outline from the target view, so the solution is always readable.
    /// </summary>
    public static class IsoRoomBuilder
    {
        // Room volume. The platform's top face is y = 0.
        const float PlatformHalf = 4.2f;
        const float VolumeHalf = 3.5f;   // letter pieces and props stay inside |x|,|z| <= this
        const float VolumeBottom = 0.45f;
        const float VolumeTop = 6.2f;

        const float LetterHeightWorld = 3.6f;
        const float StrokeThickness = 0.3f;
        const float PieceLength = 0.5f;   // world length a stroke is cut into

        struct Palette
        {
            public Color Background, Top, Side, Under;
            public Color[] Props;
            public Palette(string bg, string top, string side, string under, params string[] props)
            {
                Background = Hex(bg); Top = Hex(top); Side = Hex(side); Under = Hex(under);
                Props = new Color[props.Length];
                for (int i = 0; i < props.Length; i++) Props[i] = Hex(props[i]);
            }
        }

        static readonly Palette[] Palettes =
        {
            new Palette("#F6D9C4", "#FFF4E6", "#E8B79A", "#C98B72", "#E86F5C", "#F2A65A", "#5B8E99"),
            new Palette("#CFE3E8", "#F4F9F9", "#9CC3CC", "#6E9AA6", "#3E6D8C", "#E9A23B", "#D96C75"),
            new Palette("#E4DAF2", "#FBF8FF", "#BCA9DB", "#8E79B6", "#6A4C93", "#F28482", "#84A59D"),
            new Palette("#DDEBD3", "#F8FCF3", "#AFCB9A", "#7FA36A", "#3D7A55", "#E07A5F", "#F2CC8F"),
            new Palette("#F3E3B5", "#FFFBEF", "#E3C27A", "#B8924A", "#C1440E", "#2A6F97", "#6B9080"),
        };

        public static IsoRoom Build(IsoRoomRecipe recipe, System.Random rng, Transform parent)
        {
            Palette palette = Palettes[rng.Next(Palettes.Length)];
            var root = new GameObject("Iso Room " + recipe.Letter).transform;
            root.SetParent(parent, false);

            var room = new IsoRoom
            {
                Root = root,
                Letter = char.ToUpperInvariant(recipe.Letter),
                TargetYaw = recipe.TargetYaw,
                TargetPitch = recipe.TargetPitch,
                LetterCenter = new Vector3(0f, 3.1f, 0f),
                LetterHeight = LetterHeightWorld,
                Background = palette.Background,
            };

            BuildPlatform(root, palette, rng);

            Quaternion view = Quaternion.Euler(recipe.TargetPitch, recipe.TargetYaw, 0f);
            Vector3 right = view * Vector3.right;
            Vector3 up = view * Vector3.up;
            Vector3 forward = view * Vector3.forward;

            Rect letterRect = BuildLetter(room, root, palette, rng, right, up, forward);
            BuildProps(recipe.PropCount, root, palette, rng, room.LetterCenter, right, up, letterRect);

            if (recipe.WithPotion)
                room.Potion = IsoPotion.Create(root, PotionSpot(rng, room.LetterCenter, right, up, letterRect));

            return room;
        }

        // ------------------------------------------------------------------
        // Platform
        // ------------------------------------------------------------------

        static void BuildPlatform(Transform root, Palette palette, System.Random rng)
        {
            float size = PlatformHalf * 2f;
            var top = IsoShapes.Cube(new Vector3(size, 0.5f, size), IsoShapes.Lit(palette.Top), root);
            top.transform.localPosition = new Vector3(0f, -0.25f, 0f);

            var middle = IsoShapes.Cube(new Vector3(size - 0.3f, 1.1f, size - 0.3f), IsoShapes.Lit(palette.Side), root);
            middle.transform.localPosition = new Vector3(0f, -1.05f, 0f);

            var under = IsoShapes.Cube(new Vector3(size - 1.6f, 1.4f, size - 1.6f), IsoShapes.Lit(palette.Under), root);
            under.transform.localPosition = new Vector3(0f, -2.3f, 0f);

            // A few flat tiles inset into the top, for a sense of scale and
            // something to read rotation from.
            Material tile = IsoShapes.Lit(Color.Lerp(palette.Top, palette.Side, 0.35f));
            for (int i = 0; i < 6; i++)
            {
                float x = Mathf.Lerp(-PlatformHalf + 0.8f, PlatformHalf - 0.8f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-PlatformHalf + 0.8f, PlatformHalf - 0.8f, (float)rng.NextDouble());
                var t = IsoShapes.Cube(new Vector3(1f, 0.04f, 1f), tile, root);
                t.transform.localPosition = new Vector3(Mathf.Round(x), 0.02f, Mathf.Round(z));
            }
        }

        // ------------------------------------------------------------------
        // The hidden letter
        // ------------------------------------------------------------------

        /// <returns>The letter's outline on the view plane, in world units around LetterCenter.</returns>
        static Rect BuildLetter(IsoRoom room, Transform root, Palette palette, System.Random rng,
                                Vector3 right, Vector3 up, Vector3 forward)
        {
            float h = room.LetterHeight;
            Vector2[][] strokes = LetterShapes.StrokesFor(room.Letter);
            Material[] letterMaterials =
            {
                IsoShapes.Lit(palette.Props[rng.Next(palette.Props.Length)]),
                IsoShapes.Lit(palette.Props[rng.Next(palette.Props.Length)]),
            };

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            int pieceIndex = 0;

            foreach (Vector2[] stroke in strokes)
            {
                for (int i = 0; i < stroke.Length; i++)
                {
                    min = Vector2.Min(min, stroke[i]);
                    max = Vector2.Max(max, stroke[i]);
                }

                for (int i = 0; i < stroke.Length - 1; i++)
                {
                    Vector2 a = stroke[i] * h, b = stroke[i + 1] * h;
                    float segment = Vector2.Distance(a, b);
                    if (segment < 1e-4f) continue;
                    int count = Mathf.Max(1, Mathf.CeilToInt(segment / PieceLength));
                    Vector2 dir2 = (b - a) / segment;
                    Vector3 along = (right * dir2.x + up * dir2.y).normalized;

                    for (int p = 0; p < count; p++)
                    {
                        Vector2 from = Vector2.Lerp(a, b, p / (float)count);
                        Vector2 to = Vector2.Lerp(a, b, (p + 1) / (float)count);
                        Vector3 onPlane = room.LetterCenter + right * ((from.x + to.x) * 0.5f) + up * ((from.y + to.y) * 0.5f);
                        Vector3 center = PushAlongView(onPlane, forward, rng, 0.35f);

                        var kind = (IsoShapes.BarKind)rng.Next(3);
                        // A little overlap so neighbouring pieces read as one stroke.
                        float length = segment / count + StrokeThickness * 0.55f;
                        var go = IsoShapes.Bar(kind, center, along, forward, length, StrokeThickness,
                                               letterMaterials[pieceIndex++ % 2], root);
                        go.name = $"Letter {room.Letter} piece";
                    }
                }

                // Round joints where the stroke turns sharply, so corners
                // (the point of an A, the elbow of an L) do not show notches.
                for (int i = 1; i < stroke.Length - 1; i++)
                {
                    Vector2 inDir = (stroke[i] - stroke[i - 1]).normalized;
                    Vector2 outDir = (stroke[i + 1] - stroke[i]).normalized;
                    if (Vector2.Dot(inDir, outDir) > 0.85f) continue;
                    Vector3 onPlane = room.LetterCenter + right * (stroke[i].x * h) + up * (stroke[i].y * h);
                    var joint = IsoShapes.Ball(StrokeThickness * 0.62f, letterMaterials[pieceIndex++ % 2], root);
                    joint.transform.position = PushAlongView(onPlane, forward, rng, 0.35f);
                    joint.name = $"Letter {room.Letter} joint";
                }
            }

            return Rect.MinMaxRect(min.x * h, min.y * h, max.x * h, max.y * h);
        }

        /// <summary>
        /// Slides a point a random distance along the view direction, staying
        /// inside the room volume (shrunk by <paramref name="margin"/>). The
        /// point's position on screen from the target view does not change.
        /// </summary>
        static Vector3 PushAlongView(Vector3 point, Vector3 forward, System.Random rng, float margin)
        {
            Vector3 lo = new Vector3(-VolumeHalf + margin, VolumeBottom + margin, -VolumeHalf + margin);
            Vector3 hi = new Vector3(VolumeHalf - margin, VolumeTop - margin, VolumeHalf - margin);
            float tMin = -6f, tMax = 6f;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = point[axis], d = forward[axis];
                if (Mathf.Abs(d) < 1e-5f)
                {
                    if (o < lo[axis] || o > hi[axis]) return point; // cannot fix by sliding; keep it on the plane
                    continue;
                }
                float t1 = (lo[axis] - o) / d, t2 = (hi[axis] - o) / d;
                tMin = Mathf.Max(tMin, Mathf.Min(t1, t2));
                tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
            }
            if (tMin > tMax) return point;
            float t = Mathf.Lerp(tMin, tMax, (float)rng.NextDouble());
            return point + forward * t;
        }

        // ------------------------------------------------------------------
        // Decoy props
        // ------------------------------------------------------------------

        static void BuildProps(int count, Transform root, Palette palette, System.Random rng,
                               Vector3 letterCenter, Vector3 right, Vector3 up, Rect letterRect)
        {
            int placed = 0;
            for (int attempt = 0; attempt < count * 12 && placed < count; attempt++)
            {
                Material material = IsoShapes.Lit(palette.Props[rng.Next(palette.Props.Length)]);
                bool grounded = rng.NextDouble() < 0.45;
                GameObject prop = MakeProp(rng, material, root, grounded, out float radius, out float halfHeight);

                float x = Mathf.Lerp(-VolumeHalf, VolumeHalf, (float)rng.NextDouble());
                float z = Mathf.Lerp(-VolumeHalf, VolumeHalf, (float)rng.NextDouble());
                float y = grounded ? halfHeight : Mathf.Lerp(VolumeBottom + radius, VolumeTop - radius, (float)rng.NextDouble());
                Vector3 position = new Vector3(x, y, z);

                // Seen from the target view, the prop must stay clear of the
                // letter's outline, or it could hide or garble the answer.
                Vector3 offset = position - letterCenter;
                Vector2 onScreen = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up));
                float reach = radius + 0.45f;
                if (onScreen.x > letterRect.xMin - reach && onScreen.x < letterRect.xMax + reach &&
                    onScreen.y > letterRect.yMin - reach && onScreen.y < letterRect.yMax + reach)
                {
                    prop.SetActive(false);
                    Object.Destroy(prop);
                    continue;
                }

                prop.transform.localPosition = position;
                placed++;
            }
        }

        static GameObject MakeProp(System.Random rng, Material material, Transform root, bool grounded,
                                   out float radius, out float halfHeight)
        {
            float yaw = rng.Next(4) * 90f + (rng.NextDouble() < 0.3 ? 45f : 0f);
            GameObject go;
            if (grounded)
            {
                switch (rng.Next(4))
                {
                    case 0:
                    {
                        float h = Mathf.Lerp(1.2f, 3.2f, (float)rng.NextDouble());
                        go = IsoShapes.Cylinder(0.32f, h, material, root);
                        radius = Mathf.Max(0.32f, h * 0.5f); halfHeight = h * 0.5f;
                        break;
                    }
                    case 1:
                        go = IsoShapes.Stair(new Vector3(1.2f, 1.2f, 1.6f), 5, material, root);
                        radius = 1.1f; halfHeight = 0.6f;
                        break;
                    case 2:
                        go = IsoShapes.Arch(1f, 0.35f, 0.5f, material, root);
                        radius = 1.1f; halfHeight = 0.5f;
                        break;
                    default:
                    {
                        float s = Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble());
                        go = IsoShapes.Cube(new Vector3(s, s, s), material, root);
                        radius = s * 0.8f; halfHeight = s * 0.5f;
                        break;
                    }
                }
            }
            else
            {
                switch (rng.Next(4))
                {
                    case 0:
                        go = IsoShapes.Ball(Mathf.Lerp(0.25f, 0.55f, (float)rng.NextDouble()), material, root);
                        radius = 0.55f; halfHeight = 0.55f;
                        break;
                    case 1:
                        go = IsoShapes.Torus(0.12f, 0.45f, material, root);
                        radius = 0.6f; halfHeight = 0.6f;
                        go.transform.localRotation = Quaternion.Euler(rng.Next(2) * 90f, 0f, 0f);
                        break;
                    case 2:
                        go = IsoShapes.Cone(0.4f, 0.9f, material, root);
                        radius = 0.6f; halfHeight = 0.45f;
                        break;
                    default:
                    {
                        float s = Mathf.Lerp(0.35f, 0.8f, (float)rng.NextDouble());
                        go = IsoShapes.Cube(new Vector3(s, s, s), material, root);
                        radius = s * 0.8f; halfHeight = s * 0.5f;
                        break;
                    }
                }
            }
            go.name = "Prop " + go.name;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.localRotation;
            return go;
        }

        static Vector3 PotionSpot(System.Random rng, Vector3 letterCenter, Vector3 right, Vector3 up, Rect letterRect)
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Mathf.Lerp(-VolumeHalf, VolumeHalf, (float)rng.NextDouble()), 0f,
                                    Mathf.Lerp(-VolumeHalf, VolumeHalf, (float)rng.NextDouble()));
                Vector3 offset = p + Vector3.up * 0.5f - letterCenter;
                Vector2 s = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up));
                if (!letterRect.Contains(s)) return p;
            }
            return new Vector3(VolumeHalf, 0f, VolumeHalf);
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace OtherwiseLabs.IsoPuzzle
{
    /// <summary>
    /// Every solid in the Iso Puzzle is a ProBuilder shape, generated at run
    /// time with ProBuilder's ShapeGenerator (cubes, cylinders, prisms, cones,
    /// stairs, arches, tori and icosphere "balls"). This class wraps those
    /// calls so the room builder can ask for "a bar from here to there" or
    /// "a pillar" without caring about each generator's argument list, and
    /// keeps one shared URP material per colour.
    ///
    /// All shapes come back with their pivot at the centre of their bounds.
    /// </summary>
    public static class IsoShapes
    {
        static readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        static Shader _litShader;
        static Shader _unlitShader;

        public static Material Lit(Color color)
        {
            if (_materials.TryGetValue(color, out Material cached) && cached != null) return cached;
            if (_litShader == null)
                _litShader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                             ?? Shader.Find("Standard");
            var material = new Material(_litShader) { color = color, name = "Iso " + ColorUtility.ToHtmlStringRGB(color) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
            _materials[color] = material;
            return material;
        }

        /// <summary>Unlit, for things that should glow regardless of light (potions).</summary>
        public static Material Glow(Color color)
        {
            if (_unlitShader == null)
                _unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(_unlitShader) { color = color, name = "Iso Glow" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        // ------------------------------------------------------------------
        // Raw ProBuilder shapes
        // ------------------------------------------------------------------

        public static GameObject Cube(Vector3 size, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateCube(PivotLocation.Center, size), material, parent, "Cube");

        /// <summary>Cylinder standing along local Y.</summary>
        public static GameObject Cylinder(float radius, float height, Material material, Transform parent, int sides = 16)
            => Finish(ShapeGenerator.GenerateCylinder(PivotLocation.Center, sides, radius, height, 0, 1), material, parent, "Cylinder");

        /// <summary>Triangular prism: triangle in local XY, extruded along local Z.</summary>
        public static GameObject Prism(Vector3 size, Material material, Transform parent)
            => Finish(ShapeGenerator.GeneratePrism(PivotLocation.Center, size), material, parent, "Prism");

        public static GameObject Cone(float radius, float height, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateCone(PivotLocation.Center, radius, height, 12), material, parent, "Cone");

        public static GameObject Ball(float radius, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, radius, 1), material, parent, "Ball");

        public static GameObject Stair(Vector3 size, int steps, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateStair(PivotLocation.Center, size, steps, true), material, parent, "Stair");

        public static GameObject Arch(float radius, float width, float depth, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateArch(PivotLocation.Center, 180f, radius, width, depth, 10, true, true, true, true, true),
                      material, parent, "Arch");

        public static GameObject Torus(float innerRadius, float outerRadius, Material material, Transform parent)
            => Finish(ShapeGenerator.GenerateTorus(PivotLocation.Center, 12, 16, innerRadius, outerRadius, true, 360f, 360f),
                      material, parent, "Torus");

        static GameObject Finish(ProBuilderMesh mesh, Material material, Transform parent, string name)
        {
            GameObject go = mesh.gameObject;
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return go;
        }

        // ------------------------------------------------------------------
        // Letter pieces
        // ------------------------------------------------------------------

        public enum BarKind { Block, Rod, Wedge }

        /// <summary>
        /// A bar whose long axis runs along <paramref name="along"/> and whose
        /// cross-section faces <paramref name="viewDir"/>. Seen down that view
        /// direction (orthographic), every kind projects to the same
        /// <paramref name="thickness"/>-wide stroke, whatever depth it sits at.
        /// <paramref name="along"/> must be perpendicular to the view.
        /// </summary>
        public static GameObject Bar(BarKind kind, Vector3 center, Vector3 along, Vector3 viewDir,
                                     float length, float thickness, Material material, Transform parent)
        {
            GameObject go;
            Quaternion rotation;
            switch (kind)
            {
                case BarKind.Rod:
                    // Cylinder length runs along local Y.
                    go = Cylinder(thickness * 0.5f, length, material, parent, 12);
                    rotation = Quaternion.LookRotation(viewDir, along);
                    break;
                case BarKind.Wedge:
                    // Prism length runs along local Z; its triangle (local XY)
                    // is seen edge-on as a thickness-wide band.
                    go = Prism(new Vector3(thickness, thickness * 0.5f, length), material, parent);
                    rotation = Quaternion.LookRotation(along, viewDir);
                    break;
                default:
                    // Cube length along local X, depth along local Z = view.
                    go = Cube(new Vector3(length, thickness, thickness), material, parent);
                    rotation = Quaternion.LookRotation(viewDir, Vector3.Cross(viewDir, along));
                    break;
            }
            go.transform.position = center;
            go.transform.rotation = rotation;
            return go;
        }
    }
}

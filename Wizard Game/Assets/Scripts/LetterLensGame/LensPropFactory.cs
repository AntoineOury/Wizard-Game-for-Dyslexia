using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// Turns any prefab into one "dot" of a hidden letter: an object standing
    /// upright on the ground, scaled so that its TOP (a mushroom cap, a rock's
    /// crown, a tree's canopy) sits on the dot's sight ray. The returned pivot
    /// is that exact point on the ray — what the alignment math reads — while
    /// the body below it rests on the terrain like any other scenery.
    ///
    /// Small props lose their colliders so the player never trips on a
    /// pebble; tall ones (trees, boulders) keep them, like the scene's own
    /// trees. Built-in pipeline "Standard" materials (several third-party
    /// packs ship them) would render pink in URP; they are converted to URP
    /// Lit once and shared from a cache.
    /// </summary>
    public static class LensPropFactory
    {
        /// <summary>A prefab's shape at scale 1, measured once from its render bounds.</summary>
        public struct Metrics
        {
            public float height;       // base to top
            public float headWidth;    // widest horizontal extent: the dot's size
            public float headCenter;   // base to the middle of the top blob

            public bool Valid => height > 1e-4f && headWidth > 1e-4f;
        }

        const float KeepCollidersAbove = 2.5f; // meters

        static readonly Dictionary<Material, Material> _converted = new Dictionary<Material, Material>();
        static readonly Dictionary<GameObject, Metrics> _metrics = new Dictionary<GameObject, Metrics>();
        static Material _fallbackStone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _converted.Clear();
            _metrics.Clear();
            _fallbackStone = null;
        }

        /// <summary>
        /// Height, width and where the "head" sits. The head is the top blob,
        /// as wide as the object: a squat rock's head is the whole rock
        /// (center at half height), a tree's head is its canopy.
        /// </summary>
        public static Metrics Measure(GameObject prefab)
        {
            if (prefab != null && _metrics.TryGetValue(prefab, out Metrics cached)) return cached;

            GameObject body = prefab != null ? Object.Instantiate(prefab) : CreateFallbackStone();
            body.transform.SetPositionAndRotation(new Vector3(0f, -10000f, 0f), Quaternion.identity);
            var metrics = new Metrics();
            if (TryGetBounds(body.GetComponentsInChildren<Renderer>(), out Bounds bounds))
            {
                metrics.height = bounds.size.y;
                metrics.headWidth = Mathf.Max(bounds.size.x, bounds.size.z);
                metrics.headCenter = metrics.height - Mathf.Min(metrics.headWidth, metrics.height) * 0.5f;
            }
            body.SetActive(false);
            Object.Destroy(body);

            if (prefab != null) _metrics[prefab] = metrics;
            return metrics;
        }

        /// <summary>
        /// Stands <paramref name="prefab"/> on the ground below
        /// <paramref name="headPoint"/>, scaled by <paramref name="scale"/>
        /// (chosen by the caller so the head lands on the point).
        /// </summary>
        public static Transform CreateStanding(GameObject prefab, Vector3 headPoint, float groundY, float scale, float yaw, Transform parent)
        {
            var pivot = new GameObject("Lens Prop").transform;
            pivot.SetParent(parent, false);
            pivot.position = headPoint;

            GameObject body = prefab != null ? Object.Instantiate(prefab) : CreateFallbackStone();
            body.name = prefab != null ? prefab.name : "Stone";
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            body.transform.localScale *= scale;

            Renderer[] renderers = body.GetComponentsInChildren<Renderer>();
            ConvertMaterials(renderers);
            if (!TryGetBounds(renderers, out Bounds bounds))
            {
                Object.Destroy(body);
                Object.Destroy(pivot.gameObject);
                return null;
            }
            if (bounds.size.y < KeepCollidersAbove) StripColliders(body);

            // Bottom-center of the bounds onto the ground, a hair sunk so
            // uneven bases don't show daylight. Trunks stay vertical.
            Vector3 bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float sink = bounds.size.y * 0.03f;
            Vector3 target = new Vector3(headPoint.x, groundY - sink, headPoint.z);
            body.transform.position += target - bottom;
            body.transform.SetParent(pivot, true);
            return pivot;
        }

        static void StripColliders(GameObject body)
        {
            foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
            {
                // Disabled now (raycasts later this frame must not hit it),
                // destroyed at the end of the frame.
                collider.enabled = false;
                Object.Destroy(collider);
            }
            foreach (Rigidbody rigidbody in body.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(rigidbody);
        }

        static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any && bounds.size.sqrMagnitude > 1e-8f;
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        static void ConvertMaterials(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material converted = Convert(materials[i]);
                    if (converted == materials[i]) continue;
                    materials[i] = converted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        static bool NeedsConversion(Material material)
        {
            if (material == null || material.shader == null) return material != null;
            string name = material.shader.name;
            return name == "Standard" || name == "Standard (Specular setup)" || name.StartsWith("Legacy Shaders/")
                || name == "Hidden/InternalErrorShader" || !material.shader.isSupported;
        }

        static Material Convert(Material source)
        {
            if (!NeedsConversion(source)) return source;
            if (_converted.TryGetValue(source, out Material cached)) return cached;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return source;

            var material = new Material(lit) { name = source.name + " (URP)" };
            if (source.HasProperty("_MainTex") && source.mainTexture != null) material.SetTexture("_BaseMap", source.mainTexture);
            if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.color);
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                material.EnableKeyword("_NORMALMAP");
            }
            bool cutout = source.IsKeywordEnabled("_ALPHATEST_ON")
                || (source.HasProperty("_Mode") && Mathf.Approximately(source.GetFloat("_Mode"), 1f));
            if (cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                if (source.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            _converted[source] = material;
            return material;
        }

        static GameObject CreateFallbackStone()
        {
            GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = stone.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            stone.transform.localScale = new Vector3(1f, 0.8f, 1f);

            if (_fallbackStone == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                _fallbackStone = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "Lens Stone" };
                _fallbackStone.color = new Color(0.55f, 0.53f, 0.5f);
                if (_fallbackStone.HasProperty("_BaseColor")) _fallbackStone.SetColor("_BaseColor", new Color(0.55f, 0.53f, 0.5f));
            }
            stone.GetComponent<MeshRenderer>().sharedMaterial = _fallbackStone;
            return stone;
        }
    }
}

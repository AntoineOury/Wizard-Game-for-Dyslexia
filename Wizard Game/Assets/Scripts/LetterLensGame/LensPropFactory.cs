using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.LetterLens
{
    /// <summary>
    /// Turns any prefab into one "dot" of a hidden letter: instantiated under
    /// a pivot that sits exactly on the dot's sight ray, scaled so its widest
    /// side is the requested diameter, and centered on that pivot by its real
    /// render bounds (prefab origins are usually at the feet, which would push
    /// the dot off the letter). The pivot is what the alignment math reads,
    /// and what a floating dot spins around, so spinning never drifts it.
    ///
    /// Props are scenery, not obstacles: colliders are removed, so the
    /// creature game's ground raycasts and the player both ignore them.
    /// Built-in pipeline "Standard" materials (several third-party packs ship
    /// them) would render pink in URP; they are converted to URP Lit once and
    /// shared from a cache.
    /// </summary>
    public static class LensPropFactory
    {
        static readonly Dictionary<Material, Material> _converted = new Dictionary<Material, Material>();
        static Material _fallbackStone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _converted.Clear();
            _fallbackStone = null;
        }

        public static Transform Create(GameObject prefab, Vector3 center, float diameter, float yaw, Transform parent)
        {
            var pivot = new GameObject("Lens Prop").transform;
            pivot.SetParent(parent, false);
            pivot.position = center;

            GameObject body = prefab != null ? Object.Instantiate(prefab) : CreateFallbackStone();
            body.name = prefab != null ? prefab.name : "Stone";
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));

            StripColliders(body);
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>();
            ConvertMaterials(renderers);

            if (!TryGetBounds(renderers, out Bounds bounds))
            {
                Object.Destroy(body);
                body = CreateFallbackStone();
                renderers = body.GetComponentsInChildren<Renderer>();
                TryGetBounds(renderers, out bounds);
            }

            float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float factor = extent > 1e-4f ? diameter / extent : 1f;
            Vector3 offset = bounds.center - body.transform.position;
            body.transform.localScale *= factor;
            body.transform.position = center - offset * factor;
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

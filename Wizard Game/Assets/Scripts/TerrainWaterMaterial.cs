using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Resolves the material for water surfaces. Both terrain systems render
    /// water with the same "OtherwiseLabs/Terrain Water" shader; this is the one
    /// place that knows how to find it and build a fallback material from it, so
    /// neither system carries its own copy of that logic.
    /// </summary>
    public static class TerrainWaterMaterial
    {
        public const string ShaderName = "OtherwiseLabs/Terrain Water";

        /// <summary>
        /// The material to use for a water surface: the author's assigned one when
        /// present, otherwise a cached auto-created material from the shared water
        /// shader. Returns null (and warns once per resolve) if the shader is
        /// missing from the project — the caller should then skip building water.
        /// </summary>
        public static Material Resolve(Material assigned, ref Material autoCache)
        {
            if (assigned != null) return assigned;
            if (autoCache == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"Shader '{ShaderName}' not found — water surfaces will be skipped. Add TerrainWater.shader to the project or assign a Water Material.");
                    return null;
                }
                autoCache = new Material(shader) { name = "Terrain Water (auto)" };
            }
            return autoCache;
        }
    }
}

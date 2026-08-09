// GPU-instanced grass blades. Vertex color carries the blade gradient
// (R: 0 root, 1 tip) and the bend weight (A), so tips sway and roots stay
// planted. Untagged pass renders under URP as SRPDefaultUnlit; instancing
// macros make DrawMeshInstanced batches work.
Shader "OtherwiseLabs/Terrain Grass"
{
    Properties
    {
        _BaseColor ("Root Color", Color) = (0.16, 0.34, 0.14, 1)
        _TipColor ("Tip Color", Color) = (0.45, 0.65, 0.25, 1)
        _SwayAmount ("Sway Amount", Range(0, 0.5)) = 0.12
        _SwaySpeed ("Sway Speed", Range(0, 6)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            fixed4 _TipColor;
            float _SwayAmount;
            float _SwaySpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float bend = v.color.a;
                // Phase from world position so neighbouring blades don't march
                // in lockstep like a parade.
                float phase = worldPos.x * 0.45 + worldPos.z * 0.38;
                worldPos.x += sin(_Time.y * _SwaySpeed + phase) * _SwayAmount * bend;
                worldPos.z += cos(_Time.y * _SwaySpeed * 0.83 + phase * 1.27) * _SwayAmount * 0.6 * bend;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.color = lerp(_BaseColor, _TipColor, v.color.r);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.color.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

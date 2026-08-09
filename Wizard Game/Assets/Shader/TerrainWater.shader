// Translucent scrolling water for the streamed terrain's lakes. Untagged pass:
// URP draws it as SRPDefaultUnlit — the same trick as the terrain vertex color
// shader, so one file works without keyword variants.
Shader "OtherwiseLabs/Terrain Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.35, 0.65, 0.75, 0.75)
        _DeepColor ("Deep Color", Color) = (0.10, 0.30, 0.50, 0.85)
        _WaveHeight ("Wave Height", Range(0, 0.5)) = 0.08
        _WaveScale ("Wave Scale", Range(0.01, 1)) = 0.15
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 1.0
        _RippleScale ("Ripple Scale", Range(0.05, 3)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            float _WaveHeight;
            float _WaveScale;
            float _WaveSpeed;
            float _RippleScale;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // Gentle bob so the surface reads as liquid even from afar.
                worldPos.y += sin(_Time.y * _WaveSpeed + worldPos.x * _WaveScale)
                            * cos(_Time.y * _WaveSpeed * 0.83 + worldPos.z * _WaveScale * 1.13)
                            * _WaveHeight;
                o.worldPos = worldPos;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Two drifting interference bands stand in for ripples without
                // any texture: cheap, tileless, seamless across chunks.
                float a = sin(i.worldPos.x * _RippleScale + _Time.y * 0.7)
                        * sin(i.worldPos.z * _RippleScale * 1.21 - _Time.y * 0.55);
                float b = sin((i.worldPos.x + i.worldPos.z) * _RippleScale * 0.53 + _Time.y * 0.9);
                float shimmer = saturate(0.5 + 0.35 * a + 0.15 * b);
                fixed4 color = lerp(_DeepColor, _ShallowColor, shimmer);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}

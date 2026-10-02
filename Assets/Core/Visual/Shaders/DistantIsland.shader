Shader "AKI/Distant Island"
{
    Properties
    {
        _BaseMap ("Island picture", 2D) = "white" {}
        _Tint ("Muted island tint", Color) = (0.7, 0.8, 0.9, 1)
        _HazeColor ("Horizon haze", Color) = (0.51, 0.65, 0.73, 1)
        _Haze ("Horizon haze amount", Range(0, 1)) = 0.7
        _WhiteCutoff ("White background removal", Range(0.7, 1)) = 0.96
        _ChromaCutoff ("Neutral background removal", Range(0, 0.1)) = 0.02
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint, _HazeColor;
                half _Haze, _WhiteCutoff, _ChromaCutoff, _Cutoff;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half hi = max(tex.r, max(tex.g, tex.b));
                half lo = min(tex.r, min(tex.g, tex.b));
                // The supplied pictures have white margins and a neutral-grey backing rectangle.
                half mask = tex.a * step(lo, _WhiteCutoff) * step(_ChromaCutoff, hi - lo);
                clip(mask - _Cutoff);
                return half4(lerp(tex.rgb * _Tint.rgb, _HazeColor.rgb, _Haze), 1);
            }
            ENDHLSL
        }
    }
}

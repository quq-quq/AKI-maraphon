Shader "AKI/UnderwaterParticle"
{
    // Unlit particle for effects under water (bubbles, blood). The underwater haze is drawn before transparents,
    // so particles fade and lose their red with distance on their own, roughly matching it. They also soften where
    // they touch geometry and disappear at the water surface.
    // Blend is premultiplied: _Additive 0 = normal alpha blend (blood), 1 = pure glow (fizz), in between = bubbles.
    Properties
    {
        _BaseMap        ("Texture", 2D) = "white" {}
        [HDR] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Additive       ("Additive (0 = alpha blend, 1 = glow)", Range(0, 1)) = 0
        _FadeDensity    ("Fade With Distance (1/m)", Range(0, 1)) = 0.15
        _Absorption     ("Colour Loss RGB (1/m)", Vector) = (0.25, 0.07, 0.03, 0)
        [Toggle(_SOFT_PARTICLES)] _Soft ("Soft Particles (needs Depth Texture)", Float) = 0
        _SoftDistance   ("Soft Distance (m)", Range(0.01, 2)) = 0.25
        _SurfaceFade    ("Fade Below Surface (m)", Range(0.01, 1)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ _SOFT_PARTICLES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Additive;
                float  _FadeDensity;
                float4 _Absorption;
                float  _SoftDistance;
                float  _SurfaceFade;
            CBUFFER_END

            // set by WaterSurface: cell > 0 means there is water in the scene, centre.y is its mean level
            float  _WaterMeshCell;
            float4 _WaterMeshCenter;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;

                // water between the particle and the eye: red goes first, then everything sinks into the haze
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                c.rgb *= exp(-_Absorption.rgb * dist);
                float a = c.a * exp(-_FadeDensity * dist);

            #if defined(_SOFT_PARTICLES)
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float selfZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                a *= saturate((sceneZ - selfZ) / _SoftDistance);
            #endif

                // bubbles pop when they reach the surface
                if (_WaterMeshCell > 0.0)
                    a *= saturate((_WaterMeshCenter.y - i.positionWS.y) / _SurfaceFade);

                return half4(c.rgb * a, a * (1.0h - _Additive));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

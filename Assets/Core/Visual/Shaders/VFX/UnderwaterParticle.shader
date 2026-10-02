Shader "AKI/UnderwaterParticle"
{
    // Unlit particle for effects under water (bubbles, blood, the harpoon rope). The underwater haze is drawn before
    // transparents, so particles fade and lose their red with distance on their own, roughly matching it. They also
    // soften where they touch geometry and disappear at the real (FFT) wave surface, not just the mean water level.
    // Drawn right after the water surface: from below, the surface would otherwise be sorted over them at random.
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
        [ToggleUI] _ClipAboveWater ("Hide Above Water", Float) = 1
        // WaterCurrentView supplies glow, vertical excursion and oscillation phase. Zero for other particles.
        [HideInInspector] _CurrentRhythm ("Current Rhythm", Vector) = (0, 0, 0, 0)
        [HideInInspector][HDR] _CurrentGlowColour ("Current Glow Colour", Color) = (.45, .85, 1, 1)
        [HideInInspector] _CurrentUsesParticleMask ("Use Current Particle Mask", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
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
                float  _ClipAboveWater;
                float4 _CurrentRhythm;
                half4 _CurrentGlowColour;
                float _CurrentUsesParticleMask;
            CBUFFER_END

            // set by WaterSurface: cell > 0 means there is water in the scene, centre.y is its mean level
            float  _WaterMeshCell;
            float  _WaterMeshGrowth;
            float4 _WaterMeshCenter;

            // FFT ocean (OceanFFT): three cascades of displacement; length scales stay 0 without it
            TEXTURE2D(_OceanDisp0);
            TEXTURE2D(_OceanDisp1);
            TEXTURE2D(_OceanDisp2);
            SamplerState ocean_trilinear_repeat_sampler;
            float4 _OceanLengthScales;

            // same mip choice as the water mesh (WaterWaves.hlsl), so the cut matches the surface you see
            float OceanLod(float lengthScale, float cell)
            {
                return max(0.0, log2(cell * 256.0 / lengthScale));
            }

            float3 OceanDisplacement(float2 xz)
            {
                float cell = max(_WaterMeshCell, 1e-3) * (1.0 + length(xz - _WaterMeshCenter.xz) / max(_WaterMeshGrowth, 1e-3));
                float3 d = SAMPLE_TEXTURE2D_LOD(_OceanDisp0, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.x, OceanLod(_OceanLengthScales.x, cell)).xyz;
                d += SAMPLE_TEXTURE2D_LOD(_OceanDisp1, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.y, OceanLod(_OceanLengthScales.y, cell)).xyz;
                d += SAMPLE_TEXTURE2D_LOD(_OceanDisp2, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.z, OceanLod(_OceanLengthScales.z, cell)).xyz;
                return d;
            }

            // world Y of the wavy surface above xz (mean level without the FFT ocean)
            float SurfaceHeight(float2 xz)
            {
                float level = _WaterMeshCenter.y;
                if (_OceanLengthScales.x <= 0.0) return level;
                // the FFT also moves water sideways: find the undisplaced point that ends up above xz
                float2 xz0 = xz;
                [unroll]
                for (int k = 0; k < 2; k++)
                    xz0 = xz - OceanDisplacement(xz0).xz;
                return level + OceanDisplacement(xz0).y;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float3 uv         : TEXCOORD0; // xy = sprite UV, z = Custom1.x rhythm flag
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  surfaceY   : TEXCOORD2;
                half rhythmMask   : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                // Vertex colour is constant across a speck: its phase is stable without another particle stream.
                // Render-only displacement preserves the simulation's current drift and cannot bias positions.
                float speckPhase = dot(v.color.rgb, float3(131.7, 231.1, 95.3));
                o.rhythmMask = lerp(1.0, saturate(v.uv.z), _CurrentUsesParticleMask);
                o.positionWS.y += o.rhythmMask * _CurrentRhythm.y * sin(_CurrentRhythm.z + speckPhase);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv.xy, _BaseMap);
                // per vertex is plenty: a bubble is far smaller than a wave
                o.surfaceY = _WaterMeshCell > 0.0 ? SurfaceHeight(o.positionWS.xz) : 1e6;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                // Real additive HDR emission, independent of the dim base tint. Existing bubbles have a zero pulse.
                c.rgb += _CurrentGlowColour.rgb * (_CurrentRhythm.x * i.rhythmMask);

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

                if (_WaterMeshCell > 0.0)
                {
                    float depth = i.surfaceY - i.positionWS.y;
                    // bubbles pop when they reach the surface
                    if (_ClipAboveWater > 0.5) a *= saturate(depth / _SurfaceFade);
                    // seen from above, they are drawn over the water surface: sink them into it with depth
                    if (_WorldSpaceCameraPos.y > _WaterMeshCenter.y + 0.5 && depth > 0.0)
                        a *= exp(-depth * 1.5);
                }

                return half4(c.rgb * a, a * (1.0h - _Additive));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

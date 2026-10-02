Shader "AKI/WaterUnderwater"
{
    // Full-screen effect drawn while the camera is below the surface. Properties are identical to
    // AKI/Water on purpose: WaterSurface copies the water material's values into this one.
    Properties
    {
        [Header(Water Body)]
        _ShallowColor      ("Shallow Colour", Color) = (0.01, 0.32, 0.52, 1)
        _DeepColor         ("Deep Colour", Color) = (0.00, 0.03, 0.15, 1)
        _Absorption        ("Absorption RGB (1/m)", Vector) = (0.42, 0.11, 0.05, 0)
        _DepthDistance     ("Depth To Deep Colour (m)", Range(0.5, 30)) = 3.5
        _Turbidity         ("Turbidity / Haze", Range(0.2, 5)) = 1
        _ScatterBrightness ("Scatter Brightness", Range(0, 3)) = 0.7
        _SSSColor          ("Crest Translucency Colour", Color) = (0.03, 0.45, 0.75, 1)
        _SSSIntensity      ("Crest Translucency", Range(0, 3)) = 0.5

        [Header(Waves)]
        _WindAngle         ("Wind Direction (deg)", Range(0, 360)) = 30
        _WaveAmplitude     ("Amplitude (m)", Range(0, 3)) = 0.9
        _WaveLength        ("Longest Wavelength (m)", Range(2, 80)) = 18
        _WaveSteepness     ("Choppiness", Range(0, 1)) = 0.7
        _WaveSpread        ("Direction Spread", Range(0, 1)) = 0.5
        _WaveSpeed         ("Speed", Range(0, 3)) = 1
        _WaveVariation     ("Swell Variation", Range(0, 0.9)) = 0.45
        _WaveRandomness    ("Randomness", Range(0, 1)) = 0.85
        _WaveSeed          ("Random Seed", Range(0, 100)) = 7
        _WaveWarp          ("Voronoi Warp (rad)", Range(0, 6)) = 2.6
        _WarpScale         ("Voronoi Warp Scale (1/m)", Range(0.005, 0.15)) = 0.03
        _CellRipples       ("Voronoi Cell Ripples", Range(0, 1)) = 0.45
        _CellScale         ("Cell Ripple Scale (1/m)", Range(0.1, 2)) = 0.42
        _CellWaveHeight    ("Voronoi Wave Height (m)", Range(0, 2)) = 0.55
        _CellWaveScale     ("Voronoi Wave Scale (1/m)", Range(0.02, 0.4)) = 0.09
        _CellWaveSpeed     ("Voronoi Wave Speed", Range(0, 3)) = 0.7
        [Toggle(_WAVE_DETAIL)] _WaveDetail ("Per-pixel Detail Waves", Float) = 1
        [Toggle(_FFT_WAVES)] _FFTWaves ("FFT Ocean (needs OceanFFT component)", Float) = 0
        _DetailStrength    ("Detail Strength", Range(0, 2)) = 0.8
        _DetailFade        ("Detail Fade (x wavelength)", Range(10, 300)) = 90
        _FlattenDistance   ("Flatten Distance (m)", Range(50, 1500)) = 500

        [Header(Surface)]
        _ReflectionStrength("Reflection", Range(0, 1)) = 0.6
        _Roughness         ("Reflection Roughness", Range(0, 0.6)) = 0.08
        _SpecularIntensity ("Sun Glint", Range(0, 5)) = 0.7
        _SpecularPower     ("Sun Glint Sharpness", Range(20, 2000)) = 500
        _RefractionStrength("Refraction", Range(0, 4)) = 1.1
        _EdgeSoftness      ("Shore Edge Softness (m)", Range(0.01, 2)) = 0.35
        _SimpleOpacity     ("Opacity (no scene textures)", Range(0, 1)) = 0.9

        [Header(Cartoon Look)]
        _ToonAmount        ("Cartoon Amount", Range(0, 1)) = 0
        _ToonBands         ("Colour Bands", Range(2, 8)) = 4
        _ToonSoftness      ("Band Edge Softness", Range(0.001, 0.5)) = 0.2
        _ToonHighlight     ("Wave Highlight Colour", Color) = (0.70, 1.0, 0.95, 1)

        [Header(Large Scale Voronoi)]
        _ColorCellScale    ("Colour Cell Scale (1/m)", Range(0.005, 0.2)) = 0.03
        _ColorVariation    ("Colour Variation", Range(0, 1.5)) = 0.15
        _GlintCellScale    ("Glint Cell Scale (1/m)", Range(0.01, 0.4)) = 0.07
        _GlintCellStrength ("Glint Cell Strength", Range(0, 1)) = 0.85

        [Header(Foam)]
        [Toggle(_FOAM)] _FoamOn ("Foam", Float) = 1
        _FoamColor         ("Foam Colour", Color) = (1, 1, 1, 1)
        _ShoreFoamWidth    ("Shore Foam Width (m)", Range(0.05, 4)) = 1.1
        _CrestFoamThreshold("Crest Foam Threshold", Range(0, 1)) = 0.55
        _FoamScale         ("Foam Scale", Range(0.1, 4)) = 0.9
        _FoamIntensity     ("Foam Intensity", Range(0, 2)) = 1

        [Header(Caustics)]
        [Toggle(_CAUSTICS)] _CausticsOn ("Caustics On Bottom", Float) = 1
        _CausticsIntensity ("Intensity", Range(0, 6)) = 2.2
        _CausticsScale     ("Scale", Range(0.05, 3)) = 0.6
        _CausticsSpeed     ("Speed", Range(0, 3)) = 0.7
        _CausticsMaxDepth  ("Fade Depth (m)", Range(1, 60)) = 25
        _CausticsDispersion("Rainbow Split", Range(0, 1)) = 0.4

        [Header(Light Shafts)]
        [Toggle(_GODRAYS)] _GodRaysOn ("Underwater Light Shafts", Float) = 1
        _RayColor          ("Colour", Color) = (0.50, 0.80, 1, 1)
        _RayIntensity      ("Intensity", Range(0, 8)) = 2.6
        _RayScale          ("Pattern Scale", Range(0.02, 1.5)) = 0.22
        _RaySteps          ("Steps (quality)", Range(2, 12)) = 8
        _RayLength         ("Max Length (m)", Range(1, 60)) = 50
        _RayPhase          ("Forward Scattering", Range(0, 0.9)) = 0.72
        _RayFade           ("View Fade", Range(0, 0.5)) = 0.045
        _RayContrast       ("Beam Contrast", Range(0.5, 8)) = 1.9
        _RayDepthFade      ("Fade With Depth (1/m)", Range(0, 1)) = 0.025

        [Header(Seen From Below)]
        _UnderFogScale     ("Underwater Fog Density", Range(0.05, 6)) = 0.6
        _UnderWobble       ("Underwater Wobble", Range(0, 3)) = 1
        _UnderMaxDistance  ("Underwater Visibility (m)", Range(5, 300)) = 120
        _UnderFogColor     ("Underwater Haze Colour", Color) = (0.02, 0.19, 0.44, 1)
        _UnderDeepColor    ("Underwater Deep Colour", Color) = (0.00, 0.02, 0.08, 1)
        _UnderDepthFalloff ("Haze Darkening With Depth (1/m)", Range(0.005, 0.2)) = 0.05
        _UnderLightAbsorb  ("Sunlight Loss With Depth", Range(0, 2)) = 0.6

        [Header(Platform)]
        [Toggle(_SCENE_TEXTURES)] _SceneTextures ("Use Depth + Opaque Texture (PC)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"     // after opaques + the opaque-texture copy, before the water surface
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Underwater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            // multi_compile (not shader_feature): the keywords are switched on from script at runtime
            #pragma multi_compile_local_fragment _ _CAUSTICS
            #pragma multi_compile_local_fragment _ _GODRAYS
            #pragma multi_compile_local _ _FFT_WAVES

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #include "WaterInput.hlsl"
            #include "WaterWaves.hlsl"
            #include "WaterEffects.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  mode       : TEXCOORD0;   // 1 = fully submerged, 0.5 = at the surface (per-pixel waterline)
                nointerpolation float4 plane : TEXCOORD1;   // the surface around the camera (WaterCameraPlane)
            };

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o;
                const float t = _Time.y;
                const float waterLevel = GetObjectToWorldMatrix()._m13;
                const float q = WaterQ();
                const float3 cam = _WorldSpaceCameraPos;

                // height of the wavy surface right above/below the camera (undo the Gerstner xz shift by iteration)
                float2 xz0 = cam.xz;
                [unroll]
                for (int k = 0; k < 3; k++)
                    xz0 = cam.xz - WaterDisplacement(xz0, t, q).xz;
                float surfY = waterLevel + WaterDisplacement(xz0, t, q).y;

                float band = WaterSurfaceBand();
                o.mode = (cam.y < surfY - band) ? 1.0 : 0.5;
                o.plane = WaterCameraPlaneLoad();
                // camera well above the water -> collapse the triangle outside the screen (no pixels are shaded)
                o.positionCS = (cam.y < surfY + band) ? GetFullScreenTriangleVertexPosition(vid) : float4(2, 2, 1, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                const float t = _Time.y;
                const float waterLevel = GetObjectToWorldMatrix()._m13;
                const float3 cam = _WorldSpaceCameraPos;

                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                // gentle screen-space wobble, like looking through moving water
                float2 wob = float2(sin(uv.y * 18.0 + t * 1.6 + sin(uv.x * 7.0 + t)),
                                    cos(uv.x * 15.0 - t * 1.2 + sin(uv.y * 9.0 - t * 0.8))) * (_UnderWobble * 0.004);
                float2 uvD = uv + wob;

                float rawD = SampleSceneDepth(uvD);
                bool sky = WATER_IS_SKY(rawD);
                float3 worldPos = ComputeWorldSpacePosition(uvD, rawD, UNITY_MATRIX_I_VP);
                float3 toPix = worldPos - cam;
                float dist = length(toPix);
                float3 dir = toPix / max(dist, 1e-4);
                dist = min(dist, _UnderMaxDistance);

                // A ray going up into the open (nothing in the scene in the way) runs into the water's surface, and
                // that is drawn over this pixel with its own haze and light shafts: nothing to do here. (Looking up,
                // that is half the screen that used to be worked out twice.)
                if (i.mode > 0.75 && sky && dir.y * 2000.0 > max(waterLevel - cam.y, 0.0)) discard;

                // per-pixel waterline when the camera is at the surface
                half coverage = 1.0h;
                if (i.mode < 0.75)
                {
                    // the ray through this pixel, without the wobble: the split has to match the lens' waterline
                    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                    float3 rayDir = normalize(ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP) - cam);
                    float3 np = WaterNearPoint(cam, rayDir, camFwd, _ProjectionParams.y);
                    coverage = (half)smoothstep(-0.0005, 0.0005, WaterPlaneSubmergedDist(i.plane, np, cam));
                    if (coverage <= 0.0h) discard;
                }

                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                half3 lightColor = mainLight.color;
                half3 ambient = SampleSH(float3(0, 1, 0));
                half3 scatterLight = WaterScatterLight(ambient, L, lightColor);

                half3 sceneCol = sky ? half3(0, 0, 0) : SampleSceneColor(uvD);
            #if defined(_CAUSTICS)
                // under the real waves, not under the flat mean level (that cut objects in a crest along a straight line);
                // the exact test only near the surface, where a crest or trough can make a difference
                if (!sky)
                {
                    float band = WaterSurfaceBand();
                    bool submerged = worldPos.y < waterLevel - band
                                  || (worldPos.y < waterLevel + band && WaterSubmergedDist(worldPos, t, waterLevel) > 0.0);
                    if (submerged)
                        sceneCol = WaterApplyCaustics(sceneCol, worldPos, waterLevel, L, lightColor, t);
                }
            #endif

                sceneCol *= lerp(half3(1, 1, 1), saturate(_ShallowColor.rgb * 1.25h), _ToonAmount * 0.75);   // cartoon: tint instead of grey-out

                float camDepth = max(waterLevel - cam.y, 0.0);
                // Fog colour: scattered light along the ray. It depends on the depth of both ends of the ray and glows
                // towards the sun. Looking sideways the far end stays at camera depth, so the horizon fades into one even haze.
                float endDepth = max(waterLevel - (cam.y + dir.y * dist), 0.0);
                half3 fogCol = WaterUnderFogColor(camDepth, endDepth, dir, L, scatterLight);
                float3 T = WaterTransmittanceSoft(dist, _UnderFogScale);
                half3 col = sceneCol * T + fogCol * (1.0 - T);

            #if defined(_GODRAYS)
                col += WaterGodRays(cam, cam + dir * dist, i.positionCS.xy, L, lightColor, waterLevel, t) * 2.0h;
            #endif

                // cartoon: saturation
                half luma = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, 1.0 + 0.12 * _ToonAmount);
                return half4(col, coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

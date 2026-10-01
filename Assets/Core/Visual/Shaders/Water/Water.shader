Shader "AKI/Water"
{
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
        _RaySteps          ("Steps (quality)", Range(2, 32)) = 24
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
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off      // both sides: the underside is the ceiling you see from below

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _SCENE_TEXTURES
            #pragma shader_feature_local_fragment _WAVE_DETAIL
            #pragma shader_feature_local _FFT_WAVES
            #pragma shader_feature_local_fragment _FOAM
            #pragma shader_feature_local_fragment _CAUSTICS
            #pragma shader_feature_local_fragment _GODRAYS

            // hard (single tap) shadows only: the shafts and caustics sample the shadow map many times per pixel
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #include "WaterInput.hlsl"
            #include "WaterWaves.hlsl"
            #include "WaterEffects.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 waveXZ     : TEXCOORD1;   // undisplaced xz - what the wave functions are evaluated at
                float  fogCoord   : TEXCOORD2;
                float  q          : TEXCOORD3;   // Gerstner steepness factor (constant per material)
                float4 fieldA     : TEXCOORD4;   // low-frequency wave fields, evaluated per vertex (see WaterWaves.hlsl)
                float4 fieldB     : TEXCOORD5;
                float4 fieldC     : TEXCOORD6;
                float4 cellColor  : TEXCOORD7;   // large-scale colour + glint cells (xy colour, zw glints)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.waveXZ = posWS.xz;
                o.q = WaterQ();

                float dist = distance(posWS, _WorldSpaceCameraPos);
                float geoFade = saturate(1.0 - (dist - _FlattenDistance * 0.6) / (_FlattenDistance * 0.4));
                const float tt = _Time.y;
            #if defined(_FFT_WAVES)
                o.fieldA = 0; o.fieldB = 0; o.fieldC = 0;
                o.cellColor = 0;
                posWS += OceanDisplacement(posWS.xz) * geoFade;
            #else
                WaterWaveField field = WaterEvalField(posWS.xz, tt);
                WaterPackField(field, o.fieldA, o.fieldB, o.fieldC);
                o.cellColor = float4((WaterSoftCells(posWS.xz * _ColorCellScale, tt * 0.04) - 0.5) * 1.8,
                                     WaterSoftCells(posWS.xz * _GlintCellScale, tt * 0.08));
                posWS += WaterDisplacementF(field, posWS.xz, tt, o.q) * geoFade;
            #endif

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // The water surface as seen from below: Snell's window onto the sky, mirror outside it.
            // It is fogged (and lit by the same light shafts) exactly like the rest of the volume, so at a distance
            // it becomes the very same haze as the water beside it and there is no seam at the horizon.
            half4 WaterUnderside(float4 positionCS, float3 posWS, float3 V, float3 n, float dist, float waterLevel, float t,
                                 float3 L, half3 lightColor, half3 scatterLight)
            {
                float3 cam = _WorldSpaceCameraPos;
                float camDepth = max(waterLevel - cam.y, 0.0);
                float3 viewDir = -V;                                 // camera -> surface
                float capDist = min(dist, _UnderMaxDistance);
                float endDepth = max(waterLevel - (cam.y + viewDir.y * capDist), 0.0);

                // volume haze along this view ray (same formula as the full-screen effect)
                half3 hazeCol = WaterUnderFogColor(camDepth, endDepth, viewDir, L, scatterLight);
            #if defined(_GODRAYS)
                hazeCol += WaterGodRays(cam, cam + viewDir * capDist, positionCS.xy, L, lightColor, waterLevel, t) * 2.0h;
            #endif
                // what the mirror part of the ceiling reflects: the horizontal water at the camera's depth
                half3 mirrorCol = WaterUnderFogColor(camDepth, camDepth, normalize(float3(viewDir.x, 0.0, viewDir.z) + 1e-4), L, scatterLight);

                float3 nU = -n;
                float3 R = refract(viewDir, nU, WATER_IOR);          // water -> air
                bool tir = dot(R, R) < 1e-4;                         // beyond the critical angle: total internal reflection
                R = tir ? reflect(viewDir, nU) : normalize(R);
                float fres = tir ? 1.0 : (0.02 + 0.98 * pow(1.0 - saturate(dot(R, n)), 5.0));

                half3 above = GlossyEnvironmentReflection(R, posWS, _Roughness, 1.0h);
            #if defined(_SCENE_TEXTURES)
                if (!tir)
                {
                    // let the beach / rocks above water show through the window when they are on screen
                    float2 uvR = ComputeNormalizedDeviceCoordinates(posWS + R * 40.0, UNITY_MATRIX_VP);
                    float2 e = saturate(min(uvR, 1.0 - uvR) * 12.0);
                    float rawR = SampleSceneDepth(uvR);
                    float3 wsR = ComputeWorldSpacePosition(uvR, rawR, UNITY_MATRIX_I_VP);
                    float valid = e.x * e.y * ((WATER_IS_SKY(rawR) || wsR.y > waterLevel + 0.05) ? 1.0 : 0.0);
                    above = lerp(above, SampleSceneColor(uvR), valid);
                }
            #endif

                // the ceiling's pattern loses contrast quickly with distance
                float detail = exp(-dist * 0.06);
                float shimmer = WaterCaustics(posWS.xz * 0.35, t * 0.5);
                shimmer = lerp(shimmer * shimmer, smoothstep(0.5, 0.58, shimmer), _ToonAmount) * detail;
                float facet = lerp(1.0, 0.7 + 0.6 * saturate(dot(n, normalize(L + float3(0, 1, 0)))), detail);
                half3 mirror = mirrorCol * (1.15 * facet) + _RayColor.rgb * lightColor * (shimmer * 0.22 * saturate(L.y * 3.0) * exp(-camDepth * 0.1));

                half3 col = lerp(above * 1.35h, mirror, fres);      // the sky through the window is bright white-blue

                float3 T = WaterTransmittanceSoft(dist * 2.0, _UnderFogScale);
                col = col * T + hazeCol * (1.0 - T);

                half luma = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, 1.0 + 0.12 * _ToonAmount);
                return half4(col, 1.0h);
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                const float t = _Time.y;
                const float waterLevel = GetObjectToWorldMatrix()._m13;
                // A camera below the surface only ever sees the underside (this also hides folded far-away waves
                // that would otherwise flash their top side through the haze).
                // Near the surface the camera can be half in the water: decide per pixel whether this view ray starts
                // below the waves (same test as the underwater overlay and the lens effect, so the split matches).
                bool camAbove = _WorldSpaceCameraPos.y >= waterLevel;
                if (abs(_WorldSpaceCameraPos.y - waterLevel) < WaterSurfaceBand())
                {
                    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                    float3 np = WaterNearPoint(_WorldSpaceCameraPos, normalize(i.positionWS - _WorldSpaceCameraPos), camFwd, _ProjectionParams.y);
                    camAbove = WaterSubmergedDist(np, t, waterLevel) <= 0.0;
                }
                const bool front = camAbove;

                float3 posWS = i.positionWS;
                float3 toCam = _WorldSpaceCameraPos - posWS;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 1e-4);

                // ---- waves: normal + how much the surface is pinching (foam)
                float3 n;
                float jacobian, lace;
            #if defined(_FFT_WAVES)
                OceanSurface(i.waveXZ, n, jacobian);     // turbulence plays the jacobian's role for foam
                lace = 0.0;
            #else
                WaterWaveField field = WaterUnpackField(i.fieldA, i.fieldB, i.fieldC);
                WaterNormal(field, i.waveXZ, t, dist, i.q, n, jacobian, lace);
            #endif
                n = normalize(lerp(n, float3(0, 1, 0), 0.25 * _ToonAmount));   // cartoon: calmer normals
                n = normalize(lerp(n, float3(0, 1, 0), saturate((dist - _FlattenDistance * 0.5) / (_FlattenDistance * 0.5))));

                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                half3 lightColor = mainLight.color;

                half3 ambient = SampleSH(float3(0, 1, 0));
                half3 scatterLight = WaterScatterLight(ambient, L, lightColor);

                if (!front)
                    return WaterUnderside(i.positionCS, posWS, V, n, dist, waterLevel, t, L, lightColor, scatterLight);

                float NdotV = saturate(dot(n, V));
                float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                fresnel = lerp(fresnel, WaterToonQuant(fresnel, 3.0, _ToonSoftness), _ToonAmount * 0.6);

                // ---- large-scale Voronoi patches of slightly different water colour, plus faint cell borders
                float colorBias = 0.0;
                float colorTone = 1.0;
            #if defined(_WAVE_DETAIL)
                {
                    // very soft blobs of slightly different colour, no borders
                    float2 sc = i.cellColor.xy;
                    colorBias = sc.x * 0.5 * _ColorVariation;
                    colorTone = 1.0 + sc.y * 0.3 * _ColorVariation;
                }
            #endif

                half3 under;
                half alpha;
                float shore = 0.0;
                float shoreDistOut = 100.0;

            #if defined(_SCENE_TEXTURES)
                float2 uv0 = GetNormalizedScreenSpaceUV(i.positionCS);

                float surfEye = LinearEyeDepth(posWS, UNITY_MATRIX_V);
                float rawD = SampleSceneDepth(uv0);
                float sceneEye = LinearEyeDepth(rawD, _ZBufferParams);
                float3 sceneWS0 = ComputeWorldSpacePosition(uv0, rawD, UNITY_MATRIX_I_VP);
                float shoreDist = max(sceneEye - surfEye, 0.0);   // distance to whatever is behind the water surface
                shoreDistOut = shoreDist;

                // ---- refraction: bend the view ray by the wave normal, project the hit point back to the screen.
                //      Deeper water => bigger shift, so the seabed shimmers and distorts like real water.
                float3 I = -V;
                float3 Rr = refract(I, n, 1.0 / WATER_IOR);
                Rr = normalize(lerp(I, Rr, _RefractionStrength));
                float pathLen0 = WATER_IS_SKY(rawD) ? 40.0 : min(distance(posWS, sceneWS0), 40.0);
                float2 uvR = ComputeNormalizedDeviceCoordinates(posWS + Rr * pathLen0, UNITY_MATRIX_VP);
                float2 edge = saturate(min(uvR, 1.0 - uvR) * 24.0);
                uvR = lerp(uv0, uvR, edge.x * edge.y);

                float rawR = SampleSceneDepth(uvR);
                float eyeR = LinearEyeDepth(rawR, _ZBufferParams);
                // something stands in front of the water at the refracted position -> do not pull it into the water
                if (eyeR < surfEye)
                {
                    uvR = uv0;
                    rawR = rawD;
                }

                bool sky = WATER_IS_SKY(rawR);
                float3 bottomWS = ComputeWorldSpacePosition(uvR, rawR, UNITY_MATRIX_I_VP);
                float depthV = sky ? 1000.0 : max(waterLevel - bottomWS.y, 0.0);
                float pathLen = sky ? 1000.0 : distance(posWS, bottomWS);

                half3 bottomColor = sky ? half3(0, 0, 0) : SampleSceneColor(uvR);

            #if defined(_CAUSTICS)
                if (!sky)
                    bottomColor = WaterApplyCaustics(bottomColor, bottomWS, waterLevel, L, lightColor, t);
            #endif

                // ---- Beer-Lambert absorption + in-scattering = the haze of dense water
                float3 T = WaterTransmittance(pathLen, 1.0);
                half3 scatterCol = WaterBodyColorB(depthV, colorBias, scatterLight);
                // cartoon: the bottom seen through the water takes on the water's tint instead of going grey
                bottomColor *= lerp(half3(1, 1, 1), saturate(_ShallowColor.rgb * 1.25h), _ToonAmount * 0.75);
                under = bottomColor * T + scatterCol * (1.0 - T);

                // ---- god rays: light shafts through the water column
            #if defined(_GODRAYS)
                float3 endWS = sky ? posWS + Rr * _RayLength : bottomWS;
                // far away the shafts are too small to see: skip the march
                half3 rays = dist < 150.0 ? WaterGodRays(posWS, endWS, i.positionCS.xy, L, lightColor, waterLevel, t) : half3(0, 0, 0);
                under += rays * (1.0 - fresnel);
            #endif

                alpha = saturate(shoreDist / _EdgeSoftness);
                shore = saturate(1.0 - shoreDist / _ShoreFoamWidth);
            #else
                // mobile / no scene textures: analytic gradient by viewing angle, no refraction or depth
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(0.5 + 0.5 * pow(NdotV, 1.5)));
                body = lerp(body, WaterToonPosterize(body, _ToonBands, _ToonSoftness), _ToonAmount);
                under = body * scatterLight;
                alpha = saturate(_SimpleOpacity + fresnel);
            #endif

                under *= colorTone;

                // ---- crest translucency: sun glowing through thin wave tops
                float height = (posWS.y - waterLevel) / max(_WaveAmplitude, 0.05);
                float sssView = pow(saturate(dot(V, -(L + n * 0.4))), 4.0);
                under += _SSSColor.rgb * lightColor * (sssView * saturate(height * 0.5 + 0.5) * _SSSIntensity * saturate(L.y * 3.0));

                // ---- cartoon crest bands: a lighter stripe painted along the tops of the waves
                float crestNoise = WaterValueNoise(i.waveXZ * 0.35 + t * 0.05);
                float crestBand = smoothstep(0.50, 0.55, height * 0.5 + 0.5 + (crestNoise - 0.5) * 0.35);
                under += _ToonHighlight.rgb * (crestBand * 0.28 * _ToonAmount);

                // ---- reflection of sky / probes with Fresnel
                float3 Rf = reflect(-V, n);
                Rf.y = abs(Rf.y);
                half3 refl = GlossyEnvironmentReflection(Rf, posWS, _Roughness, 1.0h);
                refl = lerp(refl, WaterToonPosterize(refl, _ToonBands + 1.0, _ToonSoftness), _ToonAmount * 0.75);
                half3 col = lerp(under, refl, saturate(fresnel * _ReflectionStrength));

                // ---- sun glints: soft Blinn-Phong, or hard-edged sparkles in cartoon mode
                float3 H = normalize(L + V);
                float ndh = saturate(dot(n, H));
                float specReal = pow(ndh, _SpecularPower) * (_SpecularPower + 8.0) * (1.0 / (8.0 * PI));
                specReal += pow(ndh, 40.0) * 0.10;        // broad soft sheen: the "sun path" on the water
                float specToon = smoothstep(0.35, 0.42, pow(ndh, _SpecularPower * 0.3)) * 1.5;
                float spec = lerp(specReal, specToon, _ToonAmount);
            #if defined(_WAVE_DETAIL)
                {
                    // sun glints come in soft clusters that swell and fade
                    float2 sg = i.cellColor.zw;
                    float twinkle = 0.5 + 0.5 * sin(t * (0.6 + sg.y) + sg.x * 14.0);
                    float mask = 0.3 + 1.4 * twinkle * (0.5 + sg.y);
                    spec *= lerp(1.0, mask, _GlintCellStrength);
                }
            #endif

                col += lightColor * (spec * _SpecularIntensity * saturate(dot(n, L)));

                // ---- foam: shoreline + breaking crests
            #if defined(_FOAM)
                float crest = saturate((_CrestFoamThreshold - jacobian) / 0.35);
                float lap = 0.85 + 0.15 * sin(t * 1.4 + shore * 9.0);
            #if defined(_FFT_WAVES)
                float foam = WaterFoam(i.waveXZ, t, shore * lap, 0.0, lace) * _FoamIntensity;      // shoreline
                float oceanHaze;
                foam = max(foam, OceanFoam(i.waveXZ, t, jacobian, oceanHaze) * _FoamIntensity);  // breaking crests
                col = lerp(col, _FoamColor.rgb * (ambient + lightColor * 0.35) * 0.45, oceanHaze * oceanHaze * 0.18);   // churned water
            #else
                float foam = WaterFoam(i.waveXZ, t, shore * lap, crest, lace) * _FoamIntensity;
            #endif

                // cartoon shoreline: crisp animated stripes of foam rolling in
                float wide = saturate(1.0 - shoreDistOut / (_ShoreFoamWidth * 2.6));
                float stripe = smoothstep(0.55, 0.62, sin(shoreDistOut * 5.0 - t * 1.6) * 0.5 + 0.5) * wide * wide;
                foam = max(foam, stripe * _ToonAmount);

                foam = saturate(foam) * _FoamColor.a;
                half3 foamLit = _FoamColor.rgb * (ambient + lightColor * (0.25 + 0.75 * saturate(dot(float3(0, 1, 0), L))));
                foamLit = lerp(foamLit, _FoamColor.rgb, _ToonAmount);    // flat, unlit white
                col = lerp(col, foamLit, foam);
                alpha = saturate(max(alpha, foam));
            #endif

                // cartoon: push saturation up
                half luma = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, 1.0 + 0.35 * _ToonAmount);

                col = MixFog(col, i.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

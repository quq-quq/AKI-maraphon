Shader "Hidden/AKI/WaterLens"
{
    // Full-screen "camera lens" pass drawn by WaterLensFeature after post-processing: water sheet + drops after
    // surfacing, and the waterline across the lens. Properties mirror AKI/Water so the wave maths matches exactly
    // (WaterCameraEffects copies the water material's values in).
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
        _RayIntensity      ("Intensity", Range(0, 8)) = 1.6
        _RayScale          ("Pattern Scale", Range(0.02, 1.5)) = 0.22
        _RaySteps          ("Steps (quality)", Range(2, 32)) = 16
        _RayLength         ("Max Length (m)", Range(1, 60)) = 30
        _RayPhase          ("Forward Scattering", Range(0, 0.9)) = 0.72
        _RayFade           ("View Fade", Range(0, 0.5)) = 0.07
        _RayContrast       ("Beam Contrast", Range(0.5, 8)) = 2.4
        _RayDepthFade      ("Fade With Depth (1/m)", Range(0, 1)) = 0.06

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
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "WaterLens"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #include "WaterInput.hlsl"
            #include "WaterWaves.hlsl"

            // set by WaterCameraEffects
            float  _WaterLensWetness;        // 1 right after surfacing .. 0 dry
            float  _WaterLensSinceExit;      // seconds since the camera left the water
            float  _WaterLensNearSurface;    // 1 while the camera is close enough to the surface to be half submerged
            float  _WaterLensDistortion;
            float  _WaterLensDrops;
            float  _WaterLevelGlobal;
            float4 _WaterLensCamPos;
            float4 _WaterLensNearFwd;        // forward * near distance
            float4 _WaterLensNearRight;      // right * half width of the near plane
            float4 _WaterLensNearUp;         // up * half height of the near plane
            float  _BreathEffect;            // 0..1 suffocation from holding the breath (BreathHolding)

            float3 LensHash32(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            float LensHash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            half3 FragBlitSample(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, saturate(uv), 0).rgb;
            }

            // ---------------------------------------------------------------- drops
            // Everything is a height field in "screen height" units (x is multiplied by the aspect ratio so drops stay round).
            // Its gradient bends the image like a tiny lens.

            // Beads that stick to the glass. They evaporate one by one as the wetness goes down.
            float Beads(float2 q, float density, float wet, float seed)
            {
                float2 g = q * density;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float3 n = LensHash32(id + seed);
                float alive = saturate((wet * 1.15 - n.z) * 6.0) * step(0.6, frac(n.z * 7.31));
                float2 c = (n.xy - 0.5) * 0.55;
                float r = (0.14 + 0.24 * n.y) * alive;
                float2 d = f - c;
                float h = saturate(1.0 - dot(d, d) / max(r * r, 1e-5));
                return sqrt(h) * r / density;
            }

            // Big drops running down the lens, leaving a thin wet trail. Speed and start time differ per column.
            float Runners(float2 q, float sinceExit, float wet, float aspect)
            {
                const float cols = 9.0;
                float cx = q.x * cols;
                float id = floor(cx);
                float n0 = LensHash11(id + 3.1);
                float n1 = LensHash11(id + 11.7);
                float n2 = LensHash11(id + 27.3);

                float alive = saturate((wet * 1.3 - n0 * 0.9) * 4.0);
                float speed = 0.18 + 0.35 * n1;
                float yHead = 1.1 - frac(sinceExit * speed * 0.8 + n2) * 1.4;          // top -> bottom, then again
                float wiggle = sin(q.y * 23.0 + n0 * 40.0) * 0.004 + sin(q.y * 61.0 + n1 * 17.0) * 0.0015;
                float dx = (frac(cx) - 0.5 - (n0 - 0.5) * 0.6) / cols + wiggle;
                float dy = q.y - yHead;

                float rw = (0.015 + 0.009 * n1) * alive;
                float rl = rw * (dy > 0.0 ? 1.7 : 1.1);                                  // teardrop: long tail above
                float head = saturate(1.0 - (dx * dx) / max(rw * rw, 1e-6) - (dy * dy) / max(rl * rl, 1e-6));
                float h = sqrt(head) * rw;

                // wet trail above the drop, getting thinner with distance
                float trailLen = saturate(dy / 0.5);
                float tw = rw * 0.45 * (1.0 - trailLen);
                float trail = (dy > 0.0) ? saturate(1.0 - (dx * dx) / max(tw * tw, 1e-7)) : 0.0;
                h = max(h, sqrt(trail) * tw * 0.6);
                return h;
            }

            float DropHeight(float2 q, float wet, float sinceExit, float aspect)
            {
                float h = Beads(q, 18.0, wet, 1.0);
                h = max(h, Beads(q, 34.0, wet, 7.3));
                                h = max(h, Runners(q, sinceExit, wet, aspect));
                return h;
            }

            half3 SampleBlur(float2 uv, float radius)
            {
                half3 c = FragBlitSample(uv) * 0.4h;
                c += FragBlitSample(uv + float2( radius,  radius)) * 0.15h;
                c += FragBlitSample(uv + float2(-radius,  radius)) * 0.15h;
                c += FragBlitSample(uv + float2( radius, -radius)) * 0.15h;
                c += FragBlitSample(uv + float2(-radius, -radius)) * 0.15h;
                return c;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float t = _Time.y;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 q = float2(uv.x * aspect, uv.y);

                float2 offset = 0;
                float blur = 0;
                half light = 0;
                half dark = 0;

                // ---- the waterline crossing the lens
                if (_WaterLensNearSurface > 0.5)
                {
                    float3 np = _WaterLensCamPos.xyz + _WaterLensNearFwd.xyz
                              + _WaterLensNearRight.xyz * (uv.x * 2.0 - 1.0)
                              + _WaterLensNearUp.xyz * (uv.y * 2.0 - 1.0);
                    float s = WaterSubmergedDist(np, t, _WaterLevelGlobal);     // metres, > 0 under water
                    float sy = s / max(2.0 * length(_WaterLensNearUp.xyz), 1e-4); // -> fraction of the screen height

                    float band = exp(-abs(sy) * 90.0);
                    offset.y += band * 0.02 * _WaterLensDistortion * (sy > 0.0 ? 1.0 : -1.0);   // meniscus magnifies
                    dark += exp(-abs(sy) * 450.0) * 0.55;                                        // thin dark line
                    light += exp(-abs(sy + 0.006) * 350.0) * 0.35;                               // bright rim just above it
                    blur += band * 0.004;
                }

                // ---- water on the glass after surfacing
                float wet = _WaterLensWetness * _WaterLensDrops;
                if (wet > 0.001)
                {
                    float since = _WaterLensSinceExit;

                    // 1) a continuous sheet of water sliding off in the first half second
                    float edge = 1.2 - since * 2.2 + 0.035 * sin(uv.x * 19.0 + t * 3.0) + 0.02 * sin(uv.x * 47.0 - t * 5.0);
                    float sheet = smoothstep(edge + 0.015, edge - 0.015, uv.y) * _WaterLensWetness;
                    float2 wave = float2(sin(uv.y * 31.0 + t * 7.0 + sin(uv.x * 13.0)), cos(uv.x * 27.0 - t * 6.0 + sin(uv.y * 11.0)));
                    offset += wave * (0.012 * sheet * _WaterLensDistortion);
                    blur += sheet * 0.006;
                    float edgeLine = exp(-abs(uv.y - edge) * 140.0) * step(0.001, sheet + 0.001) * _WaterLensWetness;
                    offset.y += edgeLine * 0.02 * _WaterLensDistortion;
                    light += edgeLine * 0.25;

                    // 2) drops and running streaks
                    const float e = 0.0015;
                    float h0 = DropHeight(q, wet, since, aspect);
                    if (h0 > 0.0)
                    {
                        float hx = DropHeight(q + float2(e, 0), wet, since, aspect);
                        float hy = DropHeight(q + float2(0, e), wet, since, aspect);
                        float2 grad = float2(hx - h0, hy - h0) / e;
                        offset -= grad * (0.045 * _WaterLensDistortion);        // a drop is a tiny lens: flips the image a little
                        float3 nrm = normalize(float3(-grad, 1.0));
                        light += pow(saturate(dot(nrm, normalize(float3(-0.35, 0.55, 1.0)))), 60.0) * 0.6;   // glint
                        dark += saturate(length(grad) * 0.35) * 0.25;                                      // dark rim
                    }
                }

                half3 col = blur > 0.0005 ? SampleBlur(uv + offset, blur) : FragBlitSample(uv + offset);
                col *= 1.0h - saturate(dark);
                col += light;

                // ---- running out of air: a dark vignette closes in, then the whole view fades towards black
                float suff = saturate(_BreathEffect);
                if (suff > 0.0)
                {
                    float2 d = (uv - 0.5) * float2(aspect, 1.0);
                    float r = length(d);
                    float pulse = 1.0 + 0.06 * suff * sin(t * 6.0);                  // faint heartbeat near the end
                    float inner = lerp(0.95, 0.18, suff) * pulse;
                    float vig = smoothstep(inner, inner + lerp(0.6, 0.45, suff), r);
                    col *= 1.0h - (half)(vig * lerp(0.5, 1.0, suff));
                    col *= 1.0h - (half)(0.85 * suff * suff);                         // everything darkens
                    half luma = dot(col, half3(0.299, 0.587, 0.114));
                    col = lerp(col, luma.xxx, (half)(0.5 * suff));                   // colours drain out
                }
                return half4(col, 1.0h);
            }

            ENDHLSL
        }
    }

    Fallback Off
}

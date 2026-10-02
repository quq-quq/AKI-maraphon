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
            #pragma multi_compile_local _ _FFT_WAVES

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
            // set by Blackout (passing out and coming to)
            float  _ScreenFade;              // 0..1 towards black
            float  _WakeBlur;                // 0..1 gaussian blur of a view that isn't in focus yet
            float  _EyeClosed;               // 0 = eyes open .. 1 = lids shut

            float3 LensHash32(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            // smooth 2D value noise, 0..1. Quintic fade: its slope bends the view (refraction), so the slope has to
            // change smoothly across cell borders too, or the cells show up as seams.
            float LensNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = LensHash32(i).x, b = LensHash32(i + float2(1, 0)).x;
                float c = LensHash32(i + float2(0, 1)).x, d = LensHash32(i + float2(1, 1)).x;
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half3 FragBlitSample(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, saturate(uv), 0).rgb;
            }

            // ---------------------------------------------------------------- water on the lens after surfacing
            // A height field of water on the glass (q: x scaled by the aspect, y = 0 bottom .. 1 top; heights in
            // screen-height units). The view is bent by its slope. Three things, like water poured over glass:
            //  - a sheet that covers the whole lens the moment the head breaks the surface, then runs off downwards,
            //    slow at first and faster and faster, with a fat bead of water piled up along its trailing edge,
            //    torn into tongues that hang back,
            //  - streams that keep running down where the sheet has gone: broad, soft-sided, meandering, with ripples
            //    flowing inside, thinning out and stopping one by one,
            //  - a thin streaky wet layer behind it all, drying off. No separate drops: they read as cartoon beads.

            // height of the sheet's trailing edge (it drains from the top): gravity, so slow start, then it runs off
            float SheetFall(float since)
            {
                return 1.15 - since * 0.3 - since * since * 0.62;
            }

            float SheetEdge(float x, float since)
            {
                // tongues hang back from the edge, uneven in width and length; they get longer as it drains
                float wobble = LensNoise(float2(x * 2.3, 3.7)) * 0.08 + LensNoise(float2(x * 7.0, 5.3)) * 0.03;
                float n = LensNoise(float2(x * 4.1 + LensNoise(float2(x * 1.7, 1.3)) * 2.0, 9.1));
                float tongues = pow(saturate(n * 1.25 - 0.25), 4.0) * 0.35 * saturate(since * 1.6);
                return SheetFall(since) + wobble + tongues;
            }

            float Sheet(float2 q, float since, float flow)
            {
                float inside = SheetEdge(q.x, since) - q.y;                  // > 0 below the trailing edge
                if (inside < -0.04) return 0.0;
                float body = smoothstep(-0.03, 0.04, inside);
                // water piles up along the edge, more in some places than others
                float bead = exp(-max(inside, 0.0) / 0.045) * (0.15 + 0.3 * LensNoise(float2(q.x * 6.0, 2.0)));

                // lumps in the sheet, sliding down with it and stretched into streaks as it runs
                float warp = LensNoise(q * float2(3.0, 1.2) + float2(0.0, flow));
                float lumps = LensNoise(float2(q.x * 4.0 + warp * 1.5, q.y * (1.8 - 0.8 * saturate(since)) + flow * 1.6));
                float fine = LensNoise(float2(q.x * 11.0 - warp * 2.0, q.y * 3.0 + flow * 2.5));
                float thick = (0.5 + 0.38 * lumps + 0.12 * fine) * saturate(1.0 - since * 0.4);
                // running ripples on its surface: these bend the view the most, a wavering, swimming picture
                float ripples = LensNoise(float2(q.x * 6.0 + warp * 3.0, q.y * 5.0 + flow * 4.0)) * 0.55
                              + LensNoise(float2(q.x * 12.0 - warp * 2.0, q.y * 10.0 + flow * 6.0)) * 0.2;
                return (thick + bead) * body * 0.045 + ripples * body * 0.022 * saturate(1.0 - since * 0.5);
            }

            // One layer of streams: water still running down from where the sheet was, at most one per column of
            // width cellW. Wide and shallow with soft sides (a gaussian cross-section, so nothing reads as a bead or a
            // thread), meandering, with ripples running down inside it. Each one starts behind the draining sheet,
            // flows for a while and then thins out and stops.
            float StreamInColumn(float2 q, float c, float since, float cellW, float seed, float density)
            {
                float3 r = LensHash32(float2(c, seed));
                if (r.x > density) return 0.0;
                r.x /= density;

                float start = 0.1 + 0.5 * r.y;                               // when it is left behind by the sheet
                float tt = since - start;
                if (tt <= 0.0) return 0.0;
                float top = SheetFall(start) + 0.1;                           // where it comes from
                float front = top - tt * (0.25 + 0.35 * r.z) - tt * tt * 0.5; // its leading end runs down
                if (q.y > top || q.y < front - 0.1) return 0.0;

                float flow = since * (1.2 + 0.8 * r.z);
                // the path wanders, and slowly shifts sideways as water finds a new way down
                float cx = (c + 0.5 + 0.3 * (r.y - 0.5)) * cellW
                         + (LensNoise(float2(q.y * 2.5 + r.z * 13.0, c)) - 0.5) * cellW * 0.5
                         + (LensNoise(float2(q.y * 4.0 - flow * 0.3, c + 5.0)) - 0.5) * cellW * 0.06;
                float w = cellW * (0.12 + 0.12 * r.z)                         // width, pulsing as more water comes
                        * (0.75 + 0.5 * LensNoise(float2(q.y * 3.0 + flow * 1.5, c + 9.0)));
                float dx = (q.x - cx) / w;
                float profile = exp(-dx * dx * 2.0);

                // the leading end is a rounded, slightly thicker tongue, not a separate drop
                float ahead = (q.y - front) / 0.05;
                float tip = smoothstep(-1.0, 0.6, ahead) * (1.0 + 0.35 * exp(-ahead * ahead * 2.0));
                // the water running inside it: broad, gentle surges (fine ones turn the picture into stairs)
                float ripple = 0.85 + 0.15 * LensNoise(float2(c * 3.1, q.y * 4.0 + flow * 2.5));
                // thins out and stops after a while, from the top down
                float life = 1.2 + 1.8 * r.x;
                float fade = saturate(1.0 - tt / life) * smoothstep(top, top - 0.08, q.y);

                // shallow: a stream on glass bends the view a few pixels, it is not a lens
                return profile * tip * ripple * fade * w * 0.12;
            }

            // a stream can wander over its column's border: add up the neighbours too, or the borders show as seams
            float Streams(float2 q, float since, float cellW, float seed, float density)
            {
                float c = floor(q.x / cellW);
                return StreamInColumn(q, c - 1.0, since, cellW, seed, density)
                     + StreamInColumn(q, c,       since, cellW, seed, density)
                     + StreamInColumn(q, c + 1.0, since, cellW, seed, density);
            }

            float FilmHeight(float2 q, float since, float wet)
            {
                float flow = since * 0.9;
                float h = Sheet(q, since, flow);
                // a thin streaky wet layer behind the sheet, running down and drying off
                float behind = saturate((q.y - SheetEdge(q.x, since)) / 0.05);
                h += behind * saturate(1.0 - since / 2.5) * 0.003
                   * LensNoise(float2(q.x * 9.0 + LensNoise(q * float2(3.0, 1.0)) * 2.0, q.y * 1.2 + flow * 1.2));
                // streams still running down where the sheet has gone: a broad layer and a finer one
                h += Streams(q, since, 0.16, 17.0, 0.5) + Streams(q + float2(0.05, 0.0), since * 0.95, 0.09, 53.0, 0.3);
                return h * saturate(wet * 1.5);
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

            // Gaussian blur in one pass: taps on a sunflower (Vogel) spiral, weighted by a gaussian of their distance.
            // radius = 2-sigma reach as a share of the screen height.
            half3 SampleGaussian(float2 uv, float radius, float aspect)
            {
                const int Taps = 40;
                half3 sum = 0;
                float weights = 0;
                [unroll]
                for (int i = 0; i < Taps; i++)
                {
                    float r = sqrt((i + 0.5) / Taps);
                    float a = i * 2.39996323;                 // golden angle
                    float2 o = float2(cos(a), sin(a)) * r * radius;
                    o.x /= aspect;
                    float w = exp(-2.0 * r * r);
                    sum += FragBlitSample(uv + o) * w;
                    weights += w;
                }
                return sum / weights;
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
                float film = 0;
                float2 filmOffset = 0;
                float filmDefocus = 0;
                half3 filmTint = 1;
                float wet = _WaterLensWetness * _WaterLensDrops;
                if (wet > 0.001)
                {
                    float since = _WaterLensSinceExit;
                    float h0 = FilmHeight(q, since, wet);                    if (h0 > 1e-5)
                    {
                        const float e = 0.0025;
                        float hx = FilmHeight(q + float2(e, 0), since, wet);
                        float hy = FilmHeight(q + float2(0, e), since, wet);
                        float2 grad = float2(hx - h0, hy - h0) / e;
                        float slope = length(grad);

                        // the slope of the water refracts the view
                        // (while the view is fully out of focus the fine ripples would only add a screen-door pattern)
                        float first = saturate(1.0 - since / 0.5);
                        filmOffset = -grad * (0.035 * (1.0 - 0.6 * first * first) * _WaterLensDistortion);
                        float len = length(filmOffset);
                        if (len > 0.06) filmOffset *= 0.06 / len;
                        offset += filmOffset;

                        // only a faint sheen and a slight darkening where the water is steep: clear running water,
                        // not glossy beads with outlines
                        dark += saturate((slope - 0.5) * 0.5) * 0.12;
                        float3 nrm = normalize(float3(-grad * 1.5, 1.0));
                        float3 H = normalize(normalize(float3(-0.45, 0.7, 0.55)) + float3(0, 0, 1));
                        light += pow(saturate(dot(nrm, H)), 40.0) * 0.08 * saturate(slope * 2.0);

                        film = saturate(h0 * 30.0);
                        filmTint = lerp(half3(1, 1, 1), half3(0.86, 0.95, 1.02), (half)film);   // through water: a touch bluer

                        // Looking through the sheet is out of focus: strongly the instant the head leaves the water (that
                        // hides the jump from the underwater view), clearing as the sheet runs off.
                        float sheet = saturate(Sheet(q, since, since * 0.9) * 25.0);
                        filmDefocus = 0.035 * first * first + 0.006 * sheet;
                    }
                }

                half3 col;
                if (_WakeBlur > 0.001) col = SampleGaussian(uv + offset, _WakeBlur * 0.06, aspect);
                else if (filmDefocus > 0.0015) col = SampleGaussian(uv + offset, filmDefocus, aspect);
                else col = blur > 0.0005 ? SampleBlur(uv + offset, blur) : FragBlitSample(uv + offset);
                if (film > 0.001)
                {
                    // water splits the colours where it bends the view most (more where it is thick)
                    // (only where the view is sharp: sharp colour fringes over a blurred picture look like a glitch)
                    float split = 0.04 + 0.06 * film;
                    half fringe = (half)(film * saturate(1.0 - filmDefocus / 0.008));
                    col.r = lerp(col.r, FragBlitSample(uv + offset + filmOffset * split).r, fringe);
                    col.b = lerp(col.b, FragBlitSample(uv + offset - filmOffset * split).b, fringe);
                }
                col *= 1.0h - saturate(dark);
                col += light;
                col *= filmTint;

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

                // ---- eyelids: an eye-shaped opening, narrower towards the sides, soft-edged lashes line
                float closed = saturate(_EyeClosed);
                if (closed > 0.001)
                {
                    float2 p = (uv - 0.5) * float2(aspect, 1.0);
                    float side = saturate(abs(p.x) / (0.5 * aspect + 0.15));
                    float halfOpen = (1.0 - closed) * 0.85 * (1.0 - 0.55 * side * side);
                    float lid = smoothstep(halfOpen - 0.02, halfOpen + 0.05 + 0.12 * closed, abs(p.y));
                    col *= 1.0h - (half)lid;
                }

                col *= 1.0h - (half)saturate(_ScreenFade);
                return half4(col, 1.0h);
            }

            ENDHLSL
        }
    }

    Fallback Off
}

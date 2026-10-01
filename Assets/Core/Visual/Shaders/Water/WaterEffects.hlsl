#ifndef AKI_WATER_EFFECTS_INCLUDED
#define AKI_WATER_EFFECTS_INCLUDED

#define WATER_IOR 1.333

#if UNITY_REVERSED_Z
    #define WATER_IS_SKY(d) ((d) < 1e-6)
#else
    #define WATER_IS_SKY(d) ((d) > 1.0 - 1e-6)
#endif

// Procedural texture made by WaterTextures.cs: R = fine light-shaft filaments, G = broad swells, B = sharp caustics.
TEXTURE2D(_WaterRayTex);
SAMPLER(sampler_WaterRayTex);

// ---------------------------------------------------------------- noise
float WaterHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float WaterValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = WaterHash21(i);
    float b = WaterHash21(i + float2(1, 0));
    float c = WaterHash21(i + float2(0, 1));
    float d = WaterHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Interleaved gradient noise: cheap dither used to jitter the ray-march start.
float WaterDither(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Smooth cellular noise: a soft blend of per-cell random values, so there are no hard cell borders at all.
// Returns two random-ish values in 0..1 that vary smoothly and slowly wander with tAnim.
float2 WaterSoftCells(float2 p, float tAnim)
{
    float2 ip = floor(p);
    float2 fp = p - ip;
    float ws = 0;
    float2 rs = 0;

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 h = WaterHash22(ip + g);
            float2 d = g + 0.5 + 0.45 * sin(tAnim + TWO_PI * h) - fp;
            float w = exp(-4.0 * dot(d, d));
            ws += w;
            rs += w * h;
        }
    }
    return rs / max(ws, 1e-4);
}

// ---------------------------------------------------------------- cartoon helpers
// Quantises x (0..1) into `bands` flat steps with a soft edge, so gradients become cel-shaded layers.
float WaterToonQuant(float x, float bands, float soft)
{
    float v = saturate(x) * bands;
    float i = floor(v);
    return (i + smoothstep(0.5 - soft, 0.5 + soft, v - i)) / bands;
}

half3 WaterToonPosterize(half3 c, float bands, float soft)
{
    return half3(WaterToonQuant(c.r, bands, soft), WaterToonQuant(c.g, bands, soft), WaterToonQuant(c.b, bands, soft));
}

// ---------------------------------------------------------------- shadows
half WaterMainLightShadow(float3 positionWS)
{
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
    return MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
#else
    return 1.0h;
#endif
}

// ---------------------------------------------------------------- light under the surface
// Sunlight bends towards the vertical when it enters the water (Snell's law at a flat surface).
// Returns the direction *towards* the sun, as it is seen from below the surface.
float3 WaterUnderLight(float3 toLight)
{
    return -refract(-toLight, float3(0, 1, 0), 1.0 / WATER_IOR);
}

// Light that reaches the water body: sky ambient + sun.
half3 WaterScatterLight(half3 ambient, float3 toLight, half3 lightColor)
{
    return (ambient + lightColor * (0.35 + 0.65 * saturate(toLight.y))) * _ScatterBrightness;
}

// Colour of the water volume itself as seen by something `depth` metres below the surface.
// bias shifts the shallow<->deep mix (used by the large-scale Voronoi colour variation).
half3 WaterBodyColorB(float depth, float bias, half3 scatterLight)
{
    float m = saturate(1.0 - exp(-depth / max(_DepthDistance, 1e-3)) + bias);
    m = lerp(m, WaterToonQuant(m, _ToonBands, _ToonSoftness), _ToonAmount);   // cel-shaded depth layers
    return lerp(_ShallowColor.rgb, _DeepColor.rgb, m) * scatterLight;
}

half3 WaterBodyColor(float depth, half3 scatterLight)
{
    return WaterBodyColorB(depth, 0.0, scatterLight);
}

// Henyey-Greenstein phase, normalised so an isotropic medium gives 1.
float WaterPhaseHG(float cosT, float g)
{
    return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosT, 1e-3), 1.5);
}

// Colour of the water *volume* along a view ray while the camera is under the surface: the light scattered
// towards the eye. Bright blue near the surface, darker and deeper blue further down, brighter when looking up
// and glowing towards the sun (forward scattering).
half3 WaterUnderFogColor(float camDepth, float endDepth, float3 viewDir, float3 toLight, half3 scatterLight)
{
    float d = 0.5 * (camDepth + endDepth);
    half3 c = lerp(_UnderFogColor.rgb, _UnderDeepColor.rgb, 1.0 - exp(-d * _UnderDepthFalloff));
    float up = saturate(viewDir.y * 0.5 + 0.5);
    c *= lerp(0.55, 1.3, up * up);
    float hg = min(WaterPhaseHG(dot(WaterUnderLight(toLight), viewDir), 0.7), 5.0);
    c *= 0.8 + 0.25 * hg * saturate(toLight.y * 3.0);
    return c * scatterLight * 0.75;
}

// Underwater volume: same idea but with very soft band edges, so distance haze fades smoothly instead of in layers.
float3 WaterTransmittanceSoft(float dist, float densityScale)
{
    float3 T = exp(-_Absorption.rgb * _Turbidity * densityScale * dist);
    float fogK = 1.0 - exp(-_Absorption.g * 3.0 * _Turbidity * densityScale * dist);
    float fogQ = lerp(fogK, WaterToonQuant(fogK, _ToonBands, 0.32), 0.5);
    return lerp(T, (1.0 - fogQ).xxx, _ToonAmount * 0.85);
}

// Fraction of light that survives `dist` metres of water. Physically per channel (red dies first);
// in cartoon mode it is turned into flat, evenly coloured fog layers.
float3 WaterTransmittance(float dist, float densityScale)
{
    float3 T = exp(-_Absorption.rgb * _Turbidity * densityScale * dist);
    // cartoon water is denser than real water: flat fog layers that hide the bottom quickly
    float fogK = 1.0 - exp(-_Absorption.g * 3.0 * _Turbidity * densityScale * dist);
    float fogQ = WaterToonQuant(fogK, _ToonBands, _ToonSoftness);
    return lerp(T, (1.0 - fogQ).xxx, _ToonAmount * 0.85);
}

// ---------------------------------------------------------------- caustics
// Web of bright filaments made from domain-warped ridged sines.
// p is a position on the water plane (where the light entered), t is time.
float WaterCaustics(float2 p, float t)
{
    float2 q = p * 2.0;
    float net = 0.0;
    float prod = 1.0;

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float ph = t * (0.8 + 0.31 * i);
        q += 0.6 * float2(sin(q.y * 1.3 + ph), cos(q.x * 1.1 - ph * 0.83));
        float2 dir = float2(cos(i * 2.094 + 0.4), sin(i * 2.094 + 0.4));
        float r = 1.0 - abs(sin(dot(q, dir) * 1.4 + ph * 0.5));
        r = r * r * r;
        net += r;
        prod *= (0.35 + r);
    }
    return saturate(net * 0.55 + prod * 1.1 - 0.15);
}

// Sharp caustic web from the texture: two layers panning in different directions, min() of both gives the
// familiar dancing network of bright lines.
float WaterCausticTex(float2 p, float t)
{
    float2 uv1 = p * 0.13 + float2(0.021, 0.013) * t;
    float2 uv2 = float2(-p.y, p.x) * 0.11 + float2(-0.016, 0.022) * t + 0.37;
    float a = SAMPLE_TEXTURE2D_LOD(_WaterRayTex, sampler_WaterRayTex, uv1, 0).b;
    float b = SAMPLE_TEXTURE2D_LOD(_WaterRayTex, sampler_WaterRayTex, uv2, 0).b;
    return min(a, b);
}

// Adds sun caustics to something lit at bottomWS (colour bottomColor), water surface at waterLevel.
// Also dims objects by the water the sunlight crossed to reach them (deep things are darker and bluer).
half3 WaterApplyCaustics(half3 bottomColor, float3 bottomWS, float waterLevel, float3 toLight, half3 lightColor, float t)
{
    float depthV = max(waterLevel - bottomWS.y, 0.0);
    float3 Lw = WaterUnderLight(toLight);
    float travel = depthV / max(Lw.y, 0.05);
    float2 hitXZ = bottomWS.xz + Lw.xz * travel;

    float2 p = hitXZ * _CausticsScale * 1.2;
    float tt = t * _CausticsSpeed;
    float2 split = Lw.xz * (_CausticsDispersion * 0.12) + float2(0.05, 0.03) * _CausticsDispersion;
    float3 caus = float3(WaterCausticTex(p + split, tt), WaterCausticTex(p, tt), WaterCausticTex(p - split, tt));
    caus = caus * caus * 3.0;

    float3 sunT = exp(-_Absorption.rgb * _Turbidity * travel);
    float fade = saturate(1.0 - depthV / _CausticsMaxDepth);
    half sh = WaterMainLightShadow(bottomWS);
    half3 lit = bottomColor * exp(-_Absorption.rgb * _Turbidity * travel * _UnderLightAbsorb);
    return lit + bottomColor * lightColor * sunT * caus * (sh * fade * _CausticsIntensity * saturate(toLight.y * 4.0));
}

// ---------------------------------------------------------------- foam
// shore: 0..1 closeness to the shoreline / intersecting objects, crest: 0..1 wave pinching.
float WaterFoam(float2 xz, float t, float shore, float crest, float lace)
{
    float2 p = xz * _FoamScale;
    float tex = WaterValueNoise(p + float2(t * 0.08, -t * 0.05)) * 0.6
              + WaterValueNoise(p * 2.7 - float2(t * 0.11, t * 0.07)) * 0.4;
    tex = saturate(tex * 0.75 + lace * 0.45);                     // foam clings to the Voronoi cell borders

    // "dissolve" threshold: high coverage -> low threshold, so foam breaks up into lace at its border.
    float cover = saturate(max(shore, crest));
    float soft = lerp(0.12, 0.025, _ToonAmount);                 // hard-edged blobs in cartoon mode
    return smoothstep(1.0 - cover - soft, 1.0 - cover + soft, tex) * step(0.001, cover);
}

// FFT ocean foam: grows smoothly with how hard the crest is breaking (turbulence from OceanFFT, which also leaves
// trails behind moving crests). Dense in the middle of a breaking patch, broken lace towards its edges.
float OceanFoam(float2 xz, float t, float turbulence, out float haze)
{
    float amount = saturate((_CrestFoamThreshold - turbulence) * 7.0);
    haze = saturate((_CrestFoamThreshold + 0.08 - turbulence) * 5.0);      // milky water a bit beyond the foam

    // lace stretched along the wind: dense white where the crest breaks hardest, torn streaks around it
    float2 wind;
    sincos(radians(_WindAngle), wind.y, wind.x);
    float2 q = float2(dot(xz, wind) * 0.45, dot(xz, float2(-wind.y, wind.x))) * _FoamScale;
    float lace = WaterValueNoise(q * 1.3 + float2(t * 0.05, 0.0)) * 0.5
               + WaterValueNoise(q * 3.7 - t * 0.04) * 0.3
               + WaterValueNoise(q * 9.0 + 4.0) * 0.2;
    // never fully solid: even the heart of a breaking crest has holes and thinner areas
    float cover = saturate(sqrt(amount) * 1.3) * 0.82;
    float f = smoothstep(1.0 - cover, 1.0 - cover + 0.22, lace);
    return f * (0.55 + 0.45 * saturate(lace * 1.4)) * (0.6 + 0.4 * amount);
}

// ---------------------------------------------------------------- volumetric light shafts
// How much sunlight enters the surface at xz. Waves focus the sun into a web of bright filaments; because
// every sun ray is a straight line, this 2D field is extruded down along the sun direction into sheets
// of light. Two scales (fine filaments + broad swells) give many thin, soft, overlapping shafts.
// PERFORMANCE: the field is a small tileable texture (generated once by WaterSurface, R = fine, G = broad),
// so a march step costs two texture fetches instead of dozens of sin/cos.

float WaterRayField(float2 xz, float t, float lod)
{
    float2 uv1 = xz * (_RayScale * 0.5) + float2(t * 0.013, t * 0.009) * _CausticsSpeed;
    float2 uv2 = xz * (_RayScale * 0.18) + float2(0.37 - t * 0.006 * _CausticsSpeed, 0.11 + t * 0.008 * _CausticsSpeed);
    float fine  = SAMPLE_TEXTURE2D_LOD(_WaterRayTex, sampler_WaterRayTex, uv1, lod).r;
    float broad = SAMPLE_TEXTURE2D_LOD(_WaterRayTex, sampler_WaterRayTex, uv2, max(lod - 1.5, 0.0)).g;
    return saturate(fine * 0.45 + broad * 0.55);
}

// Marches from startWS towards endWS (a view ray through the water). At every step the *sun* ray through
// that point is followed back up to the surface (where it entered, bent by refraction); the light it carries
// is the field above, attenuated by the water it travelled through (blue/green survives, red does not) and
// cut by the shadow map (rocks and boats really do break the beams). Forward scattering makes the shafts
// glow when you look towards the sun.
half3 WaterGodRays(float3 startWS, float3 endWS, float2 pixel, float3 toLight, half3 lightColor,
                   float waterLevel, float t)
{
    float3 seg = endWS - startWS;
    float len = length(seg);
    float3 dir = seg / max(len, 1e-4);
    len = min(len, _RayLength);
    // a ray that leaves the water through the surface stops there
    if (dir.y > 1e-3 && startWS.y < waterLevel)
        len = min(len, (waterLevel - startWS.y) / dir.y);

    float3 Lw = WaterUnderLight(toLight);
    float3 absorb = _Absorption.rgb * _Turbidity;

    int steps = (int)clamp(_RaySteps, 2.0, 32.0);
    float dt = len / steps;
    float jitter = WaterDither(pixel);
    // pre-filter the pattern by the step length (mip level), so thin filaments can't flicker between steps -> no grain
    float lod = clamp(log2(max(dt * _RayScale * 0.5 * 256.0, 1.0)) - 1.0, 0.0, 6.0);

    // Henyey-Greenstein forward scattering (normalised so an isotropic medium would be 1)
    float g = _RayPhase;
    float cosT = dot(Lw, dir);
    float phase = (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosT, 1e-3), 1.5);

    float3 sum = 0;
    [loop]
    for (int k = 0; k < steps; k++)
    {
        float s = (k + jitter) * dt;
        float3 p = startWS + dir * s;

        float travel = max(waterLevel - p.y, 0.0) / max(Lw.y, 0.05);   // distance the sun ray spent in water
        float2 hitXZ = p.xz + Lw.xz * travel;                           // where it entered the surface

        float field = pow(WaterRayField(hitXZ, t, lod), _RayContrast);
        float3 Tsun = exp(-absorb * travel);
        float  Tview = exp(-s * _RayFade);
        float  depthFade = exp(-max(waterLevel - p.y, 0.0) * _RayDepthFade);   // shafts die out with depth
        float  sh = WaterMainLightShadow(p);

        sum += field * sh * Tsun * Tview * depthFade;
    }

    sum *= dt * phase * _RayIntensity * 0.12;   // 0.12 ~ in-scattering coefficient per metre
    sum = sum / (1.0 + sum * 0.7);              // soft shoulder: looking into the sun glows instead of blowing out
    return (half3)(sum * lightColor * _RayColor.rgb * saturate(toLight.y * 4.0));
}

#endif

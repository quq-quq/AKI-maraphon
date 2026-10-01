#ifndef AKI_WATER_WAVES_INCLUDED
#define AKI_WATER_WAVES_INCLUDED

// Procedural Gerstner sea. A handful of sliders drives a whole wave spectrum:
//   wave i has wavelength  L0 * LACUNARITY^i,  amplitude  A0 * GAIN^i  and a direction around the wind.
// Sine sums alone look regular, so the "behaviour" of the sea is driven by slowly varying fields on top:
//   1. _WaveRandomness: every wave gets its own random wavelength / amplitude / direction and its
//      strength drifts in patches across the sea and over time,
//   2. Voronoi phase warp (_WaveWarp): bends and shears the wave fronts,
//   3. Voronoi wave field (_CellWaveHeight): independent cells rise and fall on their own schedules
//      (real geometry), and every wave is stronger or weaker depending on the cell it is in,
//   4. small Voronoi cell ripples (pixel only) and lace foam along the cell borders.
// Speed follows the deep-water dispersion relation w = sqrt(g * k): big swells outrun ripples.
//
// PERFORMANCE: all of those fields are low-frequency, so they are evaluated once per *vertex* and
// interpolated to the pixels (WaterWaveField -> pack/unpack). The pixel shader only sums a few sines.

#define WATER_BASE_WAVES    6
#define WATER_DETAIL_WAVES  6
#define WATER_LACUNARITY    0.72
#define WATER_GAIN          0.64
#define WATER_GRAVITY       9.81

// Distance between mesh vertices (m); set globally by WaterSurface. Waves shorter than a few cells cannot be
// represented by the grid (they alias into spikes), so they only shade the surface instead of moving vertices.
float _WaterMeshCell;
float _WaterMeshGrowth;     // expanding grid: cell size grows by 1 + distance / growth
float4 _WaterMeshCenter;

// ------------------------------------------------------------------------------------------------
// FFT ocean (OceanFFT.cs): three cascades of displacement + derivatives, published as global textures.
#if defined(_FFT_WAVES)
TEXTURE2D(_OceanDisp0);
TEXTURE2D(_OceanDisp1);
TEXTURE2D(_OceanDisp2);
TEXTURE2D(_OceanDeriv0);
TEXTURE2D(_OceanDeriv1);
TEXTURE2D(_OceanDeriv2);
SamplerState ocean_trilinear_repeat_sampler;
float4 _OceanLengthScales;

// mip level whose texel matches the local mesh cell, so vertices never sample waves the grid can't show
float OceanLod(float lengthScale, float cell)
{
    return max(0.0, log2(cell * 256.0 / lengthScale));
}

float OceanCellAt(float2 xz)
{
    return max(_WaterMeshCell, 1e-3) * (1.0 + length(xz - _WaterMeshCenter.xz) / max(_WaterMeshGrowth, 1e-3));
}

// displacement (xyz, metres) of the undisplaced point xz; mesh-matched mip levels
float3 OceanDisplacement(float2 xz)
{
    float cell = OceanCellAt(xz);
    float3 d = SAMPLE_TEXTURE2D_LOD(_OceanDisp0, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.x, OceanLod(_OceanLengthScales.x, cell)).xyz;
    d += SAMPLE_TEXTURE2D_LOD(_OceanDisp1, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.y, OceanLod(_OceanLengthScales.y, cell)).xyz;
    d += SAMPLE_TEXTURE2D_LOD(_OceanDisp2, ocean_trilinear_repeat_sampler, xz / _OceanLengthScales.z, OceanLod(_OceanLengthScales.z, cell)).xyz;
    return d;
}

// per pixel: normal from the summed slopes, and "turbulence" (low where crests break -> foam)
void OceanSurface(float2 xz, out float3 normal, out float turbulence)
{
    float2 uv0 = xz / _OceanLengthScales.x;
    float2 uv1 = xz / _OceanLengthScales.y;
    float2 uv2 = xz / _OceanLengthScales.z;
    float4 deriv = SAMPLE_TEXTURE2D(_OceanDeriv0, ocean_trilinear_repeat_sampler, uv0)
                 + SAMPLE_TEXTURE2D(_OceanDeriv1, ocean_trilinear_repeat_sampler, uv1)
                 + SAMPLE_TEXTURE2D(_OceanDeriv2, ocean_trilinear_repeat_sampler, uv2);
    float2 slope = float2(deriv.x / (1.0 + deriv.z), deriv.y / (1.0 + deriv.w));
    normal = normalize(float3(-slope.x, 1.0, -slope.y));

    float t0 = SAMPLE_TEXTURE2D(_OceanDisp0, ocean_trilinear_repeat_sampler, uv0).w;
    float t1 = SAMPLE_TEXTURE2D(_OceanDisp1, ocean_trilinear_repeat_sampler, uv1).w;
    turbulence = min(t0, lerp(1.0, t1, 0.6));
}

// same as OceanSurface with an explicit mip level (compute shaders / vertex stage)
void OceanSurfaceLod(float2 xz, float lod, out float3 normal, out float turbulence)
{
    float2 uv0 = xz / _OceanLengthScales.x;
    float2 uv1 = xz / _OceanLengthScales.y;
    float2 uv2 = xz / _OceanLengthScales.z;
    float4 deriv = SAMPLE_TEXTURE2D_LOD(_OceanDeriv0, ocean_trilinear_repeat_sampler, uv0, lod)
                 + SAMPLE_TEXTURE2D_LOD(_OceanDeriv1, ocean_trilinear_repeat_sampler, uv1, lod)
                 + SAMPLE_TEXTURE2D_LOD(_OceanDeriv2, ocean_trilinear_repeat_sampler, uv2, lod);
    float2 slope = float2(deriv.x / (1.0 + deriv.z), deriv.y / (1.0 + deriv.w));
    normal = normalize(float3(-slope.x, 1.0, -slope.y));
    turbulence = SAMPLE_TEXTURE2D_LOD(_OceanDisp0, ocean_trilinear_repeat_sampler, uv0, lod).w;
}
#endif

float WaterRand(float n)
{
    return frac(sin(n * 12.9898 + _WaveSeed * 78.233) * 43758.5453);
}

float2 WaterHash22(float2 p)
{
    p += _WaveSeed * 13.7;
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.xx + q.yz) * q.zy);
}

// Animated Voronoi (Worley) noise in cell units.
//   returns x = F1 (distance to the nearest feature point), y = F2 - F1 (~ distance to the cell border)
//   grad    = dF1/dp, a unit vector pointing away from the nearest feature point
//   cellRnd = random number of the nearest cell (0..1)
float2 WaterVoronoi(float2 p, float tAnim, out float2 grad, out float cellRnd)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    float f1 = 8.0, f2 = 8.0;
    float2 v1 = 0;
    float r1 = 0;

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 h = WaterHash22(ip + g);
            float2 o = 0.5 + 0.45 * sin(tAnim + TWO_PI * h);   // feature points wander slowly
            float2 r = g + o - fp;
            float d = dot(r, r);
            if (d < f1) { f2 = f1; f1 = d; v1 = r; r1 = h.x; }
            else if (d < f2) { f2 = d; }
        }
    }

    float d1 = sqrt(f1);
    grad = -v1 / max(d1, 1e-4);
    cellRnd = r1;
    return float2(d1, sqrt(f2) - d1);
}

// Voronoi wave field. Every cell owns a smooth bump that wanders around and rises and falls on its OWN
// schedule (random period, phase and size). Bumps overlap and add up, so the sea gets swells and troughs that
// are not copies of each other. Continuous everywhere (compact kernels, no nearest-cell switching).
//   h      height in metres,  slope  dh/dxz,  rnd  smoothly varying random values
void WaterCellField(float2 xz, float t, out float h, out float2 slope, out float4 rnd)
{
    float2 p = xz * _CellWaveScale;
    float2 ip = floor(p);
    float2 fp = p - ip;

    float hs = 0;
    float2 dh = 0;
    float ws = 0;
    float4 rs = 0;

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 h2 = WaterHash22(ip + g);
            float2 h3 = WaterHash22(ip + g + 41.7);
            float2 c = g + 0.5 + 0.4 * sin(t * (0.15 + 0.25 * h3.x) + TWO_PI * h2);   // wandering centre
            float2 d = fp - c;
            float w = saturate(1.0 - dot(d, d));                                     // kernel radius = 1 cell
            float w2 = w * w;

            float a = sin(t * _CellWaveSpeed * (0.5 + h3.y) + TWO_PI * h3.x) * (0.4 + 0.6 * h2.y);
            hs += a * w2;
            dh += a * (-4.0 * w) * d;
            ws += w2;
            rs += w2 * float4(h2, h3);
        }
    }

    h = hs * _CellWaveHeight;
    slope = dh * (_CellWaveHeight * _CellWaveScale);
    rnd = (rs + 0.5 * 0.05) / (ws + 0.05);   // smooth even where no bump reaches
}

// Strength multiplier (0.25..1) of wave i inside the current region.
float WaterCellMod(int i, float4 rnd)
{
    float rv = rnd[i & 3];
    float m = 0.25 + 0.75 * (0.5 + 0.5 * sin(rv * TWO_PI * (1.0 + 0.5 * (i >> 2)) + i));
    return lerp(1.0, m, saturate(_WaveRandomness));
}

// Per-wave constants (no position / time dependence).
void WaterWaveConst(int i, out float2 dir, out float k, out float A, out float w)
{
    float jit = _WaveRandomness;
    float r0 = WaterRand(i + 1.0);
    float r1 = WaterRand(i + 17.0);
    float r2 = WaterRand(i + 41.0);

    float L = _WaveLength * pow(WATER_LACUNARITY, i) * (1.0 + (r0 - 0.5) * 0.8 * jit);
    k = TWO_PI / L;
    A = _WaveAmplitude * pow(WATER_GAIN, i) * (1.0 + (r2 - 0.5) * 0.9 * jit);
    w = sqrt(WATER_GRAVITY * k) * _WaveSpeed;

    float spread = (i < WATER_BASE_WAVES) ? _WaveSpread : saturate(_WaveSpread + 0.35);
    float golden = frac(i * 0.61803399 + 0.5) * 2.0 - 1.0;
    float off = lerp(golden, r1 * 2.0 - 1.0, jit * 0.75);
    sincos(radians(_WindAngle) + off * spread * PI, dir.y, dir.x);
}

// Slowly drifting patches of strength for wave i, in (0.25 .. 1].
float WaterPatch(int i, float2 xz, float t)
{
    float r0 = WaterRand(i + 73.0);
    float r1 = WaterRand(i + 91.0);
    float r2 = WaterRand(i + 113.0);

    float2 d1;
    sincos(r0 * TWO_PI, d1.y, d1.x);
    float2 d2 = float2(-d1.y, d1.x);

    float a = sin(dot(xz, d1) * (0.018 + 0.03 * r1) + t * (0.10 + 0.20 * r2) + r2 * TWO_PI);
    float b = sin(dot(xz, d2) * (0.020 + 0.03 * r2) - t * (0.08 + 0.15 * r1) + r0 * TWO_PI);
    float p = 0.5 + 0.5 * a * b;
    return lerp(1.0, 0.25 + 0.75 * p, saturate(_WaveRandomness));
}

// Large-scale amplitude breathing shared by every wave.  Normalised to (0..1].
float WaterSwellMask(float2 xz, float t)
{
    float m = 1.0 + _WaveVariation * sin(xz.x * 0.037 + t * 0.11) * sin(xz.y * 0.043 - t * 0.09);
    return m / (1.0 + _WaveVariation);
}

// Sum over base waves of A * |grad phase|. Choosing Q = steepness / sum makes the horizontal squeeze of the
// surface at most `steepness` <= 1, which is exactly the "no self-intersecting loops" condition.
// |grad phase| is k plus the worst case of the Voronoi warp gradient (wi <= 1.5), so raising the warp
// can never make the crests fold over themselves. Constant per material.
float WaterQ()
{
    float s = 0;
    [unroll]
    for (int i = 0; i < WATER_BASE_WAVES; i++)
    {
        float2 d; float k, A, w;
        WaterWaveConst(i, d, k, A, w);
        s += A * (k + 1.5 * _WaveWarp * _WarpScale);
    }
    return saturate(_WaveSteepness) / max(s, 1e-4);
}

// ------------------------------------------------------------------------------------------------
// Low-frequency fields: evaluated per vertex, interpolated to pixels.
struct WaterWaveField
{
    float  warp;
    float2 warpGrad;
    float  cellH;
    float2 cellSlope;
    float  swell;
    float  mods[WATER_BASE_WAVES];     // amplitude multiplier of every base wave at this spot
};

WaterWaveField WaterEvalField(float2 xz, float t)
{
    WaterWaveField f;

    float2 g; float rr;
    float2 v = WaterVoronoi(xz * _WarpScale, t * 0.12, g, rr);
    f.warp = v.x * _WaveWarp;
    f.warpGrad = g * (_WarpScale * _WaveWarp);

    float4 cellRnd;
    WaterCellField(xz, t, f.cellH, f.cellSlope, cellRnd);

    f.swell = WaterSwellMask(xz, t);
    [unroll]
    for (int i = 0; i < WATER_BASE_WAVES; i++)
        f.mods[i] = f.swell * WaterPatch(i, xz, t) * WaterCellMod(i, cellRnd);

    return f;
}

// 0 when the grid is too coarse for a wave of this wavelength, 1 when it is fine enough.
float WaterMeshFade(float wavelength)
{
    return saturate(wavelength / max(_WaterMeshCell, 1e-3) * (1.0 / 3.0) - 1.0);
}

// Pack into 3 float4 interpolators and back.
void WaterPackField(WaterWaveField f, out float4 a, out float4 b, out float4 c)
{
    a = float4(f.warp, f.warpGrad, f.swell);
    b = float4(f.cellSlope, f.mods[0], f.mods[1]);
    c = float4(f.mods[2], f.mods[3], f.mods[4], f.mods[5]);
}

WaterWaveField WaterUnpackField(float4 a, float4 b, float4 c)
{
    WaterWaveField f;
    f.warp = a.x; f.warpGrad = a.yz; f.swell = a.w;
    f.cellSlope = b.xy; f.cellH = 0;
    f.mods[0] = b.z; f.mods[1] = b.w;
    f.mods[2] = c.x; f.mods[3] = c.y; f.mods[4] = c.z; f.mods[5] = c.w;
    return f;
}

// Vertex displacement of the undisplaced world position xz.
float3 WaterDisplacementF(WaterWaveField f, float2 xz, float t, float q)
{
    float3 d = 0;

    [unroll]
    for (int i = 0; i < WATER_BASE_WAVES; i++)
    {
        float2 dir; float k, A, w;
        WaterWaveConst(i, dir, k, A, w);
        A *= f.mods[i] * WaterMeshFade(TWO_PI / k);
        float wi = 0.5 + WaterRand(i + 200.0);      // every wave is warped by a different amount

        float s, c;
        sincos(k * dot(dir, xz) - w * t + f.warp * wi, s, c);

        d.xz += dir * (q * A * c);
        d.y  += A * s;
    }
    d.y += f.cellH * WaterMeshFade(1.0 / _CellWaveScale);
    return d;
}

float3 WaterDisplacement(float2 xz, float t, float q)
{
#if defined(_FFT_WAVES)
    return OceanDisplacement(xz);
#else
    return WaterDisplacementF(WaterEvalField(xz, t), xz, t, q);
#endif
}

// ------------------------------------------------------------------------------------------------
// Waterline helpers: is the start of a view ray (a point on the camera's near plane) under the wavy surface?
// Used by the surface, the underwater overlay and the lens effect, so all three agree on where the split is.

// Height band (m) around the mean level inside which the camera may be partly submerged.
float WaterSurfaceBand()
{
    return _WaveAmplitude * 2.0 + _CellWaveHeight + 0.4;
}

float3 WaterNearPoint(float3 cam, float3 dir, float3 camFwd, float nearDist)
{
    return cam + dir * (nearDist / max(dot(dir, camFwd), 1e-3));
}

// Metres the point lies below the wavy surface (negative = above). Ignores the small horizontal Gerstner shift.
float WaterSubmergedDist(float3 p, float t, float waterLevel)
{
    return waterLevel + WaterDisplacement(p.xz, t, WaterQ()).y - p.y;
}

// Analytic normal + Jacobian of the horizontal Gerstner mapping (jacobian < ~0.6 means the
// surface is pinching together -> wave crest about to break -> foam).
// lace: 0..1, high along Voronoi cell borders (used to draw lace foam).
void WaterNormal(WaterWaveField f, float2 xz, float t, float dist, float q, out float3 normal, out float jacobian, out float lace)
{
    float2 slope = f.cellSlope * WaterMeshFade(1.0 / _CellWaveScale);   // gradient of the height field
    float  ys = 0;          // sum Q*k*A*sin
    float  jxx = 1, jzz = 1, jxz = 0;

    [unroll]
    for (int i = 0; i < WATER_BASE_WAVES; i++)
    {
        float2 dir; float k, A, w;
        WaterWaveConst(i, dir, k, A, w);
        A *= f.mods[i];
        float wi = 0.5 + WaterRand(i + 200.0);

        float s, c;
        sincos(k * dot(dir, xz) - w * t + f.warp * wi, s, c);

        // d(height)/dxz = A*cos(phase) * grad(phase); the warp adds its own gradient to the phase
        slope += (A * c) * (k * dir + wi * f.warpGrad);
        float kA = k * A;
        ys += q * kA * s;

        float qs = q * kA * s;
        jxx -= qs * dir.x * dir.x;
        jzz -= qs * dir.y * dir.y;
        jxz -= qs * dir.x * dir.y;
    }

    lace = 0.0;
#if defined(_WAVE_DETAIL)
    [unroll]
    for (int j = WATER_BASE_WAVES; j < WATER_BASE_WAVES + WATER_DETAIL_WAVES; j++)
    {
        float2 dir; float k, A, w;
        WaterWaveConst(j, dir, k, A, w);
        float fade = saturate(1.0 - dist / ((TWO_PI / k) * _DetailFade));   // small waves vanish with distance
        slope += dir * (k * A * cos(k * dot(dir, xz) - w * t) * fade * _DetailStrength * (0.5 + 0.5 * f.swell));
    }

    // Voronoi cell ripples: small wandering domes; their borders become creases (and lace foam).
    // Only evaluated close to the camera - the only place where they are big enough to see.
    float cellFade = saturate(1.0 - dist * _CellScale / (_DetailFade * 0.5));
    if (cellFade > 0.0 && _CellRipples > 0.0)
    {
        float2 cg; float cr;
        float2 vc = WaterVoronoi(xz * _CellScale, t * 0.35, cg, cr);
        float x = saturate(vc.x / 0.7);
        float ds = 6.0 * x * (1.0 - x) / 0.7;                     // derivative of smoothstep(0, 0.7, F1)
        slope -= cg * (ds * 0.12 * _CellRipples * _CellScale * cellFade);
        lace = (1.0 - smoothstep(0.0, 0.16, vc.y)) * cellFade;
    }
#endif

    normal = normalize(float3(-slope.x, 1.0 - ys, -slope.y));
    jacobian = jxx * jzz - jxz * jxz;
}

#endif

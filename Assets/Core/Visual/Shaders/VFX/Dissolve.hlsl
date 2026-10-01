#ifndef AKI_DISSOLVE_INCLUDED
#define AKI_DISSOLVE_INCLUDED

// Noise dissolve shared by every pass of AKI/DissolveLit. Not in UnityPerMaterial on purpose: the forward pass reuses
// URP's own Lit input, so these sit next to it (the shader isn't SRP-batched; it is only used while something melts).
float _Dissolve;           // 0 = whole, 1 = gone
float _DissolveScale;      // noise cells per metre
float _DissolveEdge;       // width of the glowing rim, in noise units
half4 _DissolveEdgeColor;

float DissolveHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float DissolveValueNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(DissolveHash(i + float3(0, 0, 0)), DissolveHash(i + float3(1, 0, 0)), f.x),
                     lerp(DissolveHash(i + float3(0, 1, 0)), DissolveHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(DissolveHash(i + float3(0, 0, 1)), DissolveHash(i + float3(1, 0, 1)), f.x),
                     lerp(DissolveHash(i + float3(0, 1, 1)), DissolveHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// Object-space position in metres (object scale applied, so the pattern has the same size on every object and
// sticks to it while it moves and turns).
float3 DissolvePosition(float3 positionOS)
{
    float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
    return positionOS * scale;
}

// Cuts away the dissolved part; returns how much of the glowing rim this pixel is (0..1).
half DissolveClip(float3 positionOS)
{
    if (_Dissolve <= 0.0) return 0.0h;
    float3 p = DissolvePosition(positionOS) * _DissolveScale;
    float n = 0.55 * DissolveValueNoise(p) + 0.3 * DissolveValueNoise(p * 2.17 + 17.1) + 0.15 * DissolveValueNoise(p * 4.63 + 31.7);
    n = saturate((n - 0.2) / 0.6);                        // spread the fbm's bunched-up values over 0..1
    float threshold = lerp(-_DissolveEdge, 1.0, _Dissolve);
    float d = n - threshold;
    clip(d);
    return (half)(1.0 - saturate(d / max(_DissolveEdge, 1e-4)));
}

#endif

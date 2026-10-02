#ifndef BOTTOM_STOCHASTIC_INPUT_INCLUDED
#define BOTTOM_STOCHASTIC_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// Identical layout in all passes and keyword variants, for SRP batching.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
half4 _BaseColor;
float _BumpScale;
float _Smoothness;
float _Metallic;
float _Parallax;
float _Surface;
float _Cutoff;
float _RandomizeUV;
float _UseParallax;
float _RandomOffsetStrength;
float _RandomRotationStrength;
float _RotationStepDeg;
float _RandomSeed;
float _TileSize;
float _BlendSharpness;
float _HeightBlend;
// Shared with CoralSmartTerrain; one constant-buffer layout across its passes.
float _GroundTileMeters;
float _CliffTileMeters;
float _CliffNormalStrength;
float _SlopeStart;
float _SlopeEnd;
float _ElevationStart;
float _ElevationEnd;
float _ElevationInfluence;
float _TransitionNoise;
float _ReefTerrain;
float _CliffOnly;
float _TerrainDetailStart;
float _TerrainDetailEnd;
half4 _CliffTint;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_ParallaxMap); SAMPLER(sampler_ParallaxMap);

struct BottomPatch
{
    float2 uv;
    float2 dx;
    float2 dy;
    float2 rotation;
};

float3 BottomHash(float2 id)
{
    float3 h = float3(dot(id, float2(127.1, 311.7)),
                      dot(id, float2(269.5, 183.3)),
                      dot(id, float2(419.2, 371.9)));
    return frac(sin(h + _RandomSeed * float3(17.31, 29.17, 43.73)) * 43758.5453);
}

float2 BottomRotate(float2 v, float2 cs)
{
    return float2(cs.x * v.x - cs.y * v.y, cs.y * v.x + cs.x * v.y);
}

BottomPatch BottomMakePatch(float2 uv, float2 dx, float2 dy, float2 id)
{
    float3 random = BottomHash(id);
    float step = max(_RotationStepDeg, 1.0);
    float angle = floor(random.z * (360.0 / step)) * step * (PI / 180.0) * _RandomRotationStrength;
    BottomPatch patch;
    sincos(angle, patch.rotation.y, patch.rotation.x);
    float2 center = float2(id.x + 0.5 * id.y, 0.866025404 * id.y) * max(_TileSize, 0.001);
    // Rotate around the patch center, without fract() or per-frame randomness.
    // With both random strengths zero, all three patches become the original UV.
    patch.uv = BottomRotate(uv - center, patch.rotation) + center
               + random.xy * _RandomOffsetStrength;
    // Use unbroken analytic gradients. Never differentiate hashed/discontinuous UVs.
    patch.dx = BottomRotate(dx, patch.rotation);
    patch.dy = BottomRotate(dy, patch.rotation);
    return patch;
}

void BottomLayoutGrad(float2 uv, float2 dx, float2 dy, out BottomPatch a, out BottomPatch b,
                      out BottomPatch c, out float3 weights)
{
#if defined(_RANDOM_TILE_UV)
    float2 p = uv / max(_TileSize, 0.001);
    float2 grid = float2(p.x - 0.577350269 * p.y, 1.154700538 * p.y);
    float2 cell = floor(grid);
    float2 f = frac(grid);
    float2 idA, idB, idC;
    if (f.x + f.y <= 1.0)
    {
        idA = cell;
        idB = cell + float2(1, 0);
        idC = cell + float2(0, 1);
        weights = float3(1.0 - f.x - f.y, f.x, f.y);
    }
    else
    {
        idA = cell + float2(1, 1);
        idB = cell + float2(0, 1);
        idC = cell + float2(1, 0);
        weights = float3(f.x + f.y - 1.0, 1.0 - f.x, 1.0 - f.y);
    }
    a = BottomMakePatch(uv, dx, dy, idA);
    b = BottomMakePatch(uv, dx, dy, idB);
    c = BottomMakePatch(uv, dx, dy, idC);
    // A patch has exactly zero influence on the opposite edge. Neighboring
    // triangles therefore use identical samples and weights along their shared edge.
    weights = pow(saturate(weights), max(_BlendSharpness, 1.0));
#else
    a.uv = uv; a.dx = dx; a.dy = dy; a.rotation = float2(1, 0);
    b = a; c = a;
    weights = float3(1, 0, 0);
#endif
    weights /= max(dot(weights, float3(1, 1, 1)), 0.000001);
}

void BottomLayout(float2 uv, out BottomPatch a, out BottomPatch b,
                  out BottomPatch c, out float3 weights)
{
    BottomLayoutGrad(uv,ddx(uv),ddy(uv),a,b,c,weights);
}

float BottomHeight(BottomPatch patch)
{
    return SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap,
                                 patch.uv, patch.dx, patch.dy).r;
}

float3 BottomWeights(BottomPatch a, BottomPatch b, BottomPatch c, float3 weights)
{
#if defined(_RANDOM_TILE_UV)
    float3 height = float3(BottomHeight(a), BottomHeight(b), BottomHeight(c));
    // Positive relief-based bias keeps small texture details clearer than a plain
    // crossfade. Multiplication preserves zero weights at every triangle edge.
    weights *= lerp(float3(1, 1, 1), pow(0.1 + 0.9 * height, 4.0), _HeightBlend);
    weights /= max(dot(weights, float3(1, 1, 1)), 0.000001);
#endif
    return weights;
}

half3 BottomPatchNormal(BottomPatch patch, half scale)
{
    half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap,
                               patch.uv, patch.dx, patch.dy), scale);
    // Texture coordinates rotated by R require the tangent-space normal to be
    // transformed by inverse(R), otherwise the lighting points the wrong way.
    n.xy = BottomRotate(n.xy, float2(patch.rotation.x, -patch.rotation.y));
    return n;
}

half3 BottomSampleNormal(float2 uv, TEXTURE2D_PARAM(normalMap, normalSampler), half scale)
{
    BottomPatch a, b, c; float3 weights;
    BottomLayout(uv, a, b, c, weights);
    weights = BottomWeights(a, b, c, weights);
#if defined(_RANDOM_TILE_UV)
    return normalize(BottomPatchNormal(a, scale) * weights.x
                   + BottomPatchNormal(b, scale) * weights.y
                   + BottomPatchNormal(c, scale) * weights.z);
#else
    return BottomPatchNormal(a, scale);
#endif
}

// The URP depth-normal pass must sample the same rotated/blended normal field.
#define SampleNormal BottomSampleNormal

void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
{
#if defined(_PARALLAXMAP)
    BottomPatch a, b, c; float3 weights;
    BottomLayout(uv, a, b, c, weights);
    float height = BottomHeight(a) * weights.x;
    #if defined(_RANDOM_TILE_UV)
        height += BottomHeight(b) * weights.y + BottomHeight(c) * weights.z;
    #endif
    uv += viewDirTS.xy / (max(viewDirTS.z, 0.0) + 0.42) * ((height - 0.5) * _Parallax);
#endif
}

void InitializeStandardLitSurfaceDataGrad(float2 uv, float2 dx, float2 dy, out SurfaceData surface)
{
    BottomPatch a, b, c; float3 weights;
    BottomLayoutGrad(uv, dx, dy, a, b, c, weights);
    weights = BottomWeights(a, b, c, weights);
    half3 color = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, a.uv, a.dx, a.dy).rgb;
    half roughness = SAMPLE_TEXTURE2D_GRAD(_RoughnessMap, sampler_RoughnessMap, a.uv, a.dx, a.dy).r;
    half3 normal = BottomPatchNormal(a, _BumpScale);
#if defined(_RANDOM_TILE_UV)
    color = color * weights.x
          + SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, b.uv, b.dx, b.dy).rgb * weights.y
          + SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, c.uv, c.dx, c.dy).rgb * weights.z;
    roughness = roughness * weights.x
          + SAMPLE_TEXTURE2D_GRAD(_RoughnessMap, sampler_RoughnessMap, b.uv, b.dx, b.dy).r * weights.y
          + SAMPLE_TEXTURE2D_GRAD(_RoughnessMap, sampler_RoughnessMap, c.uv, c.dx, c.dy).r * weights.z;
    normal = normal * weights.x + BottomPatchNormal(b, _BumpScale) * weights.y
                               + BottomPatchNormal(c, _BumpScale) * weights.z;
#endif
    surface = (SurfaceData)0;
    surface.albedo = color * _BaseColor.rgb;
    surface.normalTS = normalize(normal);
    surface.smoothness = saturate((1.0 - roughness) * _Smoothness);
    surface.metallic = _Metallic;
    surface.occlusion = 1;
    surface.alpha = 1;
}
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    InitializeStandardLitSurfaceDataGrad(uv,ddx(uv),ddy(uv),surface);
}
#endif

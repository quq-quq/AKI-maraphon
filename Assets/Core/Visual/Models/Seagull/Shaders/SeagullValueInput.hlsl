#ifndef AKI_SEAGULL_VALUE_INPUT_INCLUDED
#define AKI_SEAGULL_VALUE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// Identical material layout in colour, depth, normals and motion-vector passes.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    half4 _BaseColor;
    half _Cutoff;
    half _Surface;
    half _Value;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

#if defined(SEAGULL_VALUE_COLOR_PASS)
half4 SeagullValueColor()
{
    // RGB only: changing brightness must never change alpha clipping/coverage.
    return half4(_BaseColor.rgb * _Value, _BaseColor.a);
}
#define _BaseColor SeagullValueColor()
#endif

#endif

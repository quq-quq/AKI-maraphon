#ifndef CORAL_PASTEL_INPUT_INCLUDED
#define CORAL_PASTEL_INPUT_INCLUDED
// Define the stock sampler first, then redirect LitInput's albedo sampling.
// Alpha, UVs, surface detail, normal/roughness/AO and all URP lighting stay intact.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
half4 CoralPastelAlbedo(float2 uv,TEXTURE2D_PARAM(albedoMap,albedoSampler))
{
    half4 texel=SampleAlbedoAlpha(uv,TEXTURE2D_ARGS(albedoMap,albedoSampler));
    half luminance=dot(texel.rgb,half3(.2126,.7152,.0722));
    half3 desaturated=lerp(texel.rgb,luminance.xxx,.76);
    texel.rgb=lerp(desaturated,(.7+.3*luminance).xxx,.3);
    return texel;
}
#define SampleAlbedoAlpha CoralPastelAlbedo
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef SampleAlbedoAlpha
#endif

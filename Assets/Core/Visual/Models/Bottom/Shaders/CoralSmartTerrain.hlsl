#ifndef CORAL_SMART_TERRAIN_INCLUDED
#define CORAL_SMART_TERRAIN_INCLUDED
#include "BottomStochasticInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

TEXTURE2D(_CliffBaseMap); SAMPLER(sampler_CliffBaseMap);
TEXTURE2D(_CliffNormalMap); SAMPLER(sampler_CliffNormalMap);
TEXTURE2D(_CliffRoughnessMap); SAMPLER(sampler_CliffRoughnessMap);
TEXTURE2D(_CliffAOMap); SAMPLER(sampler_CliffAOMap);
TEXTURE2D(_TerrainHolesTexture); SAMPLER(sampler_TerrainHolesTexture);
#if defined(UNITY_INSTANCING_ENABLED) && defined(_REEF_TERRAIN)
TEXTURE2D(_TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture);
float4 _TerrainHeightmapRecipSize;
float4 _TerrainHeightmapScale;
UNITY_INSTANCING_BUFFER_START(Terrain)
UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
UNITY_INSTANCING_BUFFER_END(Terrain)
#endif

struct ReefAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct ReefVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float2 lightmapUV : TEXCOORD3;
    half fog : TEXCOORD4;
    UNITY_VERTEX_OUTPUT_STEREO
};
void ReefTerrainPosition(inout ReefAttributes v)
{
#if defined(UNITY_INSTANCING_ENABLED) && defined(_REEF_TERRAIN)
    float4 patch = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (v.positionOS.xy + patch.xy) * patch.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));
    v.positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    v.positionOS.y = height * _TerrainHeightmapScale.y;
    v.normalOS = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
    v.uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}
void ReefClipHoles(float2 uv)
{
#if defined(_REEF_TERRAIN) && defined(_ALPHATEST_ON)
    clip(SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r - 0.0005);
#endif
}
ReefVaryings ReefVertex(ReefAttributes v)
{
    ReefVaryings o = (ReefVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    ReefTerrainPosition(v);
    VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionWS = p.positionWS;
    o.positionCS = p.positionCS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv = v.uv;
    o.lightmapUV = v.uv * unity_LightmapST.xy + unity_LightmapST.zw;
    o.fog = ComputeFogFactor(p.positionCS.z);
    return o;
}

// All cliff channels use the same continuously blended random patches.
void ReefCliffPlane(float2 uv, float2 dx, float2 dy, out half3 albedo, out half3 n, out half rough, out half ao)
{
    BottomPatch a,b,c; float3 w;
    BottomLayoutGrad(uv,dx,dy,a,b,c,w);
    albedo = SAMPLE_TEXTURE2D_GRAD(_CliffBaseMap,sampler_CliffBaseMap,a.uv,a.dx,a.dy).rgb*w.x
           + SAMPLE_TEXTURE2D_GRAD(_CliffBaseMap,sampler_CliffBaseMap,b.uv,b.dx,b.dy).rgb*w.y
           + SAMPLE_TEXTURE2D_GRAD(_CliffBaseMap,sampler_CliffBaseMap,c.uv,c.dx,c.dy).rgb*w.z;
    rough = SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,a.uv,a.dx,a.dy).r*w.x
          + SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,b.uv,b.dx,b.dy).r*w.y
          + SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,c.uv,c.dx,c.dy).r*w.z;
    ao = SAMPLE_TEXTURE2D_GRAD(_CliffAOMap,sampler_CliffAOMap,a.uv,a.dx,a.dy).r*w.x
       + SAMPLE_TEXTURE2D_GRAD(_CliffAOMap,sampler_CliffAOMap,b.uv,b.dx,b.dy).r*w.y
       + SAMPLE_TEXTURE2D_GRAD(_CliffAOMap,sampler_CliffAOMap,c.uv,c.dx,c.dy).r*w.z;
    half3 na=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CliffNormalMap,sampler_CliffNormalMap,a.uv,a.dx,a.dy),_CliffNormalStrength);
    half3 nb=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CliffNormalMap,sampler_CliffNormalMap,b.uv,b.dx,b.dy),_CliffNormalStrength);
    half3 nc=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CliffNormalMap,sampler_CliffNormalMap,c.uv,c.dx,c.dy),_CliffNormalStrength);
    na.xy=BottomRotate(na.xy,float2(a.rotation.x,-a.rotation.y));
    nb.xy=BottomRotate(nb.xy,float2(b.rotation.x,-b.rotation.y));
    nc.xy=BottomRotate(nc.xy,float2(c.rotation.x,-c.rotation.y));
    n=normalize(na*w.x+nb*w.y+nc*w.z);
}
float ReefDetailFade(float3 p)
{
#if defined(_REEF_TERRAIN)
    return smoothstep(_TerrainDetailStart,max(_TerrainDetailEnd,_TerrainDetailStart+1),distance(p,_WorldSpaceCameraPos));
#else
    return 0;
#endif
}
half3 ReefCheapColor(float2 uv,float2 dx,float2 dy,TEXTURE2D_PARAM(map,mapSampler))
{
    BottomPatch a,b,c;float3 weights;
    BottomLayoutGrad(uv,dx,dy,a,b,c,weights);
    half3 color=SAMPLE_TEXTURE2D_GRAD(map,mapSampler,a.uv,a.dx,a.dy).rgb;
#if defined(_RANDOM_TILE_UV)
    color=color*weights.x
        +SAMPLE_TEXTURE2D_GRAD(map,mapSampler,b.uv,b.dx,b.dy).rgb*weights.y
        +SAMPLE_TEXTURE2D_GRAD(map,mapSampler,c.uv,c.dx,c.dy).rgb*weights.z;
#endif
    return color;
}
void ReefCheapSurface(float2 uv,float2 udx,float2 udy,float3 cp,float3 dx,float3 dy,half3 g,float blend,half macroTint,out SurfaceData surface)
{
    // Keep seamless randomized color patches, height/slope coverage and cliff
    // projection. Skip relief weights, normal/AO maps and roughness patch blending.
    half3 color=ReefCheapColor(uv,udx,udy,TEXTURE2D_ARGS(_BaseMap,sampler_BaseMap))*_BaseColor.rgb;
    half rough=SAMPLE_TEXTURE2D_GRAD(_RoughnessMap,sampler_RoughnessMap,uv,udx,udy).r;
    [branch] if(blend>.00001)
    {
        half3 weights=pow(abs(g),4);weights/=max(dot(weights,1),.0001);
        half3 sign=half3(g.x<0?-1:1,g.y<0?-1:1,g.z<0?-1:1);
        half3 rock=0;half rockRough=0;
        [branch] if(weights.x>.00001){
            float2 q=float2(cp.z*sign.x,cp.y),qx=float2(dx.z*sign.x,dx.y),qy=float2(dy.z*sign.x,dy.y);
            rock+=ReefCheapColor(q,qx,qy,TEXTURE2D_ARGS(_CliffBaseMap,sampler_CliffBaseMap))*weights.x;
            rockRough+=SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,q,qx,qy).r*weights.x;
        }
        [branch] if(weights.y>.00001){
            float2 q=float2(cp.x*sign.y,cp.z),qx=float2(dx.x*sign.y,dx.z),qy=float2(dy.x*sign.y,dy.z);
            rock+=ReefCheapColor(q,qx,qy,TEXTURE2D_ARGS(_CliffBaseMap,sampler_CliffBaseMap))*weights.y;
            rockRough+=SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,q,qx,qy).r*weights.y;
        }
        [branch] if(weights.z>.00001){
            float2 q=float2(-cp.x*sign.z,cp.y),qx=float2(-dx.x*sign.z,dx.y),qy=float2(-dy.x*sign.z,dy.y);
            rock+=ReefCheapColor(q,qx,qy,TEXTURE2D_ARGS(_CliffBaseMap,sampler_CliffBaseMap))*weights.z;
            rockRough+=SAMPLE_TEXTURE2D_GRAD(_CliffRoughnessMap,sampler_CliffRoughnessMap,q,qx,qy).r*weights.z;
        }
        color=lerp(color,rock*_CliffTint.rgb,blend);rough=lerp(rough,rockRough,blend);
    }
    surface=(SurfaceData)0;surface.albedo=color*macroTint;surface.smoothness=saturate((1-rough)*_Smoothness);
    surface.metallic=_Metallic;surface.occlusion=1;surface.alpha=1;surface.normalTS=half3(0,0,1);
}
void ReefMixDistance(inout SurfaceData surface,inout half3 n,SurfaceData distant,half3 g,float fade)
{
    surface.albedo=lerp(surface.albedo,distant.albedo,fade);
    surface.smoothness=lerp(surface.smoothness,distant.smoothness,fade);
    surface.occlusion=lerp(surface.occlusion,distant.occlusion,fade);
    n=normalize(lerp(n,g,fade));
}
void ReefSurface(float3 p, half3 geometricNormal, float detailFade, out SurfaceData surface, out half3 normalWS)
{
    half3 g=normalize(geometricNormal);
    surface=(SurfaceData)0;
    normalWS=g;
    float noise=(sin(p.x*.31+sin(p.z*.17))*sin(p.z*.27+p.x*.09)) * _TransitionNoise;
    float slope=acos(saturate(abs(g.y)))*(180.0/PI);
    float slopeWeight=smoothstep(_SlopeStart,_SlopeEnd,slope+noise*4);
    float elevation=smoothstep(_ElevationStart,_ElevationEnd,p.y+noise)*_ElevationInfluence;
    float blend=max(slopeWeight,elevation*(1-slopeWeight));
    blend=lerp(blend,1,saturate(_CliffOnly));
    half macroTint=.94+.06*sin(p.x*.083+sin(p.z*.071))*sin(p.z*.057);
    float3 cp=p/max(_CliffTileMeters,.01);
    // Compute derivatives BEFORE any divergent branch. Skip unused cliff layers
    // on open sand without introducing mip/UV seams at material boundaries.
    float3 dx=ddx(cp),dy=ddy(cp);
    float2 groundUV=p.xz/max(_GroundTileMeters,.01),groundDx=ddx(groundUV),groundDy=ddy(groundUV);
    SurfaceData distant=(SurfaceData)0;
    [branch] if(detailFade>.00001){
        ReefCheapSurface(groundUV,groundDx,groundDy,cp,dx,dy,g,blend,macroTint,distant);
        [branch] if(detailFade>=.99999){surface=distant;normalWS=g;return;}
    }
    SurfaceData ground;
    InitializeStandardLitSurfaceDataGrad(groundUV,groundDx,groundDy,ground);
    // Near-camera shading retains the original detailed surface gradients.
    half3 groundNormal=normalize(g+half3(ground.normalTS.x,0,ground.normalTS.y));
    [branch] if(blend<=.00001)
    {
        surface=ground;surface.albedo*=macroTint;surface.normalTS=half3(0,0,1);
        normalWS=groundNormal;ReefMixDistance(surface,normalWS,distant,g,detailFade);return;
    }
    half3 axisSign=half3(g.x<0?-1:1,g.y<0?-1:1,g.z<0?-1:1);
    half3 weights=pow(abs(g),4);weights/=max(dot(weights,1),.0001);
    half3 ax=0,ay=0,az=0,nx=half3(0,0,1),ny=nx,nz=nx;
    half rx=0,ry=0,rz=0,ox=0,oy=0,oz=0;
    [branch] if(weights.x>.00001) ReefCliffPlane(float2(cp.z*axisSign.x,cp.y),float2(dx.z*axisSign.x,dx.y),float2(dy.z*axisSign.x,dy.y),ax,nx,rx,ox);
    [branch] if(weights.y>.00001) ReefCliffPlane(float2(cp.x*axisSign.y,cp.z),float2(dx.x*axisSign.y,dx.z),float2(dy.x*axisSign.y,dy.z),ay,ny,ry,oy);
    [branch] if(weights.z>.00001) ReefCliffPlane(float2(-cp.x*axisSign.z,cp.y),float2(-dx.x*axisSign.z,dx.y),float2(-dy.x*axisSign.z,dy.y),az,nz,rz,oz);
    half3 cx=normalize(g+half3(0,nx.y,nx.x*axisSign.x));
    half3 cy=normalize(g+half3(ny.x*axisSign.y,0,ny.y));
    half3 cz=normalize(g+half3(-nz.x*axisSign.z,nz.y,0));
    half3 cliffNormal=normalize(cx*weights.x+cy*weights.y+cz*weights.z);
    surface=ground;
    surface.albedo=lerp(ground.albedo,(ax*weights.x+ay*weights.y+az*weights.z)*_CliffTint.rgb,blend)*macroTint;
    surface.smoothness=lerp(ground.smoothness,(1-(rx*weights.x+ry*weights.y+rz*weights.z))*_Smoothness,blend);
    surface.occlusion=lerp(1,ox*weights.x+oy*weights.y+oz*weights.z,blend);
    surface.normalTS=half3(0,0,1);
    normalWS=normalize(lerp(groundNormal,cliffNormal,blend));
    ReefMixDistance(surface,normalWS,distant,g,detailFade);
}
half4 ReefFragment(ReefVaryings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    ReefClipHoles(i.uv);
    SurfaceData surface;half3 n;
    ReefSurface(i.positionWS,i.normalWS,ReefDetailFade(i.positionWS),surface,n);
    InputData d=(InputData)0;
    d.positionWS=i.positionWS;d.positionCS=i.positionCS;d.normalWS=n;
    d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        d.shadowCoord=ComputeScreenPos(i.positionCS);
    #else
        d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    #endif
    d.fogCoord=i.fog;
    d.vertexLighting=VertexLighting(i.positionWS,n);
    d.bakedGI=SAMPLE_GI(i.lightmapUV,SampleSH(n),n);
    d.shadowMask=SAMPLE_SHADOWMASK(i.lightmapUV);
    d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
    half4 color=UniversalFragmentPBR(d,surface);
    color.rgb=MixFog(color.rgb,d.fogCoord);return color;
}
half4 ReefDepth(ReefVaryings i) : SV_Target
{
    ReefClipHoles(i.uv);return i.positionCS.z;
}
half4 ReefDepthNormal(ReefVaryings i) : SV_Target
{
    ReefClipHoles(i.uv);
    SurfaceData s;half3 n;ReefSurface(i.positionWS,i.normalWS,ReefDetailFade(i.positionWS),s,n);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
#else
    return half4(n,0);
#endif
}
float3 _LightDirection;float3 _LightPosition;
struct ReefMetaAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
};
ReefVaryings ReefMetaVertex(ReefMetaAttributes v)
{
    ReefVaryings o=(ReefVaryings)0;
    o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);
    o.positionCS=UnityMetaVertexPosition(v.positionOS.xyz,v.uv1,v.uv2);
    o.uv=v.uv;return o;
}
half4 ReefMetaFragment(ReefVaryings i) : SV_Target
{
    ReefClipHoles(i.uv);
    SurfaceData s;half3 n;ReefSurface(i.positionWS,i.normalWS,0,s,n);
    UnityMetaInput meta=(UnityMetaInput)0;meta.Albedo=s.albedo;meta.Emission=s.emission;
    return UnityMetaFragment(meta);
}
ReefVaryings ReefShadowVertex(ReefAttributes v)
{
    ReefVaryings o=(ReefVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);ReefTerrainPosition(v);
    float3 p=TransformObjectToWorld(v.positionOS.xyz);
    float3 n=TransformObjectToWorldNormal(v.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 l=normalize(_LightPosition-p);
#else
    float3 l=_LightDirection;
#endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(p,n,l));
#if UNITY_REVERSED_Z
    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
#else
    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
#endif
    o.uv=v.uv;return o;
}
#endif

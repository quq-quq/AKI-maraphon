#ifndef AKI_WATER_SURFACE_VERTEX_INCLUDED
#define AKI_WATER_SURFACE_VERTEX_INCLUDED

// Vertex stage of the water surface, shared by its depth pre-pass and its colour pass: both must displace the
// grid by exactly the same waves, or the colour pass fails the depth test against its own surface.

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

#endif

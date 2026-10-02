#ifndef AKI_WATER_INPUT_INCLUDED
#define AKI_WATER_INPUT_INCLUDED

// Every property lives in one UnityPerMaterial block so the shader stays SRP Batcher compatible.
// AKI/Water and AKI/WaterUnderwater declare the *same* properties, so one material can feed both
// (WaterSurface copies the values over).
CBUFFER_START(UnityPerMaterial)
    // Water body
    half4  _ShallowColor;
    half4  _DeepColor;
    half4  _Absorption;          // rgb: extinction per metre (red dies first, like in real water)
    float  _DepthDistance;       // vertical depth (m) at which the colour reaches _DeepColor
    float  _Turbidity;           // multiplies extinction -> denser, hazier water
    float  _ScatterBrightness;
    half4  _SSSColor;
    float  _SSSIntensity;

    // Waves
    float  _WindAngle;
    float  _WaveAmplitude;
    float  _WaveLength;
    float  _WaveSteepness;
    float  _WaveSpread;
    float  _WaveSpeed;
    float  _WaveVariation;
    float  _WaveRandomness;
    float  _WaveSeed;
    float  _WaveWarp;            // Voronoi phase warp (radians)
    float  _WarpScale;           // warp cells per metre
    float  _CellRipples;         // strength of the small Voronoi domes
    float  _CellScale;           // ripple cells per metre
    float  _CellWaveHeight;      // Voronoi wave field: height (m) of the independent rising/falling cells
    float  _CellWaveScale;       // cells per metre
    float  _CellWaveSpeed;       // how fast cells rise and fall
    float  _DetailStrength;
    float  _DetailFade;
    float  _FlattenDistance;

    // Surface shading
    float  _ReflectionStrength;
    float  _Roughness;
    float  _SpecularIntensity;
    float  _SpecularPower;
    float  _RefractionStrength;
    float  _EdgeSoftness;
    float  _SimpleOpacity;

    // Cartoon look
    float  _ToonAmount;
    float  _ToonBands;
    float  _ToonSoftness;
    half4  _ToonHighlight;

    // Large-scale Voronoi on colour and sun glints
    float  _ColorCellScale;
    float  _ColorVariation;
    float  _GlintCellScale;
    float  _GlintCellStrength;

    // Foam
    half4  _FoamColor;
    float  _ShoreFoamWidth;
    float  _CrestFoamThreshold;
    float  _FoamScale;
    float  _FoamIntensity;

    // Caustics on the bottom
    float  _CausticsIntensity;
    float  _CausticsScale;
    float  _CausticsSpeed;
    float  _CausticsMaxDepth;

    // Volumetric light shafts
    half4  _RayColor;
    float  _RayIntensity;
    float  _RayScale;
    float  _RaySteps;
    float  _RayLength;
    float  _RayPhase;
    float  _RayFade;
    float  _RayContrast;
    float  _RayDepthFade;        // extra fade of the shafts per metre of depth

    // Seen from below
    float  _UnderFogScale;       // fog density multiplier when the camera is under the surface
    float  _UnderWobble;         // screen-space refraction wobble
    float  _UnderMaxDistance;    // how far (m) the camera sees under water
    float  _UnderFogEnd;         // nothing farther than this (m) shows through the haze: hides the edges of the world
    half4  _UnderFogColor;       // haze colour just below the surface
    half4  _UnderDeepColor;      // haze colour deep down
    float  _UnderDepthFalloff;   // how fast the haze darkens with depth (1/m)
    float  _UnderLightAbsorb;    // how much sunlight on objects fades with their depth
    float  _CausticsDispersion;  // rainbow split of the caustics
CBUFFER_END

#endif

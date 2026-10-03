Shader "Bottom/Coral Smart Terrain"
{
    Properties
    {
        [MainTexture][NoScaleOffset] _BaseMap("Ground Base Color",2D)="white"{}
        [MainColor] _BaseColor("Ground Tint",Color)=(1,1,1,1)
        [Normal][NoScaleOffset] _BumpMap("Ground Normal",2D)="bump"{}
        _BumpScale("Ground Normal Strength",Range(0,2))=.4
        [NoScaleOffset] _RoughnessMap("Ground Roughness",2D)="white"{}
        [NoScaleOffset] _ParallaxMap("Ground Height / Random Blend",2D)="gray"{}
        _GroundTileMeters("Ground Tile Size (meters)",Float)=3
        [NoScaleOffset] _CliffBaseMap("Cliff Base Color",2D)="white"{}
        [Normal][NoScaleOffset] _CliffNormalMap("Cliff Normal",2D)="bump"{}
        [NoScaleOffset] _CliffRoughnessMap("Cliff Roughness",2D)="white"{}
        [NoScaleOffset] _CliffAOMap("Cliff AO",2D)="white"{}
        _CliffTint("Cliff Tint",Color)=(1,1,1,1)
        _CliffTileMeters("Cliff Tile Size (meters)",Float)=5
        _CliffNormalStrength("Cliff Normal Strength",Range(0,2))=.5
        _SlopeStart("Cliff Starts at Slope (degrees)",Range(0,85))=28
        _SlopeEnd("Full Cliff at Slope (degrees)",Range(1,89))=48
        _ElevationStart("Rock Elevation Starts (world Y)",Float)=-15
        _ElevationEnd("Rock Elevation Full (world Y)",Float)=-4
        _ElevationInfluence("Elevation Rock Coverage",Range(0,1))=.35
        _TransitionNoise("Irregular Transition (meters)",Range(0,5))=1.2
        _Smoothness("Smoothness Multiplier",Range(0,1))=.55
        _Metallic("Metallic",Range(0,1))=0
        [Toggle(_RANDOM_TILE_UV)] _RandomizeUV("Seamless Random Tiling",Float)=1
        _TileSize("Random Patch Size",Range(.25,4))=1
        _RandomOffsetStrength("Random Offset",Range(0,1))=1
        _RandomRotationStrength("Random Rotation",Range(0,1))=1
        _RotationStepDeg("Rotation Step",Range(1,180))=90
        _RandomSeed("Static Random Seed",Float)=14.3
        _BlendSharpness("Blend Sharpness",Range(1,6))=3
        _HeightBlend("Ground Relief Blend",Range(0,1))=.55
        [Toggle(_REEF_TERRAIN)] _ReefTerrain("Terrain GPU Instancing (off for mesh)",Float)=0
        _CliffOnly("Cliff Only (rocks)",Range(0,1))=0
        _TerrainDetailStart("Terrain Detail Fade Starts (meters)",Float)=45
        _TerrainDetailEnd("Terrain Cheap Shading After (meters)",Float)=80
        [HideInInspector] _TerrainHolesTexture("Holes",2D)="white"{}
        [HideInInspector] _Control("Control",2D)="red"{}
        [HideInInspector] _MainTex("Base Map",2D)="white"{}
        [HideInInspector] _Surface("Surface",Float)=0
        [HideInInspector] _Cutoff("Cutoff",Float)=.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
    }
    SubShader
    {
        Tags{"RenderType"="Opaque" "Queue"="Geometry-100" "RenderPipeline"="UniversalPipeline" "UniversalMaterialType"="Lit" "TerrainCompatible"="True"}
        HLSLINCLUDE
        #include "CoralSmartTerrain.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode"="UniversalForwardOnly"}
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReefVertex
            #pragma fragment ReefFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local _REEF_TERRAIN
            #pragma shader_feature_local _RANDOM_TILE_UV
            #pragma multi_compile_fragment _ _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"}
            Cull [_Cull] ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReefShadowVertex
            #pragma fragment ReefDepth
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local _REEF_TERRAIN
            #pragma multi_compile_fragment _ _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags{"LightMode"="DepthOnly"}
            Cull [_Cull] ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReefVertex
            #pragma fragment ReefDepth
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local _REEF_TERRAIN
            #pragma multi_compile_fragment _ _ALPHATEST_ON
            ENDHLSL
        }
        Pass
        {
            Name "Meta" Tags{"LightMode"="Meta"}
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReefMetaVertex
            #pragma fragment ReefMetaFragment
            #pragma shader_feature_local _RANDOM_TILE_UV
            #pragma shader_feature_local _REEF_TERRAIN
            #pragma multi_compile_fragment _ _ALPHATEST_ON
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReefVertex
            #pragma fragment ReefDepthNormal
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local _REEF_TERRAIN
            #pragma shader_feature_local _RANDOM_TILE_UV
            #pragma multi_compile_fragment _ _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Dependency "BaseMapShader"="Bottom/Coral Smart Terrain"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

Shader "Hidden/AKI/OptimizedVolumetricClouds"
{
    Properties
    {
        _CloudNoise ("Baked 3D Shape / Erosion", 3D) = "white" {}
        _CloudWeather ("Baked Coverage", 2D) = "white" {}
        _CloudLayer ("Base / Top / Distance / Scale", Vector) = (650,1150,15000,1800)
        _CloudWind ("Wind XZ / Evolution", Vector) = (6,2,.0035,0)
        _CloudShape ("Coverage / Extinction / Silver / Steps", Vector) = (.56,.012,.45,24)
        _CloudAmbient ("Ambient", Color) = (.4,.48,.6,1)
        _CloudSunColor ("Sunlight", Color) = (1,.98,.95,1)
        _CloudTimeOverride ("QA Time (-1 = automatic)", Float) = -1
        _CloudVisibility ("Per-camera Underwater Fade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        TEXTURE3D(_CloudNoise); SAMPLER(sampler_CloudNoise);
        TEXTURE2D(_CloudWeather); SAMPLER(sampler_CloudWeather);
        TEXTURE2D_X(_AKICloudBuffer);
        CBUFFER_START(UnityPerMaterial)
        float4 _CloudLayer, _CloudWind, _CloudShape;
        half4 _CloudAmbient, _CloudSunColor;
        float _CloudTimeOverride, _CloudVisibility;
        CBUFFER_END
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id); return o;
        }
        bool IsSky(float2 uv)
        {
            float depth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
            return depth < .00001;
            #else
            return depth > .99999;
            #endif
        }
        float Density(float3 p, float time)
        {
            float h = (p.y - _CloudLayer.x) / (_CloudLayer.y - _CloudLayer.x);
            if (h <= 0 || h >= 1) return 0;
            float2 drift = _CloudWind.xy * time;
            float2 weatherUV = (p.xz - drift) / 15000.0;
            float weather = SAMPLE_TEXTURE2D_LOD(_CloudWeather, sampler_CloudWeather, weatherUV, 0).r;
            float cover = saturate(_CloudShape.x + (weather - .5) * .8);
            float3 uvw = (p - float3(drift.x,0,drift.y)) / _CloudLayer.w;
            // Continuous low-speed counter-drift deforms the density; nothing is regenerated on CPU.
            uvw += float3(.35, .23, -.18) * (time * _CloudWind.z);
            half3 noise = SAMPLE_TEXTURE3D_LOD(_CloudNoise, sampler_CloudNoise, uvw, 0).rgb;
            float vertical = smoothstep(0,.10,h) * (1 - smoothstep(.40 + weather*.15,1,h));
            float shape = saturate((noise.r - (1-cover)) * 5.0);
            float erosion = (1-noise.g) * .14 * (1-shape);
            return saturate(shape * vertical - erosion);
        }
        half4 March(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            if (!IsSky(i.uv)) return 0;
            #if UNITY_REVERSED_Z
            float farDepth = 0;
            #else
            float farDepth = 1;
            #endif
            float3 farPoint = ComputeWorldSpacePosition(i.uv, farDepth, UNITY_MATRIX_I_VP);
            float3 origin = _WorldSpaceCameraPos;
            float3 dir = normalize(farPoint-origin);
            if (dir.y < .015) return 0;
            float start = max(0, (_CloudLayer.x-origin.y)/dir.y);
            float end = min((_CloudLayer.y-origin.y)/dir.y, _CloudLayer.z);
            if (end <= start) return 0;
            int count = (int)_CloudShape.w;
            float stepLength = (end-start)/count;
            // Stable screen-space jitter (no temporal shimmer or history/reprojection buffers).
            float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(.06711056,.00583715))));
            float distance = start + stepLength * (.25 + jitter*.5);
            float time = _CloudTimeOverride >= 0 ? _CloudTimeOverride : _Time.y;
            Light sun = GetMainLight();
            float3 lightDir = normalize(sun.direction);
            float forward = pow(saturate(dot(dir,lightDir)), 8) * _CloudShape.z;
            half3 sunlight = sun.color / max(1.0h, max(sun.color.r, max(sun.color.g, sun.color.b)));
            half3 accumulated = 0; float transmittance = 1;
            [loop] for (int s=0; s<48; s++)
            {
                if (s >= count || transmittance < .035) break;
                float3 p = origin + dir*distance;
                float density = Density(p,time);
                if (density > .008)
                {
                    // A single short density-gradient tap; no nested light raymarch.
                    float lightDensity = Density(p + lightDir*210,time);
                    float shadow = exp(-lightDensity*4.8);
                    float h = saturate((p.y-_CloudLayer.x)/(_CloudLayer.y-_CloudLayer.x));
                    half3 lighting = _CloudAmbient.rgb * (.7 + h*.25) + _CloudSunColor.rgb * sunlight * (shadow*.80 + .12 + forward*.45);
                    lighting = lighting / (1 + lighting*.35h);
                    float opacity = 1-exp(-density*_CloudShape.y*stepLength);
                    accumulated += lighting * (opacity*transmittance);
                    transmittance *= 1-opacity;
                }
                distance += stepLength;
            }
            // Far clouds merge gently into the existing sky rather than forming a hard horizon ring.
            float fade = smoothstep(.035,.18,dir.y) * (1-smoothstep(_CloudLayer.z*.65,_CloudLayer.z,start));
            return half4(accumulated*fade, (1-transmittance)*fade);
        }
        half4 Composite(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            if (!IsSky(i.uv)) discard;
            // Fade both premultiplied RGB and alpha, retaining the original skybox underneath.
            return SAMPLE_TEXTURE2D_X_LOD(_AKICloudBuffer, sampler_LinearClamp, i.uv, 0) * _CloudVisibility;
        }
        ENDHLSL
        Pass
        {
            Name "LowResolutionVolume"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment March
            ENDHLSL
        }
        Pass
        {
            Name "SkyComposite"
            ZWrite Off ZTest Always Cull Off
            Blend One OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}

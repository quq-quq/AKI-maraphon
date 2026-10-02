using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AKI.Clouds.Editor
{
    public static class CloudSkyBuilder
    {
        const string Root = "Assets/Core/Visual/Clouds/";
        static float Random(int x, int y, int z, int period)
        {
            x = (x % period + period) % period; y = (y % period + period) % period; z = (z % period + period) % period;
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
        static float Value(Vector3 p, int period)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float a=p.x-x,b=p.y-y,c=p.z-z; a=a*a*(3-2*a); b=b*b*(3-2*b); c=c*c*(3-2*c);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(Random(x,y,z,period),Random(x+1,y,z,period),a),
                Mathf.Lerp(Random(x,y+1,z,period),Random(x+1,y+1,z,period),a),b),
                Mathf.Lerp(Mathf.Lerp(Random(x,y,z+1,period),Random(x+1,y,z+1,period),a),
                Mathf.Lerp(Random(x,y+1,z+1,period),Random(x+1,y+1,z+1,period),a),b),c);
        }
        static float Worley(Vector3 p, int period)
        {
            int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
            float nearest=4;
            for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {
                int cx=x+dx,cy=y+dy,cz=z+dz;
                var q=new Vector3(cx+Random(cx,cy,cz,period),cy+Random(cx+17,cy+29,cz+11,period),cz+Random(cx+7,cy+3,cz+19,period));
                nearest=Mathf.Min(nearest,(p-q).sqrMagnitude);
            }
            return 1-Mathf.Clamp01(Mathf.Sqrt(nearest));
        }
        public static string Build()
        {
            if(GameObject.Find("CloudSky_Optimized")!=null)throw new InvalidOperationException("Cloud sky already exists. Preserve its settings.");
            foreach(string folder in new[]{"Textures","Materials","Prefabs"})
                if(!AssetDatabase.IsValidFolder(Root+folder))AssetDatabase.CreateFolder(Root.TrimEnd('/'),folder);
            const int size=64;var pixels=new Color32[size*size*size];
            for(int z=0;z<size;z++)for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var p=new Vector3((x+.5f)/size,(y+.5f)/size,(z+.5f)/size);
                float fractal=(Value(p*4,4)*.53f+Value(p*8,8)*.27f+Value(p*16,16)*.13f+Value(p*32,32)*.07f);
                float billow=Worley(p*5,5),fine=Worley(p*9,9);
                float shape=Mathf.Clamp01(fractal*.65f+billow*.35f+.10f);
                pixels[x+size*(y+size*z)]=new Color(shape,fine,Value(p*16,16),1);
            }
            var noise=new Texture3D(size,size,size,GraphicsFormat.R8G8B8A8_UNorm,TextureCreationFlags.None);
            noise.name="CloudShape_PerlinWorley_64";noise.wrapMode=TextureWrapMode.Repeat;noise.filterMode=FilterMode.Bilinear;
            noise.SetPixels32(pixels);noise.Apply(false,true);AssetDatabase.CreateAsset(noise,Root+"Textures/CloudShape64.asset");
            const int weatherSize=128;var weatherPixels=new Color32[weatherSize*weatherSize];
            for(int y=0;y<weatherSize;y++)for(int x=0;x<weatherSize;x++)
            {
                var p=new Vector3((x+.5f)/weatherSize,0,(y+.5f)/weatherSize);
                float value=Value(p*4,4)*.6f+Value(p*8,8)*.28f+Value(p*16,16)*.12f;
                weatherPixels[x+y*weatherSize]=new Color(value,value,value,1);
            }
            var weather=new Texture2D(weatherSize,weatherSize,TextureFormat.RGBA32,false,true);
            weather.name="CloudWeather128";weather.wrapMode=TextureWrapMode.Repeat;weather.filterMode=FilterMode.Bilinear;
            weather.SetPixels32(weatherPixels);weather.Apply(false,true);AssetDatabase.CreateAsset(weather,Root+"Textures/CloudWeather128.asset");
            var shader=Shader.Find("Hidden/AKI/OptimizedVolumetricClouds");if(shader==null)throw new InvalidOperationException("Cloud shader missing.");
            var mat=new Material(shader);mat.name="CloudSky_Optimized";mat.SetTexture("_CloudNoise",noise);mat.SetTexture("_CloudWeather",weather);
            AssetDatabase.CreateAsset(mat,Root+"Materials/CloudSky_Optimized.mat");
            var sky=new GameObject("CloudSky_Optimized");Undo.RegisterCreatedObjectUndo(sky,"Add optimized volumetric cloud sky");
            var settings=sky.AddComponent<OptimizedCloudSky>();settings.material=mat;settings.ApplySettings();
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if(renderer.rendererFeatures.OfType<OptimizedCloudsFeature>().Any())throw new InvalidOperationException("Cloud renderer feature already installed.");
            var feature=ScriptableObject.CreateInstance<OptimizedCloudsFeature>();feature.name="Optimized Volumetric Clouds";feature.shader=shader;
            Undo.RecordObject(renderer,"Install optimized cloud pass");AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);
            feature.Create();renderer.SetDirty();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(settings);EditorUtility.SetDirty(mat);
            PrefabUtility.SaveAsPrefabAsset(sky,Root+"Prefabs/CloudSky_Optimized.prefab");
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(sky.scene);EditorSceneManager.SaveScene(sky.scene);
            Selection.activeGameObject=sky;
            return "Cloud sky installed: real 3D raymarch, quarter resolution (384px cap), 32 steps, 64^3 noise, 128^2 weather, no runtime noise generation.";
        }
    }
}

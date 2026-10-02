using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AKI.Reef.Editor
{
    public static class CoralReefBuilder
    {
        const string RootName = "CoralReef_Generated";
        const string AssetRoot = "Assets/Core/Visual/Models/Bottom/Reef/";

        [MenuItem("Tools/Coral Reef/Rebuild Current Test Scene")]
        public static void RebuildFromMenu()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CoralReefSettings>(AssetRoot + "CoralReefSettings.asset");
            if (settings == null) throw new InvalidOperationException("Missing CoralReefSettings asset.");
            if (!EditorUtility.DisplayDialog("Rebuild coral reef", "Regenerate ONLY CoralReef_Generated in the current test scene? Other objects are retained. Save a scene copy first if you have hand-edited the generated corals.", "Rebuild", "Cancel")) return;
            Debug.Log(Build(settings));
        }

        public static string Build(CoralReefSettings settings)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "CoralBottonTest") throw new InvalidOperationException("Open CoralBottonTest before rebuilding.");
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            if (settings.coralPrefabs == null || settings.coralPrefabs.Length != 11 || Array.Exists(settings.coralPrefabs, x => x == null))
                throw new InvalidOperationException("Assign all 11 coral prefabs.");
            if (settings.terrainMaterial == null) throw new InvalidOperationException("Assign terrain material.");
            if (settings.basinRadius >= settings.terrainSize*.5f-8) throw new InvalidOperationException("Basin radius must leave at least 8m for the outer reef.");
            var basinHills = BasinHills(settings);
            var outerMountains = OuterMountains(settings);

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate coral reef");
            var terrainPath = AssetRoot + "CoralReef_TerrainData.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
            if (data == null) { data = new TerrainData(); AssetDatabase.CreateAsset(data, terrainPath); }
            data.name = "CoralReef_TerrainData";
            data.heightmapResolution = 513;
            data.alphamapResolution = 256;
            data.baseMapResolution = 512;
            data.size = new Vector3(settings.terrainSize, settings.terrainHeight, settings.terrainSize);
            float half = settings.terrainSize * 0.5f;
            var heights = new float[513,513];
            for (int z=0; z<513; z++) for (int x=0; x<513; x++)
            {
                float wx=(float)x/512*settings.terrainSize-half;
                float wz=(float)z/512*settings.terrainSize-half;
                heights[z,x]=Mathf.Clamp01(Height(wx,wz,settings,basinHills,outerMountains)/settings.terrainHeight);
            }
            data.SetHeights(0,0,heights);
            string layerPath=AssetRoot+"CoralGround.terrainlayer";
            var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if(layer==null){ layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,layerPath); }
            layer.diffuseTexture=settings.terrainMaterial.GetTexture("_BaseMap") as Texture2D;
            layer.normalMapTexture=settings.terrainMaterial.GetTexture("_BumpMap") as Texture2D;
            layer.tileSize=Vector2.one*settings.terrainMaterial.GetFloat("_GroundTileMeters");
            data.terrainLayers=new[]{layer};
            var alpha=new float[256,256,1];for(int z=0;z<256;z++)for(int x=0;x<256;x++)alpha[z,x,0]=1;
            data.SetAlphamaps(0,0,alpha);
            var terrainGO=Terrain.CreateTerrainGameObject(data);
            terrainGO.name="Reef Terrain";terrainGO.transform.SetParent(root.transform,false);
            terrainGO.transform.position=new Vector3(-half,settings.terrainBaseY,-half);
            var terrain=terrainGO.GetComponent<Terrain>();
            terrain.materialTemplate=settings.terrainMaterial;terrain.drawInstanced=true;
            terrain.heightmapPixelError=8;terrain.basemapDistance=2000;terrain.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            terrain.allowAutoConnect=true;

            var rng=new System.Random(settings.seed);
            var clusters=new GameObject("Hill Coral Clusters");clusters.transform.SetParent(root.transform,false);
            var sparse=new GameObject("Sparse Seabed Corals");sparse.transform.SetParent(root.transform,false);
            var centers=new List<Vector2>();int coralCount=0,clusterCount=0,sparseCount=0;
            var occupied=new List<Vector2>();
            // Find stable shelves and shoulders, never attach corals to vertical walls.
            for(int attempt=0; attempt<settings.hillClusters*100 && clusterCount<settings.hillClusters; attempt++)
            {
                float x,z;
                if(rng.NextDouble()<.52){
                    x=Range(rng,-half+4,half-4);z=Range(rng,-half+4,half-4);
                    if(new Vector2(x,z).magnitude<settings.basinRadius-5)continue;
                }else{
                    float a=Range(rng,0,Mathf.PI*2),r=Mathf.Sqrt(Range(rng,0,1))*(settings.basinRadius-6);
                    x=Mathf.Cos(a)*r;z=Mathf.Sin(a)*r;
                }
                float h=terrain.SampleHeight(new Vector3(x,0,z))+settings.terrainBaseY;
                float slope=data.GetSteepness((x+half)/settings.terrainSize,(z+half)/settings.terrainSize);
                float relative=h-settings.terrainBaseY;
                bool interior=new Vector2(x,z).magnitude<settings.basinRadius-5;
                float hillLift=relative-BasinFloor(x,z,settings);
                if(slope>settings.coralMaxSlope || hillLift<.6f || (interior && slope<.6f))continue;
                var p=new Vector2(x,z);
                if(centers.Exists(c=>(c-p).sqrMagnitude<28))continue;
                if(rng.NextDouble()>(interior?Mathf.Lerp(.4f,.95f,Mathf.InverseLerp(.6f,2.5f,hillLift)):.9f))continue;
                int requested=rng.Next(settings.minClusterCount,Math.Max(settings.minClusterCount,settings.maxClusterCount)+1);
                var group=new GameObject("Cluster_"+clusterCount.ToString("000"));
                group.transform.SetParent(clusters.transform,false);group.transform.position=new Vector3(x,h,z);
                var points=new List<Vector2>();
                for(int tries=0;tries<80 && points.Count<requested;tries++)
                {
                    float angle=Range(rng,0,Mathf.PI*2),radius=Mathf.Sqrt(Range(rng,0,1))*settings.clusterRadius;
                    var candidate=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    if(Mathf.Abs(candidate.x)>half-1 || Mathf.Abs(candidate.y)>half-1)continue;
                    float nSlope=data.GetSteepness((candidate.x+half)/settings.terrainSize,(candidate.y+half)/settings.terrainSize);
                    if(nSlope>settings.coralMaxSlope || points.Exists(c=>(c-candidate).sqrMagnitude<.7f) || occupied.Exists(c=>(c-candidate).sqrMagnitude<.6f))continue;
                    points.Add(candidate);
                }
                // Every named cluster must have 3–7 actual corals, not failed partial groups.
                if(points.Count<settings.minClusterCount){UnityEngine.Object.DestroyImmediate(group);continue;}
                int variant=rng.Next(settings.coralPrefabs.Length);
                int palette=rng.Next(6);
                for(int j=0;j<points.Count;j++){
                    int species=rng.NextDouble()<.5?variant:rng.Next(settings.coralPrefabs.Length);
                    int tint=rng.NextDouble()<.65?palette:rng.Next(6);
                    Place(settings.coralPrefabs[species],points[j],group.transform,terrain,settings,rng,j==0?1.25f:1,species,tint);
                    occupied.Add(points[j]);coralCount++;
                }
                centers.Add(p);clusterCount++;
            }
            for(int attempt=0;attempt<settings.sparseCorals*100 && sparseCount<settings.sparseCorals;attempt++)
            {
                float a=Range(rng,0,Mathf.PI*2),r=Mathf.Sqrt(Range(rng,0,1))*(settings.basinRadius-5);
                var p=new Vector2(Mathf.Cos(a)*r,Mathf.Sin(a)*r);
                float h=terrain.SampleHeight(new Vector3(p.x,0,p.y));
                float slope=data.GetSteepness((p.x+half)/settings.terrainSize,(p.y+half)/settings.terrainSize);
                if(h-BasinFloor(p.x,p.y,settings)>1.1f || slope>settings.coralMaxSlope || centers.Exists(c=>(c-p).sqrMagnitude<50) || occupied.Exists(c=>(c-p).sqrMagnitude<12))continue;
                // Patchy distribution leaves breathing room and open sand channels.
                if(Mathf.PerlinNoise(p.x*.022f+17,p.y*.022f+24)<.43f)continue;
                int species=rng.Next(settings.coralPrefabs.Length);
                Place(settings.coralPrefabs[species],p,sparse.transform,terrain,settings,rng,.85f,species,rng.Next(6));
                occupied.Add(p);sparseCount++;coralCount++;
            }
            var legacy=GameObject.Find("Legacy_Seabed_Backup");
            if(legacy==null){
                legacy=new GameObject("Legacy_Seabed_Backup");
                foreach(string name in new[]{"Seabed","Rock A","Rock B","Rock C","Rock D"}){
                    var existing=GameObject.Find(name);if(existing!=null)existing.transform.SetParent(legacy.transform,true);
                }
                legacy.SetActive(false);
            }
            EditorUtility.SetDirty(settings);EditorUtility.SetDirty(data);EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject=terrainGO;
            var view=SceneView.lastActiveSceneView;
            if(view!=null)view.LookAt(new Vector3(0,-13,0),Quaternion.Euler(24,-26,0),75,false,true);
            return "Terrain "+settings.terrainSize+"m; "+clusterCount+" clusters (3–7 corals); "+sparseCount+" sparse; "+coralCount+" corals; seed "+settings.seed;
        }

        static Vector4[] BasinHills(CoralReefSettings s)
        {
            var rng=new System.Random(s.seed^0x4a17);var hills=new List<Vector4>();
            for(int attempt=0;attempt<10000 && hills.Count<s.innerHillCount;attempt++){
                float a=Range(rng,0,Mathf.PI*2),r=Mathf.Sqrt(Range(rng,0,1))*(s.basinRadius-14);
                float x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
                if(hills.Exists(h=>new Vector2(h.x-x,h.y-z).sqrMagnitude<110))continue;
                hills.Add(new Vector4(x,z,Range(rng,7,13),Range(rng,.4f,1)*s.innerHillHeight));
            }
            return hills.ToArray();
        }
        static float BasinFloor(float x,float z,CoralReefSettings s)
        {
            float offset=s.seed*.0137f,r=new Vector2(x,z).magnitude/s.basinRadius;
            return 6.2f+.8f*Mathf.Min(r*r,1.6f)+.8f*Mathf.PerlinNoise(x*.023f+offset,z*.023f+47)+.22f*Mathf.PerlinNoise(x*.095f+31,z*.095f+offset);
        }
        static Vector4[] OuterMountains(CoralReefSettings s)
        {
            var rng=new System.Random(s.seed^0x5e31);var mountains=new List<Vector4>();
            float half=s.terrainSize*.5f;
            for(int i=0;i<s.outerMountainCount;i++){
                float angle=(i+Range(rng,-.38f,.38f))*Mathf.PI*2/s.outerMountainCount;
                float c=Mathf.Cos(angle),sn=Mathf.Sin(angle);
                // Scatter independent masses along the actual square boundary,
                // with variable heights and widths, leaving deep open gaps.
                float boundary=half/Mathf.Max(Mathf.Abs(c),Mathf.Abs(sn));
                float radius=Mathf.Max(s.basinRadius+5,boundary-Range(rng,4,17));
                mountains.Add(new Vector4(c*radius,sn*radius,Range(rng,13,24),Range(rng,.48f,1)*s.rimRise));
            }
            return mountains.ToArray();
        }
        static float Height(float x,float z,CoralReefSettings s,Vector4[] hills,Vector4[] mountains)
        {
            float offset=s.seed*.0137f;
            float radius=new Vector2(x,z).magnitude;
            float floor=BasinFloor(x,z,s);
            float hill=0;
            for(int i=0;i<hills.Length;i++){
                var h=hills[i];float dx=x-h.x,dz=z-h.y;
                float q=Mathf.Sqrt(dx*dx+dz*dz*.8f)/h.z;
                q+=(Mathf.PerlinNoise(x*.1f+offset+i,z*.1f+19)-.5f)*.18f;
                float profile=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,1.2f,q));
                hill=Mathf.Max(hill,profile*h.w);
            }
            float outer=0;
            for(int i=0;i<mountains.Length;i++){
                var h=mountains[i];float angle=(i*47+19)*Mathf.Deg2Rad;
                float dx=x-h.x,dz=z-h.y;
                float u=(dx*Mathf.Cos(angle)-dz*Mathf.Sin(angle))/h.z;
                float v=(dx*Mathf.Sin(angle)+dz*Mathf.Cos(angle))/(h.z*(.8f+i%3*.18f));
                float q=Mathf.Sqrt(u*u+v*v);
                q+=(Mathf.PerlinNoise(x*.058f+offset+i*13,z*.058f+19)-.5f)*.65f;
                q+=.075f*Mathf.Sin(Mathf.Atan2(v,u)*(3+i%3)+i);
                float profile=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f+i%3*.055f,.97f+i%2*.08f,q));
                float shelf=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.02f,1.45f,q));
                float cap=.86f+.14f*Mathf.PerlinNoise(x*.14f+12,z*.14f+offset);
                outer=Mathf.Max(outer,Mathf.Max(profile*cap,shelf*(.23f+i%3*.045f))*h.w);
            }
            outer*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(s.basinRadius-8,s.basinRadius+4,radius));
            float mass=Mathf.Max(hill,outer)*s.hillHeightMultiplier;
            float erosion=(Mathf.PerlinNoise(x*.32f+offset,z*.32f+8)-.5f)*.5f*Mathf.Clamp01(mass/5);
            float height=floor+mass+erosion;
            // Existing water surface is Y=0; retain a two-meter submerged margin.
            return Mathf.Min(height,-2-s.terrainBaseY);
        }
        static float Range(System.Random rng,float min,float max){return min+(float)rng.NextDouble()*(max-min);}
        static void Place(GameObject prefab,Vector2 p,Transform parent,Terrain terrain,CoralReefSettings settings,System.Random rng,float multiplier,int species,int palette)
        {
            var coral=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            float y=terrain.SampleHeight(new Vector3(p.x,0,p.y))+terrain.transform.position.y;
            float scale=Range(rng,settings.coralScaleRange.x,settings.coralScaleRange.y)*multiplier;
            coral.transform.position=new Vector3(p.x,y-.025f,p.y);coral.transform.localScale=Vector3.one*scale;
            float u=(p.x-terrain.transform.position.x)/terrain.terrainData.size.x;
            float v=(p.y-terrain.transform.position.z)/terrain.terrainData.size.z;
            var normal=terrain.terrainData.GetInterpolatedNormal(u,v);
            Quaternion slope=Quaternion.FromToRotation(Vector3.up,normal);
            coral.transform.rotation=Quaternion.Slerp(Quaternion.identity,slope,.7f)*Quaternion.Euler(Range(rng,-4,4),Range(rng,0,360),Range(rng,-4,4));
            int materialIndex=species*6+palette;
            if(settings.pastelMaterials!=null && settings.pastelMaterials.Length>materialIndex && settings.pastelMaterials[materialIndex]!=null)
                foreach(var renderer in coral.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=settings.pastelMaterials[materialIndex];
        }
    }
}

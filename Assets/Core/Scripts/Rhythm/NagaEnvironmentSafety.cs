using System;
using System.Collections.Generic;
using AKI.Water;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AKI.Rhythm
{
    /// <summary>Conservative skin envelopes baked once in the editor; no runtime mesh reads/skinning/pathfinding.</summary>
    [DisallowMultipleComponent]
    public sealed class NagaEnvironmentSafety : MonoBehaviour
    {
        [Serializable] public struct SkinEnvelope
        {
            public Transform[] bones;
            public Bounds[] localBounds;
        }
        [SerializeField, HideInInspector] SkinEnvelope[] envelopes = Array.Empty<SkinEnvelope>();
        [Header("Whole body clearance")]
        [Min(.1f)] public float seabedClearance = .65f;
        [Tooltip("Minimum room below the mean water level, including unsampled wave troughs.")]
        [Min(1f)] public float waveReserve = 4.5f;
        [Tooltip("Extra room below the lowest time-predicted wave probe along the body.")]
        [Min(.1f)] public float sampledWaveClearance = 1.25f;
        [Min(.5f)] public float terrainCacheCellMetres = 2f;
        [Tooltip("One shared GPU batch, never synchronous readbacks. Other water probes remain unchanged.")]
        [Range(4,20)] public int waveProbeCount = 16;
        [Tooltip("Steer the orbit away from shoreline cliffs before the full-body guard has to reject a pose.")]
        [Min(5f)] public float preferredWaterColumn = 9f;
        public int RejectedPoses { get; private set; }
        public float LastFloorGap { get; private set; }
        public float LastSurfaceGap { get; private set; }
        public bool HasSafePose => haveSafePose;
        public bool EnvironmentUnavailable { get; private set; }
        public int EnvelopeCount => envelopes.Length;

        Terrain terrain;
        Collider seabed;
        WaterProbe[] probes = Array.Empty<WaterProbe>();
        Bounds[] worldBounds = Array.Empty<Bounds>();
        float[,] floorMax;
        Vector3 terrainOrigin, terrainSize;
        float cacheStep;
        Transform[] savedBones = Array.Empty<Transform>();
        Quaternion[] savedRotations = Array.Empty<Quaternion>();
        Vector3 safePosition;
        Quaternion safeRotation;
        bool configured, haveSafePose;
        Vector3 cachedCentre;
        float nextCentreCheck;

        public Vector3 SafeOrbitCentre(Vector3 player, float footprint)
        {
            if(floorMax==null)return player;
            if(Time.time<nextCentreCheck){cachedCentre.y=player.y;return cachedCentre;}
            nextCentreCheck=Time.time+.6f;
            float level=WaterSurface.FindAt(player)?.WaterLevel??0f;
            float limit=level-waveReserve-preferredWaterColumn;
            cachedCentre=player;
            var region=new Bounds(player,new Vector3(footprint*2f,1f,footprint*2f));
            if(FloorUnder(region)<=limit)return player;
            // A tiny bounded depth-field steering stencil, not navigation/pathfinding. The nearest open
            // arc is preferred; the player can still see/hit the head as it comes around that arc.
            for(float r=4f;r<=64f;r*=2f)for(int d=0;d<8;d++)
            {
                float a=d*Mathf.PI*.25f;var p=player+new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a))*r;
                region.center=p;
                if(FloorUnder(region)<=limit){cachedCentre=p;return p;}
            }
            return player;
        }

        public void Configure(Terrain bottom, Scene scene)
        {
            foreach(var p in probes) WaterProbe.Unregister(p);
            EnvironmentUnavailable=false;
            terrain = bottom;
            if (terrain != null) BuildTerrainCache();
            else foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Seabed") { seabed = root.GetComponentInChildren<Collider>(); break; }
            worldBounds = new Bounds[envelopes.Length];
            var unique = new HashSet<Transform>();
            foreach (var e in envelopes) foreach (var b in e.bones) if (b != null) unique.Add(b);
            savedBones = new Transform[unique.Count]; unique.CopyTo(savedBones);
            savedRotations = new Quaternion[savedBones.Length];
            probes = new WaterProbe[Mathf.Clamp(waveProbeCount,4,20)];
            for (int i=0;i<probes.Length;i++) { probes[i]=new WaterProbe {position=transform.position}; WaterProbe.Register(probes[i]); }
            configured = true;
        }

        void OnDisable() { foreach (var p in probes) WaterProbe.Unregister(p); }

        void BuildTerrainCache()
        {
            terrainOrigin=terrain.transform.position; terrainSize=terrain.terrainData.size;
            cacheStep=Mathf.Max(.5f,terrainCacheCellMetres);
            int nx=Mathf.CeilToInt(terrainSize.x/cacheStep), nz=Mathf.CeilToInt(terrainSize.z/cacheStep);
            floorMax=new float[nz,nx];
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++) floorMax[z,x]=float.NegativeInfinity;
            int n=terrain.terrainData.heightmapResolution;
            var h=terrain.terrainData.GetHeights(0,0,n,n);
            // A bilinear height cell cannot exceed its four corner heights. Include every intersecting
            // cell, not just a few centre rays that could miss a sharp reef inside an envelope.
            for(int z=0;z<n-1;z++) for(int x=0;x<n-1;x++)
            {
                float top=Mathf.Max(Mathf.Max(h[z,x],h[z+1,x]),Mathf.Max(h[z,x+1],h[z+1,x+1]))*terrainSize.y+terrainOrigin.y;
                int x0=Mathf.Clamp(Mathf.FloorToInt(x*terrainSize.x/(n-1)/cacheStep),0,nx-1);
                int x1=Mathf.Clamp(Mathf.FloorToInt((x+1)*terrainSize.x/(n-1)/cacheStep),0,nx-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt(z*terrainSize.z/(n-1)/cacheStep),0,nz-1);
                int z1=Mathf.Clamp(Mathf.FloorToInt((z+1)*terrainSize.z/(n-1)/cacheStep),0,nz-1);
                for(int iz=z0;iz<=z1;iz++) for(int ix=x0;ix<=x1;ix++) floorMax[iz,ix]=Mathf.Max(floorMax[iz,ix],top);
            }
        }

        Bounds EnvelopeBounds(SkinEnvelope e)
        {
            Bounds result=default; bool first=true;
            for(int i=0;i<e.bones.Length;i++)
            {
                if(e.bones[i]==null) continue;
                var m=e.bones[i].localToWorldMatrix; var b=e.localBounds[i]; var ex=b.extents;
                // Exact transformed AABB of a box, without eight corner transforms.
                Vector3 size=new Vector3(Mathf.Abs(m.m00)*ex.x+Mathf.Abs(m.m01)*ex.y+Mathf.Abs(m.m02)*ex.z,
                    Mathf.Abs(m.m10)*ex.x+Mathf.Abs(m.m11)*ex.y+Mathf.Abs(m.m12)*ex.z,
                    Mathf.Abs(m.m20)*ex.x+Mathf.Abs(m.m21)*ex.y+Mathf.Abs(m.m22)*ex.z);
                var wb=new Bounds(m.MultiplyPoint3x4(b.center),size*2f);
                if(first) {result=wb;first=false;} else {result.Encapsulate(wb.min);result.Encapsulate(wb.max);}
            }
            // Each skinned vertex is a positive weighted average of its bone contributions, hence lies
            // inside the union AABB for its exact influencing-bone set. Horns/fins/jaw are included.
            return result;
        }

        float FloorUnder(Bounds b)
        {
            if(floorMax!=null)
            {
                if(b.min.x<terrainOrigin.x || b.max.x>terrainOrigin.x+terrainSize.x || b.min.z<terrainOrigin.z || b.max.z>terrainOrigin.z+terrainSize.z)
                    return float.PositiveInfinity;
                int nx=floorMax.GetLength(1),nz=floorMax.GetLength(0);
                int x0=Mathf.Clamp(Mathf.FloorToInt((b.min.x-terrainOrigin.x)/cacheStep),0,nx-1);
                int x1=Mathf.Clamp(Mathf.FloorToInt((b.max.x-terrainOrigin.x)/cacheStep),0,nx-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((b.min.z-terrainOrigin.z)/cacheStep),0,nz-1);
                int z1=Mathf.Clamp(Mathf.FloorToInt((b.max.z-terrainOrigin.z)/cacheStep),0,nz-1);
                float top=float.NegativeInfinity;
                for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++) top=Mathf.Max(top,floorMax[z,x]);
                return top;
            }
            if(seabed==null) return float.NegativeInfinity;
            float highest=float.NegativeInfinity;
            for(int z=0;z<3;z++) for(int x=0;x<3;x++)
            {
                var p=new Vector3(Mathf.Lerp(b.min.x,b.max.x,x*.5f),100f,Mathf.Lerp(b.min.z,b.max.z,z*.5f));
                if(seabed.Raycast(new Ray(p,Vector3.down),out var hit,500f)) highest=Mathf.Max(highest,hit.point.y);
            }
            return highest;
        }

        bool FitVertical(out float delta)
        {
            float level=WaterSurface.FindAt(transform.position)?.WaterLevel ?? 0f;
            float ceiling=level-waveReserve;
            foreach(var p in probes) if(p.HasData && Time.time-p.SampleTime<.3f) ceiling=Mathf.Min(ceiling,p.PredictedHeight-sampledWaveClearance);
            float lower=float.NegativeInfinity, upper=float.PositiveInfinity;
            LastFloorGap=LastSurfaceGap=float.PositiveInfinity;
            for(int i=0;i<envelopes.Length;i++)
            {
                var b=worldBounds[i]=EnvelopeBounds(envelopes[i]);
                float floor=FloorUnder(b);
                lower=Mathf.Max(lower,floor+seabedClearance-b.min.y);
                upper=Mathf.Min(upper,ceiling-b.max.y);
                LastFloorGap=Mathf.Min(LastFloorGap,b.min.y-floor);
                LastSurfaceGap=Mathf.Min(LastSurfaceGap,ceiling-b.max.y);
            }
            delta=Mathf.Clamp(0f,lower,upper);
            return lower<=upper && !float.IsInfinity(delta) && !float.IsNaN(delta);
        }

        public bool Constrain()
        {
            if(!configured || envelopes.Length==0) return true;
            if(FitVertical(out float lift))
            {
                transform.position+=Vector3.up*lift;
                LastFloorGap+=lift; LastSurfaceGap-=lift;
                RememberPose(); UpdateProbes(); return true;
            }
            RejectedPoses++;
            bool restored=haveSafePose&&RestorePose();
            if(!restored)
            {
                // Bounded encounter-entry placement only, never a runtime path search.
                Vector3 start=transform.position; bool found=false;
                for(float radius=4f;radius<=256f&&!found;radius*=2f)
                    for(int dir=0;dir<8&&!found;dir++)
                    {
                        float a=dir*Mathf.PI*.25f;transform.position=start+new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a))*radius;
                        if(FitVertical(out lift)){transform.position+=Vector3.up*lift;RememberPose();found=true;}
                    }
                if(!found)
                {
                    transform.position=start;EnvironmentUnavailable=true;
                    foreach(var skin in GetComponentsInChildren<SkinnedMeshRenderer>())skin.enabled=false;
                    return false;
                }
            }
            UpdateProbes(); return false;
        }

        void RememberPose()
        {
            safePosition=transform.position; safeRotation=transform.rotation;
            for(int i=0;i<savedBones.Length;i++) savedRotations[i]=savedBones[i].localRotation;
            haveSafePose=true;
        }
        bool RestorePose()
        {
            transform.SetPositionAndRotation(safePosition,safeRotation);
            for(int i=0;i<savedBones.Length;i++) savedBones[i].localRotation=savedRotations[i];
            if(!FitVertical(out float lift))return false;
            transform.position+=Vector3.up*lift;
            LastFloorGap+=lift;LastSurfaceGap-=lift;
            return true;
        }
        void UpdateProbes()
        {
            for(int i=0;i<probes.Length;i++)
            {
                int index=Mathf.RoundToInt(i*(envelopes.Length-1f)/(probes.Length-1f));
                probes[i].position=EnvelopeBounds(envelopes[index]).center;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Bake whole-skin safety envelopes (Editor only)")]
        public void BakeEnvelopes()
        {
            var skin=GetComponentInChildren<SkinnedMeshRenderer>();
            if(skin==null) throw new InvalidOperationException("Missing Naga skin.");
            string path=UnityEditor.AssetDatabase.GetAssetPath(skin.sharedMesh);
            var importer=UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.ModelImporter;
            bool readable=importer!=null && importer.isReadable;
            try
            {
                if(importer!=null&&!readable){importer.isReadable=true;importer.SaveAndReimport();}
                var mesh=skin.sharedMesh;var v=mesh.vertices;var bind=mesh.bindposes;
                var perVertex=mesh.GetBonesPerVertex();var weights=mesh.GetAllBoneWeights();
                var groups=new Dictionary<string,SkinEnvelope>(); int offset=0;
                for(int i=0;i<v.Length;i++)
                {
                    var indices=new List<int>();
                    for(int j=0;j<perVertex[i];j++)if(weights[offset+j].weight>0f)indices.Add(weights[offset+j].boneIndex);
                    offset+=perVertex[i]; indices.Sort(); string key=string.Join(",",indices);
                    if(indices.Count==0)continue;
                    bool fresh=!groups.TryGetValue(key,out var e);
                    if(fresh){e=new SkinEnvelope{bones=new Transform[indices.Count],localBounds=new Bounds[indices.Count]};}
                    for(int j=0;j<indices.Count;j++)
                    {
                        int k=indices[j]; var point=bind[k].MultiplyPoint3x4(v[i]); e.bones[j]=skin.bones[k];
                        if(fresh)e.localBounds[j]=new Bounds(point,Vector3.zero);else e.localBounds[j].Encapsulate(point);
                    }
                    groups[key]=e;
                }
                perVertex.Dispose(); weights.Dispose();
                var list=new List<SkinEnvelope>(groups.Values);
                list.Sort((a,b)=>transform.InverseTransformPoint(EnvelopeBounds(a).center).z.CompareTo(transform.InverseTransformPoint(EnvelopeBounds(b).center).z));
                envelopes=list.ToArray();UnityEditor.EditorUtility.SetDirty(this);
            }
            finally {if(importer!=null&&importer.isReadable!=readable){importer.isReadable=readable;importer.SaveAndReimport();}}
        }
#endif
    }
}

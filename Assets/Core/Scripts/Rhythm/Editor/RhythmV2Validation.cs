using System;
using System.Reflection;
using AKI.Water;
using UnityEditor;
using UnityEngine;

namespace AKI.Rhythm.Editor
{
    /// <summary>Explicit editor-only visual geometry audit. Never executes in a player build or on its own.</summary>
    public static class RhythmV2Validation
    {
        [Serializable] public class Report
        {
            public int samples, verticesChecked, terrainViolations, waterViolations, directionChanges, rejectedPoses;
            public float minTerrainGap=999f, minWaveGap=999f, minHeadY=999f, maxHeadY=-999f, travelledMetres;
            public string status="not started";
        }
        static Report result=new Report();
        static double ends, next;
        static Mesh baked;
        static Texture2D[] ocean;
        static Vector3 last;
        static int sign;
        public static string ResultJson => JsonUtility.ToJson(result,true);
        public static void Begin(float seconds=40f)
        {
            Stop(); result=new Report{status="sampling"};ends=EditorApplication.timeSinceStartup+seconds;next=0d;
            baked=new Mesh();ocean=new Texture2D[3];last=Vector3.zero;sign=0;
            EditorApplication.update+=Tick;
        }
        public static void Stop()
        {
            EditorApplication.update-=Tick;
            if(baked!=null) UnityEngine.Object.DestroyImmediate(baked);
            if(ocean!=null)foreach(var t in ocean)if(t!=null)UnityEngine.Object.DestroyImmediate(t);
            if(result.status=="sampling")result.status="complete";
        }
        static Vector3 Disp(Vector2 xz,Vector4 scales)
        {
            Vector3 v=Vector3.zero;
            for(int i=0;i<3;i++)if(ocean[i]!=null)
            {
                var c=ocean[i].GetPixelBilinear(Mathf.Repeat(xz.x/scales[i],1f),Mathf.Repeat(xz.y/scales[i],1f));
                v+=new Vector3(c.r,c.g,c.b);
            }
            return v;
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup>=ends){Stop();return;}
            if(EditorApplication.isPaused||EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+.7d;
            var motion=UnityEngine.Object.FindFirstObjectByType<NagaRhythmMotion>();
            if(motion==null)return;
            var skin=motion.GetComponentInChildren<SkinnedMeshRenderer>();var terrain=Terrain.activeTerrain;
            var water=WaterSurface.FindAt(motion.HeadWorldPosition);var fft=OceanFFT.Active;
            if(fft!=null)
            {
                var previous=RenderTexture.active;
                for(int i=0;i<3;i++)
                {
                    var rt=fft.GetDisplacement(i);if(rt==null)continue;
                    if(ocean[i]==null)ocean[i]=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true){wrapMode=TextureWrapMode.Repeat};
                    RenderTexture.active=rt;ocean[i].ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);ocean[i].Apply(false);
                }
                RenderTexture.active=previous;
            }
            // true is essential here: the user's WaterTest encounter scale is 1.2, not unit scale.
            // This compensates renderer scale in the baked local coordinates before TransformPoint.
            skin.BakeMesh(baked,true);var points=baked.vertices;
            float mean=water!=null?water.WaterLevel:0f;
            foreach(var v in points)
            {
                Vector3 p=skin.transform.TransformPoint(v);
                if(terrain!=null)
                {
                    float gap=p.y-(terrain.SampleHeight(p)+terrain.transform.position.y);
                    result.minTerrainGap=Mathf.Min(result.minTerrainGap,gap);if(gap<-.01f)result.terrainViolations++;
                }
                float level=mean;
                if(fft!=null)
                {
                    Vector2 original=new Vector2(p.x,p.z),q=original;
                    for(int i=0;i<2;i++){var d=Disp(q,fft.LengthScales);q=original-new Vector2(d.x,d.z);}
                    level+=Disp(q,fft.LengthScales).y;
                }
                float surfaceGap=level-p.y;
                result.minWaveGap=Mathf.Min(result.minWaveGap,surfaceGap);if(surfaceGap<-.01f)result.waterViolations++;
            }
            var head=motion.HeadWorldPosition;
            result.minHeadY=Mathf.Min(result.minHeadY,head.y);result.maxHeadY=Mathf.Max(result.maxHeadY,head.y);
            if(result.samples>0)result.travelledMetres+=Vector3.Distance(last,head);last=head;
            float speed=(float)typeof(NagaRhythmMotion).GetField("currentAngularSpeed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(motion);
            int now=Mathf.Abs(speed)>.5f?(int)Mathf.Sign(speed):sign;
            if(sign!=0&&now!=sign)result.directionChanges++;sign=now;
            var safety=motion.GetComponent<NagaEnvironmentSafety>();result.rejectedPoses=safety!=null?safety.RejectedPoses:0;
            result.samples++;result.verticesChecked+=points.Length;
        }
    }
}

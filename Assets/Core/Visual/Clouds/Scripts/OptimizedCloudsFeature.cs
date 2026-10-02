using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AKI.Clouds
{
    public sealed class OptimizedCloudsFeature : ScriptableRendererFeature
    {
        public Shader shader;
        CloudPass pass;
        public static int LastBufferWidth { get; private set; }
        public static int LastBufferHeight { get; private set; }
        public static int LastStepBudget { get; private set; }
        public static int RenderedCameras { get; private set; }
        public static int SkippedUnderwaterCameras { get; private set; }

        public override void Create()
        {
            pass = new CloudPass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var sky = OptimizedCloudSky.Active;
            if (sky == null || sky.material == null || !sky.material.shader.isSupported || pass == null) return;
            var camera = renderingData.cameraData.camera;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            if (camera.cameraType == CameraType.SceneView && !sky.showInSceneView) return;
            if (camera.cameraType == CameraType.Game && camera.scene.IsValid() && camera.scene != sky.gameObject.scene) return;
            if (renderingData.cameraData.renderType != CameraRenderType.Base) return;
            float visibility = sky.GetCloudVisibility(camera);
            if (visibility <= 0)
            { SkippedUnderwaterCameras++; return; }
            float halfCone = Mathf.Atan(Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * Mathf.Sqrt(1 + camera.aspect * camera.aspect));
            if (camera.transform.forward.y < -Mathf.Sin(halfCone) - .03f) return;
            pass.sky = sky;
            renderer.EnqueuePass(pass);
        }

        sealed class CloudPass : ScriptableRenderPass
        {
            public OptimizedCloudSky sky;
            static readonly int CloudBuffer = Shader.PropertyToID("_AKICloudBuffer");
            static readonly int CloudVisibility = Shader.PropertyToID("_CloudVisibility");
            readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
            sealed class PassData { public Material material; public int shaderPass; public float visibility; }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid()) return;
                var sourceDesc = camera.cameraTargetDescriptor;
                int width = Mathf.Max(1, sourceDesc.width / sky.downsample);
                int height = Mathf.Max(1, sourceDesc.height / sky.downsample);
                int longestEdge = Mathf.Max(width, height);
                if (longestEdge > sky.maximumBufferWidth)
                {
                    float scale = (float)sky.maximumBufferWidth / longestEdge;
                    width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                    height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                }
                LastBufferWidth = width; LastBufferHeight = height; LastStepBudget = sky.raySteps; RenderedCameras++;
                var desc = new TextureDesc(width, height)
                {
                    name = "AKI Clouds Low Resolution", colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = DepthBits.None, clearBuffer = false,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                var clouds = graph.CreateTexture(desc);
                using (var builder = graph.AddRasterRenderPass<PassData>("AKI Clouds / Low-resolution volume", out var data))
                {
                    data.material = sky.material; data.shaderPass = 0;
                    // Background only: do not punch foreground depth holes into this small
                    // buffer. Full-resolution composite depth preserves crisp silhouettes.
                    builder.SetRenderAttachment(clouds, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(clouds, CloudBuffer);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.shaderPass, MeshTopology.Triangles, 3, 1));
                }
                using (var builder = graph.AddRasterRenderPass<PassData>("AKI Clouds / Sky-only composite", out var data))
                {
                    data.material = sky.material; data.shaderPass = 1;
                    data.visibility = sky.GetCloudVisibility(camera.camera);
                    builder.UseTexture(clouds, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    // Premultiplied hardware blend: no full-resolution scene-colour copy.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        // Per-camera draw data, not shared-material state; no per-frame block allocation.
                        compositeProperties.SetFloat(CloudVisibility, d.visibility);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.shaderPass, MeshTopology.Triangles, 3, 1, compositeProperties);
                    });
                }
            }
        }
    }
}

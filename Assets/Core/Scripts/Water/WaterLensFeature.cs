using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace AKI.Water
{
    /// <summary>
    /// URP renderer feature: the "camera lens" of the water effects, drawn over the finished frame.
    ///  - a sheet of water sliding off the lens right after surfacing, then drops that run down and dry up,
    ///  - the waterline (meniscus) crossing the lens when the camera is half in the water,
    ///  - running out of air (vignette), passing out (fade to black) and coming to (eyelids, blur).
    /// The pass is only added while <see cref="Active"/> is set (by <see cref="WaterCameraEffects"/>),
    /// so a dry camera away from the surface pays nothing.
    /// Install via the menu AKI/Water/Install Lens Effect.
    /// </summary>
    public class WaterLensFeature : ScriptableRendererFeature
    {
        public Shader shader;

        /// <summary>Set every frame by WaterCameraEffects: the lens is wet or the camera is at the surface.</summary>
        public static bool Active;

        /// <summary>Set by BreathHolding while the suffocation vignette is visible.</summary>
        public static bool BreathActive;

        /// <summary>Set by Blackout while the screen fades, the eyes open and the view comes into focus.</summary>
        public static bool ScreenActive;
        public static bool RhythmHitActive;

        /// <summary>The material the pass draws with; WaterCameraEffects copies the water's settings into it.</summary>
        public static Material SharedMaterial { get; private set; }

        Material material;
        LensPass pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/AKI/WaterLens");
            if (shader == null) return;

            if (material == null) material = CoreUtils.CreateEngineMaterial(shader);
            SharedMaterial = material;

            pass = new LensPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
                requiresIntermediateTexture = true
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!(Active || BreathActive || ScreenActive || RhythmHitActive) || material == null || pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

            pass.material = material;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (SharedMaterial == material) SharedMaterial = null;
            CoreUtils.Destroy(material);
            material = null;
        }

        class LensPass : ScriptableRenderPass
        {
            public Material material;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;

                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_WaterLensColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), "AKI Water Lens");
                resources.cameraColor = destination;
            }
        }
    }
}

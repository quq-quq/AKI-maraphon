using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AKI.Water
{
    /// <summary>
    /// URP renderer feature: the "camera lens" of the water effects, drawn over the finished frame.
    ///  - a sheet of water sliding off the lens right after surfacing, then drops that run down and dry up,
    ///  - the waterline (meniscus) crossing the lens when the camera is half in the water,
    ///  - running out of air (vignette), passing out (fade to black) and coming to (eyelids, blur),
    ///  - under water, the rim of the diving mask's glass.
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

        /// <summary>Set by RhythmBeatFX while the beat glow pulses down from the top edge of the screen.</summary>
        public static bool RhythmBeatActive;

        /// <summary>Set by UnderwaterPostVolume while the diving mask's rim shows (under water).</summary>
        public static bool MaskActive;

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
            if (!(Active || BreathActive || ScreenActive || MaskActive || RhythmHitActive || RhythmBeatActive) || material == null || pass == null) return;
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

            static readonly int WetnessId = Shader.PropertyToID("_WaterLensWetness");
            static readonly int DropsId = Shader.PropertyToID("_WaterLensDrops");
            static readonly int FilmId = Shader.PropertyToID("_WaterLensFilm");
            static readonly int FilmTexelId = Shader.PropertyToID("_WaterLensFilmTexel");

            class FilmData { public Material material; }

            class LensData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle film;
                public Vector4 filmTexel;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;

                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);

                // Water on the lens after surfacing: its height field is smooth, so it is worked out at half resolution
                // (shader pass 1) and the lens pass reads it and its slope from there.
                TextureHandle film = TextureHandle.nullHandle;
                var filmTexel = Vector4.zero;
                // Single-pass lens variants calculate the film inline; only the two-pass variant has a film pass.
                if (material.passCount > 1 && Shader.GetGlobalFloat(WetnessId) * Shader.GetGlobalFloat(DropsId) > 0.001f)
                {
                    TextureDesc filmDesc = desc;
                    filmDesc.name = "_WaterLensFilm";
                    filmDesc.width = Mathf.Max(1, desc.width / 2);
                    filmDesc.height = Mathf.Max(1, desc.height / 2);
                    filmDesc.format = GraphicsFormat.R16G16_SFloat;
                    filmDesc.filterMode = FilterMode.Bilinear;
                    filmDesc.wrapMode = TextureWrapMode.Clamp;
                    filmDesc.clearBuffer = false;
                    film = renderGraph.CreateTexture(filmDesc);
                    filmTexel = new Vector4(1f / filmDesc.width, 1f / filmDesc.height, filmDesc.width, filmDesc.height);

                    using (var builder = renderGraph.AddRasterRenderPass<FilmData>("AKI Water Lens Film", out var data))
                    {
                        data.material = material;
                        builder.SetRenderAttachment(film, 0, AccessFlags.WriteAll);
                        builder.SetRenderFunc((FilmData d, RasterGraphContext ctx) =>
                            Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, 1));
                    }
                }

                desc.name = "_WaterLensColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<LensData>("AKI Water Lens", out var data))
                {
                    data.material = material;
                    data.source = source;
                    data.film = film;
                    data.filmTexel = filmTexel;
                    builder.UseTexture(source);
                    if (film.IsValid()) builder.UseTexture(film);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((LensData d, RasterGraphContext ctx) =>
                    {
                        d.material.SetTexture(FilmId, d.film.IsValid() ? (Texture)(RTHandle)d.film : Texture2D.blackTexture);
                        d.material.SetVector(FilmTexelId, d.filmTexel);
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                    });
                }
                resources.cameraColor = destination;
            }
        }
    }
}

# Optimized volumetric cloud sky (URP 17 RenderGraph)

Already installed in `Assets/Settings/PC_Renderer.asset` and placed in `CoralBottonTest` as `CloudSky_Optimized`. The old skybox, directional sun, water, birds and scene objects are preserved. A reusable prefab is in `Prefabs/CloudSky_Optimized.prefab`; use one enabled sky component per scene.

## Default GPU budget

- Real 3D density raymarch, not billboard cloud planes.
- Quarter resolution per axis, capped at **384 pixels on the longest buffer edge**. At 1920x1080 and 3840x2160 this gives 384x216 (82,944 pixels), rather than raymarching millions of full-resolution pixels.
- Up to **32 view steps**; rays skip the horizon/below-horizon and geometry-covered pixels. March exits early when accumulated opacity is high. Lighting uses one short extra density tap per occupied view step, not a nested light march.
- Two raster passes: low-resolution cloud volume and a depth-masked premultiplied sky composite. No full-resolution scene-color copy, compute simulation, temporal-history buffers, cloud shadow map or reflection-probe updates.
- Clouds stay fully visible throughout the waterline / wave zone. The existing water shader masks genuinely submerged view rays. Below a conservative wave band (at least 6 m by default), a 2 m smoothstep fades the premultiplied cloud composite, then skips both passes entirely. The band follows the local WaterSurface level and material wave settings; no wave simulation or GPU readback is added. Steep downward views, preview/reflection cameras and overlay cameras also skip. Disable the sky GameObject to skip it completely.
- 64x64x64 packed repeating Perlin/Worley noise: **1 MiB**; 128x128 weather map: **64 KiB**. Generated only by the editor builder, not on scene load. Noise assets are uploaded without CPU Read/Write. The default 16:9 low-resolution HDR buffer is about **648 KiB**, transient and managed by RenderGraph.
- No cloud MonoBehaviour Update, per-cloud transforms, physics, raycasts or navigation. GPU time is not guaranteed across hardware: profile the two `AKI Clouds / ...` markers in the target build.

## Controls

Select `CloudSky_Optimized`. The Inspector contains:

- **Ultra Light**: 24 steps, quarter resolution, 320-pixel edge cap.
- **Optimized** (current): 32 steps, quarter resolution, 384-pixel edge cap.
- **Sharper**: 40 steps, half resolution, 640-pixel edge cap (more expensive).
- Wind in metres/second, continuous Evolution, coverage/density, cloud height/thickness and lighting colors.

Clouds occupy the sky layer between 650 and 1150 m. Wind advects the weather and density, while slow vertical/counter-drift through the 3D field changes cloud shape smoothly. There is no texture regeneration or discontinuous frame swapping. This is a distant sky solution viewed from below the layer, not a fly-through cloud/fog system. It deliberately excludes cloud ground shadows and live cloud cubemap reflections for performance.

## Verification and backups

Shader passes compile without errors/warnings. Visual captures at 0/60/120 seconds show drift and shape evolution; the 1280x720 QA view uses a 320x180 cloud buffer. Full-resolution depth masking keeps opaque birds and geometry in front of clouds. Scene and renderer backups plus screenshots are in the Codex task `outputs/OptimizedClouds` folder. No additional packages or RenderGraph compatibility-mode changes were needed.

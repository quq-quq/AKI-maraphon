# Seagulls: optimized preset flight

Already placed and saved in `Assets/Core/Scenes/TestScenes/CoralBottonTest.unity` as `Seagulls_Sky`.

## Assets

- `Models/Seagull.fbx`: one flat 10-triangle mesh and the five existing bones. No archived high-poly bird objects.
- `Textures/Seagull_BaseMap.png`: actual 512x512 texture, alpha cutout, mipmaps, compressed. Black background is removed in the export copy.
- `Materials/Seagull_BaseMap.mat`: URP Unlit, double-sided alpha clipping, no extra texture maps.
- `Seagull_Flap`: one-second closed wing-beat loop, 24 fps.
- `Seagull_Idle`: two-second static open-wing glide loop.
- `Animations/SeagullFlight.controller`: smooth 0.3/0.4-second flap/glide transitions.
- `Prefabs/Seagull.prefab`: reusable single bird, +Z forward. Assign a FlightSplineRoute to make it fly.
- `Prefabs/Seagulls_Sky.prefab`: complete reusable setup with all five routes and birds. Do not add it again to the current scene: it is already there.
- `Paths/SkyRoute_01..05.asset`: closed Catmull-Rom routes, lengths about 511/595/679/764/847 m. Heights about 19–41 m above the water plane at Y=0.

## Editing routes

Select `Seagulls_Sky/FlightRoute_01` (or another route). Enable Scene Gizmos. Move its numbered control-point handles, then press **Bake Flight Spline** in its Inspector. Route assets are shared by the scene and reusable sky prefab. Bake after changing points in the asset Inspector too.

Each route is pre-baked in the editor to 512 equally spaced position/orientation samples. No Unity Splines package is required. The editor-only bake includes arc-length sampling and modest turn banking. Gameplay only interpolates the stored samples at a constant speed; no navigation, path generation, raycasts, physics or per-frame array creation.

Birds fly at 9–12.2 m/s, flap for 2.5–3.1 seconds, then glide for 8–12.4 seconds, with different phases. Animator work is culled completely when invisible. The five birds together use 50 triangles. Each vertex has exactly one bone influence; renderer skin quality is Bone1. Shadow casting, shadow receiving, light probes and reflection probes are disabled for the birds. Paths are gizmos only and do not render in game.

## Verification

Both animation loops have matching first/last vertex positions. Exported Avatar is valid Generic. Five route seams close exactly; equal-distance sample steps vary by less than 0.03%. Play-mode movement and flap/glide switching were tested for all five birds, including 15 seconds of direct controller simulation. Console had no errors or warnings after testing. The reusable sky prefab was instantiated in an isolated preview scene to verify all five route references and materials.

## Source safety

Desktop `fishes.blend` and `seagull.blend` were not overwritten. Backups of the open fish project, original seagull and Unity scene, plus `Seagull_Prepared.blend` and visual checks, are in the Codex task's `outputs/TropicalFish_Seagulls` folder.

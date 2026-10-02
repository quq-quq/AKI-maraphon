# Rhythm V3 — golden spawn, mixed currents, pink hit mask, seated breathing

Implemented in WaterTest and MenuTest, with common hit settings also saved in RhythmGameplay.prefab. Existing scene overrides (boss scale, oxygen duration, timing, etc.) retained.

## Inspector controls

- **Rhythm Gameplay → RhythmGameFlow → Successful boss hit**: pink colour `(1, .055, .42)`, intensity `.58`, total duration `.55 s` (previously `.35 s`), full-strength hold `.12 s`, inner/outer radius, mask, mask strength/power/invert and optional glow. White mask areas receive the tint; black areas stay clear. Strength 1 uses the complete supplied ornament, including the central face. Strength 0 uses the radial edge vignette. Mask: `Assets/Core/Visual/Textures/vignette_mask.png`, imported as linear/clamped, without mipmaps or NPOT resizing.
- **Water → WaterCurrentView**: Rhythmic Fraction `1/3`; ordinary specks do not shake, pulse or swell to the beat. Existing drift, natural twinkle, beat size boost and timing preserved. Four pooled shadowless point lights, intensity `2`, range `4 m`, illuminate nearby Lit surfaces on beats. Count/intensity/range are adjustable; set count 0 to disable real illumination. No light per individual particle. One particle renderer/draw call using a per-particle Custom1.x flag. Missing specks after a long frame are replenished.
- **Fishman_Cutscene → FishmanCutscene → Seated breathing**: enabled, `12 breaths/min`, spine `.55°`, chest `.8°`, blend-out `.25 s`. Additive cached seated pose, no accumulating rotations, no root or boat movement. Only idle receives the motion; it fades away as the stand/run/dive animation begins.
- Golden fish spawns opposite the camera's horizontal forward direction, at the existing Golden Distance. Ocean bounds/depth correction and existing FishAI/FishAnimation remain in use.

## Unity Play Mode verification

- 650 specks: **433 ordinary / 217 rhythmic**; Custom1.x flags read back from ParticleSystem. Normal specks retained exact base size (maximum difference 0), and continued flowing.
- Golden spawn relative dot product against camera horizontal forward: **-6.000001 m**. FishAI and FishAnimation present; fish position continued changing after spawn.
- During music: four real point lights enabled, shadows None. Golden/silent phase: **0 lights enabled** after release.
- Direct rendered comparison near the actual reef, particles otherwise unchanged: 4 m pooled light range modified **13,704 pixels** versus lights off; maximum combined RGB difference 5/255. Deliberately subtle environment lighting, not merely bright unlit sprites.
- Successful authorized boss hit invoked the actual OnNagaHit handler: hit count incremented, oxygen restored, pink masked pulse visible. Measured full-strength hold **112 ms**, total pulse **552 ms** in live frames (one-frame tolerance).
- Seated chest angle over a complete breathing cycle: **0–0.80015°**, Actor local displacement **0 m**. After starting cutscene: breathing weight **0**.
- Both changed shaders returned **0 ShaderUtil compiler messages**. Project scripts compiled. Console entries observed were UnityEditor GameObjectInspector / TransformInspector / MeshRendererEditor stale-selection exceptions on domain reload, not gameplay script errors.

Captures: `Captures/RhythmV3/SeatedBreathing.png`, `PinkMaskedHit.png`, `CurrentWithLight4m.png`, `CurrentWithoutLight.png`. Before-change copies: `outputs/RhythmV3/Before/`.

No Blender/model changes, new Animator controller, per-frame music analysis or pathfinding were introduced. Test-only teleports, timer extensions, temporary authorized arrow and pause/forced-pulse settings were runtime-only and discarded on leaving Play Mode.

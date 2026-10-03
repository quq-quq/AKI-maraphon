# Rhythm gameplay setup

The scene controller is `RhythmGameFlow`. It connects the existing swimmer, breath, speargun, fish spawner and sound configuration to two recorded rhythm tracks and the golden-fish/Naga encounter. `NagaRhythmMotion` handles visual swimming only; the flow owns music, hit rewards, breath and the ending.

## Scene setup

`Assets/Core/Prefabs/Rhythm/RhythmGameplay.prefab` contains the flow, conductor and music AudioSource. Instances named **Rhythm Gameplay** are present in `MenuTest` and `WaterTest`. Use one active instance per gameplay scene.

For another scene, add that prefab and assign or provide:

- A `FirstPersonSwimController` with a player camera, `BreathHolding`, and `PlayerSpeargun` connected to a `Speargun`.
- The fishing and Naga `RhythmTrack` assets from `Assets/Core/Data/Rhythm`.
- The golden-fish and Naga prefabs, and preferably the project's `SoundConfig`.
- A `FishSpawner`, menu controller and ocean terrain where applicable.

Empty scene references are searched once in the same scene at startup. Assign them explicitly when a scene contains multiple players, spawners or terrains. The flow installs a `RhythmShotGate` on the speargun and connects its own listeners; do not add duplicate gameplay listeners for the same events.

The source flow progresses through `AwaitDive → Fishing → GoldenFish → BossFight → FinalCharge → Ended`. Fishing begins on the menu's water-entry event or when the active swimmer's head goes underwater. The golden fish has its own shooting opportunity after fishing; hitting it starts the boss track. Ignoring it reloads the scene. During the boss, an accepted rhythm shot that actually hits the boss restores a full breath. Surviving the boss track starts the charge and cuts the view to black near the mouth. Running out of air uses the existing `BreathHolding`/`Blackout` handling.

## Gameplay Inspector controls

Select **Rhythm Gameplay** and edit `RhythmGameFlow`.

| Group | Controls and purpose |
| --- | --- |
| Fishing / golden opportunity | `Golden Appears After Seconds` sets the fishing wait; `Golden Ignore Seconds` sets the timeout. Distance controls spawning. `GoldenFish_Rhythm.prefab` uses the ordinary tuna's `FishAI`, skinned mesh and `Fish_Swim` animation at the player's depth, with the original gold material/local bloom. Tune its native FishAI controls for swimming. |
| Shot timing | `Early Window Seconds` and `Late Window Seconds` set acceptance around the recorded pulse. `Spam Quiet Seconds` sets the required gap after every attack attempt, including an attempt during reload. |
| Reload feel | `Shot Cooldown Seconds` and `Reload Materialize Seconds` are applied to the existing speargun. |
| Boss survival | `Boss Breath Seconds` is the full breath duration. Surface refill is blocked during the encounter. A distinct successful launch beat can reward breath only once. |
| Boss appearance | `Naga Scale`, `Naga Orbit Radius`, and `Naga Angular Speed` size and position the monster's mouth-led orbit. |
| Shallow reef safety | `Boss Wave Clearance Metres` reserves player depth below wave troughs. `NagaEnvironmentSafety` separately guards the whole skin above the terrain and under the water. Shallow access is corrected; an encounter with no room for the model is rejected safely. |
| Successful boss hit | `Boss Hit Colour`, `Intensity`, `Fade Seconds` and `Vignette Inner` configure a short yellow light vignette. Only a distinct authorized physical Naga hit triggers it; off-beat/repeated hits cannot. It uses the existing lens pass. |
| Finale | `Final Charge Seconds` sets the lunge duration. `Cut To Black Distance` cuts the view before the mouth reaches the camera head point, or when motion reports arrival. |
| Music | `SoundConfig` volumes and underwater bypass values take priority. The flow's music fields are fallbacks. |
| Events | Fishing, golden appearance, boss start/hit, charge and ending events are available for additional presentation. |

The flow overwrites the Naga component's orbit radius, angular speed and charge duration at boss spawn. Tune those three settings on **Rhythm Gameplay**. Tune the remaining motion settings on `Assets/Core/Prefabs/Rhythm/Naga_Rhythm.prefab`.

In Play mode, the flow Inspector shows its phase, hit count, music position, current beat, last click decision and timing error. **Debug: spawn golden fish** works from Fishing; **Debug: begin boss** works from Fishing or GoldenFish. These buttons shorten the setup path without proving a complete playthrough.

## Beat maps and calibration

`FishingGamelan_Rhythm.asset` and `NagaGamelan_Rhythm.asset` contain offline measured pulse and accent arrays. The asset Inspector displays the timelines. `Beat Times` are authoritative; `Reference BPM` and `First Beat Offset Seconds` describe the analysis and do not generate or shift the gameplay grid. Accents drive a separate accent pulse rather than replacing the main shot pulse.

The conductor schedules audio on `AudioSettings.dspTime` and judges the launch click against that clock. The harpoon keeps the authorization through its physical travel, so the later impact does not need to land on another beat. Holding Attack does not automatically repeat a shot; repeated attempts extend the click quiet-time lockout.

Use `RhythmConductor.Input Latency Seconds` for playback/input calibration. A positive value allows clicks later relative to the scheduled audio. Keep the AudioSource pitch at 1; pitch changes invalidate the recorded timing. If a clip is edited, trimmed or replaced, regenerate or manually update its times. These automatically measured pulses still need listening and musical-phase review for final artistic timing.

The current arrays were aligned to Unity's actual decoded PCM, not just the MP3 duration: Fishing needs no offset; Naga needs +24 ms (1152 samples at 48 kHz). That correction is already baked into the Naga beat and accent arrays. Do not apply it again with input latency.

V2 refines those local metric pulses to percussion attack edges using a 1.995 ms multiband onset envelope, rather than maxima of sustained waveform amplitude. Fishing retains 763 pulses (two weak pulses keep their original tracker timing); Naga retains 423. This is analysis resolution, not a guarantee of 2 ms perceptual accuracy or manually certified downbeats. Both scenes and the gameplay prefab use **100 ms early + 100 ms late = 200 ms total**. Spam protection remains active. The source audio and its speed are unchanged.

The beat is felt through the player camera, not the water particles (`WaterCurrentView` specks only drift with the current). `RhythmBeatFX` on the player camera gives the field of view a short kick on every beat and runs a soft glow pulse along the top edge of the screen, from the centre out to both sides (drawn by the water lens pass). `RhythmGameFlow` adds it at startup when the camera has none; add it to the camera yourself to keep tuned values: kick degrees and attack/release, edge colour, intensity, width, downward travel, trail length and attack/release. Ambient audio and underwater hearing are connected to the player's head-water events; the music source bypasses the listener filter according to `SoundConfig`.

## Naga rig and motion

The imported model faces root-local **+Z**, with **+Y** up, and has its pivot around the middle of the long body. Motion anchors the mouth rather than moving that middle pivot onto the orbit.

The encounter uses `Bone.032` as the head and explicitly assigns `Bone.034` as the lower jaw. The prefab uses positive jaw-local X rotation for opening. The motion script does not infer `Bone.033` or `Bone.034` as a jaw from a generic bone name; a replacement model needs verified references.

`Head Point Local Offset` is in **head-bone coordinates**, not model-root coordinates. The flow calculates it from the root-local mouth marker `(0, 0.2, 24.5)` with `head.InverseTransformPoint(...)`. This conversion matters because the head bone has a rotated local basis. Update that marker when replacing the model or moving the intended mouth point.

| Naga Inspector group | Controls and purpose |
| --- | --- |
| Head and axes | Head/jaw references, mouth offset, and the model's forward/up axes. Preserve +Z/+Y for the current asset. |
| Orbit | Radius and degrees per second; irregular vertical motion combines three non-matching sine periods with random phase. V2 vertical amplitude is 4.5 m and base frequency .09 Hz. Smooth direction reversals occur every 7–13 s, with a 2.2 s angular-speed response. Heading turn rate and pitch limits avoid abrupt flips and excessive long-body tilt. Entry duration blends into the orbit. |
| Body deformation | Empty `Body Bones` discovers the two imported spine chains. An explicit array can select a replacement rig's spine. Head/jaw descendants are excluded. Curve strength and maximum segment bend control how the body wraps behind the head. Horizontal/vertical wave values are per-bone degrees; wave length is in world metres. |
| Charge | Duration, mouth stop distance, jaw-local opening axis and signed opening angle. The mouth accelerates toward the player's head point; the torso stays mostly level and the tail retains curvature to avoid piercing the surface/seabed. The jaw opens early. |

The body uses the existing skinned mesh and captured bone pose. Its renderer bounds are expanded once to accommodate bending. Do not concurrently drive the same bones with an Animator. Keep model-root scale uniform.

`NagaEnvironmentSafety` has 90 editor-baked conservative skin envelopes for the present model, grouped by their exact bone-influence sets. Positive-weight skinning stays inside these bounds, including the fins, crest, horns, tail and opening jaw. Runtime code transforms the small bounds, not mesh vertices. A 2 m conservative max-height cache includes terrain height-cell corner maxima over each footprint, avoiding missed hills between centre rays. Sixteen probes share the existing asynchronous GPU water batch; the mean-water reserve and extra probe clearance guard wave troughs. An inexpensive bounded depth-field stencil gently shifts the orbit toward a deeper open arc near reefs, and invalid poses fall back to the last whole-body-safe pose with a smooth reversal. No NavMesh, skinned MeshCollider updates or runtime audio FFT are added. The terrain cache is built once per encounter; rebuild/reconfigure it if runtime terrain heights change.

If the source mesh, skin weights, bones or bind pose change, rebake envelopes using the safety component's Editor context menu on the prefab. Uniform encounter scaling is handled automatically. The `WaterTest` terrain uses conservative cached height bounds; a legacy non-terrain `Seabed` collider uses a sampled raycast fallback, which is not the same whole-heightfield guarantee.

For a custom controller, configure the rig after setting model scale, then call `BeginOrbit(playerTransform)` and later `BeginCharge(playerHeadTransform)`. Read `HeadWorldPosition`, `ChargeProgress` and `HasReachedHead` for presentation. `PlayHitReaction()` adds a brief ripple. `StopMotion()` restores captured bone pose; `StopMotion(false)` freezes the current pose. `ResetMotion()` also restores the root transform captured during setup.

## Harpoon colliders and visual checks

`RhythmGameFlow` creates inexpensive child capsules on the skinned bones and adds `RhythmSpecialTarget` on the Naga root. Capsules use each bone's local **Y**, matching the current spine segment direction, and inherit the bone motion. `Bone.033` and `Bone.034` are excluded. This avoids rebuilding a skinned MeshCollider each frame, but coverage is an approximation: inspect the visible snout and tail tips if hits anywhere on the silhouette are required. Collider radius, height and centre are currently set in `BeginBossFight()` rather than exposed in the Inspector.

When tuning, check the full curved body at the intended scale, jaw opening from the player's view, shooting at several body sections, clearance near the seabed, and the camera's last second before black. The final head point is a zero-offset child of the player camera; moving that child changes the charge destination. The swimmer and weapon are disabled for the finale so the flow can direct the camera toward the approaching mouth.

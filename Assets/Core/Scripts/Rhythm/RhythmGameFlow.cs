using System.Collections.Generic;
using AKI.Endings;
using AKI.Fish;
using AKI.Menu;
using AKI.Player;
using AKI.Water;
using AKI.Weapons;
using Core.Scripts.Sound;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AKI.Rhythm
{
    /// <summary>One scene-owned rhythm session. No runtime analysis or pathfinding is added.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RhythmGameFlow : MonoBehaviour
    {
        public enum GamePhase { AwaitDive, Fishing, GoldenFish, BossFight, FinalCharge, Ended, Failed }
        [Header("References (empty scene references are found once in this scene)")]
        public RhythmConductor conductor;
        public RhythmTrack fishingTrack;
        public RhythmTrack nagaTrack;
        public FirstPersonSwimController swimmer;
        public BreathHolding breath;
        public PlayerSpeargun playerWeapon;
        public FishSpawner fishSpawner;
        public MainMenuController menu;
        public Terrain oceanTerrain;
        public SoundConfig soundConfig;
        public GameObject goldenFishPrefab;
        public GameObject nagaPrefab;
        [Header("Fishing → golden opportunity")]
        [Tooltip("Elapsed fishing seconds before exactly one golden fish appears. Ambient stays audible.")]
        [Min(5f)] public float goldenAppearsAfterSeconds = 75f;
        [Min(3f)] public float goldenIgnoreSeconds = 25f;
        [Min(2f)] public float goldenDistance = 6f;
        [Header("Rhythm shot / reload feel")]
        [Range(.01f,.2f)] public float earlyWindowSeconds = .1f;
        [Range(.01f,.2f)] public float lateWindowSeconds = .1f;
        [Range(.05f,.5f)] public float spamQuietSeconds = .16f;
        [Min(.1f)] public float shotCooldownSeconds = 1.25f;
        [Min(.1f)] public float reloadMaterializeSeconds = .35f;
        [Header("Boss")]
        [Min(5f)] public float bossBreathSeconds = 30f;
        [Min(4f)] public float nagaOrbitRadius = 12f;
        [Range(10f,180f)] public float nagaAngularSpeed = 55f;
        [Min(.1f)] public float nagaScale = .6f;
        [Tooltip("Extra room below the mean surface at shallow reefs, for moving wave troughs. Only unsafe shallow positions are corrected during the fight.")]
        [Min(0f)] public float bossWaveClearanceMetres = 6.5f;
        [Min(.2f)] public float finalChargeSeconds = 1.2f;
        [Range(.05f,1f)] public float cutToBlackDistance = .45f;
        [Header("Successful boss hit: light vignette")]
        public Color bossHitColour = new Color(1f, .055f, .42f, 1f);
        [Range(0f,1f)] public float bossHitIntensity = .58f;
        [Tooltip("Total pulse duration, including its hold (seconds).")]
        [Min(.05f)] public float bossHitFadeSeconds = .55f;
        [Tooltip("Time at full strength, included in Total Pulse Duration.")]
        [Min(0f)] public float bossHitHoldSeconds = .12f;
        [Range(.1f,.9f)] public float bossHitVignetteInner = .32f;
        [Range(.15f,1f)] public float bossHitVignetteOuter = .7f;
        [Tooltip("White areas are tinted; black areas remain clear. Empty = a radial vignette.")]
        public Texture2D bossHitMask;
        [Tooltip("0 = radial edge vignette; 1 = the supplied mask, including its centre artwork.")]
        [Range(0f,1f)] public float bossHitMaskStrength = 1f;
        [Range(.25f,4f)] public float bossHitMaskPower = .8f;
        public bool bossHitMaskInvert;
        [Tooltip("Optional added glow on top of the dense colour tint.")]
        [Range(0f,1f)] public float bossHitGlow = .15f;
        [Header("Music fallback (SoundConfig wins when present)")]
        [Range(0f,1f)] public float musicVolume = .75f;
        public bool musicBypassesUnderwaterFilter = true;
        [Header("Endings: black screen with subtitles (empty = the old behaviour)")]
        [Tooltip("The golden fish was let go: the view slowly goes black. Empty = the scene restarts.")]
        public EndingData goldenIgnoredEnding;
        [Tooltip("Out of air in the Naga fight. Empty = passing out and coming to.")]
        public EndingData drownedEnding;
        [Tooltip("The Naga track was survived and the Naga swallows the player, after the cut to black.")]
        public EndingData eatenEnding;
        [Header("Events")]
        public UnityEvent onFishingStarted = new UnityEvent();
        public UnityEvent onGoldenAppeared = new UnityEvent();
        public UnityEvent onBossStarted = new UnityEvent();
        public UnityEvent onBossHit = new UnityEvent();
        public UnityEvent onFinalCharge = new UnityEvent();
        public UnityEvent onEnded = new UnityEvent();

        /// <summary>Scene-independent phase moments for listeners that outlive the scene (sound).</summary>
        public static event System.Action FishingStarted;
        /// <summary>The golden fish appeared: the fishing music is over.</summary>
        public static event System.Action FishingEnded;
        public static event System.Action<Transform> NagaAppeared;
        /// <summary>The fight is over: the player ran out of air against the Naga, or it swallowed them.</summary>
        public static event System.Action NagaDisappeared;
        [SerializeField, HideInInspector] GamePhase phase;
        public GamePhase Phase => phase;
        public bool CanShoot => phase == GamePhase.Fishing || phase == GamePhase.GoldenFish || phase == GamePhase.BossFight;
        public int SuccessfulBossHits { get; private set; }
        public NagaRhythmMotion LiveNaga => naga;
        public GameObject LiveGoldenFish => gold;
        public RhythmShotGate ShotGate => gate;
        double phaseStarted;
        GameObject gold;
        NagaRhythmMotion naga;
        RhythmShotGate gate;
        Transform headPoint;
        Camera cameraView;
        readonly HashSet<long> rewardedBeats = new HashSet<long>();
        float originalBreathSeconds;
        bool initialized, ownsEndFade;
        float bossHitPulse;
        Vector3 lastDeepBossPosition;
        bool hasDeepBossPosition;

        void Start()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (swimmer == null) swimmer = root.GetComponentInChildren<FirstPersonSwimController>(true);
                if (fishSpawner == null) fishSpawner = root.GetComponentInChildren<FishSpawner>(true);
                if (menu == null) menu = root.GetComponentInChildren<MainMenuController>(true);
                if (oceanTerrain == null && root.activeSelf) oceanTerrain = root.GetComponentInChildren<Terrain>();
            }
            if (conductor == null) conductor = GetComponent<RhythmConductor>();
            if (swimmer == null || conductor == null || goldenFishPrefab == null || nagaPrefab == null || fishingTrack == null || nagaTrack == null)
            { Debug.LogError("RhythmGameFlow: missing player, conductor, beat maps or special target prefabs.", this); enabled = false; return; }
            breath = breath != null ? breath : swimmer.GetComponent<BreathHolding>();
            playerWeapon = playerWeapon != null ? playerWeapon : swimmer.GetComponent<PlayerSpeargun>();
            if (playerWeapon != null && playerWeapon.gun == null) playerWeapon.gun = swimmer.GetComponentInChildren<Speargun>(true);
            cameraView = swimmer.playerCamera != null ? swimmer.playerCamera : swimmer.GetComponentInChildren<Camera>(true);
            if (playerWeapon == null || playerWeapon.gun == null || cameraView == null || breath == null)
            { Debug.LogError("RhythmGameFlow: player needs camera, speargun and BreathHolding.", this); enabled = false; return; }
            string mapError;
            if (!fishingTrack.IsValid(out mapError) || !nagaTrack.IsValid(out mapError))
            { Debug.LogError("RhythmGameFlow: invalid recorded beat map: " + mapError, this); enabled = false; return; }
            if (soundConfig == null && SoundManager.Instance != null) soundConfig = SoundManager.Instance.Config;
            originalBreathSeconds = breath.maxBreathSeconds;
            fishingTrack.clip.LoadAudioData(); nagaTrack.clip.LoadAudioData(); // prepare before the dive / encounter
            var gun = playerWeapon.gun;
            gate = gun.GetComponent<RhythmShotGate>();
            if (gate == null) gate = gun.gameObject.AddComponent<RhythmShotGate>();
            gate.game = this; gate.conductor = conductor;
            gate.earlyWindowSeconds = earlyWindowSeconds; gate.lateWindowSeconds = lateWindowSeconds; gate.spamQuietSeconds = spamQuietSeconds;
            gun.cooldown = shotCooldownSeconds; gun.materializeSeconds = reloadMaterializeSeconds;
            swimmer.onHeadUnderwater.AddListener(OnHeadDived);
            if (menu != null) menu.onEnteredWater.AddListener(BeginFishing);
            conductor.Completed += OnMusicFinished;
            breath.onOutOfAir.AddListener(OnOutOfAir);
            headPoint = new GameObject("Naga_Final_HeadPoint").transform;
            headPoint.SetParent(cameraView.transform, false);
            // the beat is felt through the camera: a short field of view kick and a pulse from the screen edges
            var beatFx = cameraView.GetComponent<RhythmBeatFX>();
            if (beatFx == null) beatFx = cameraView.gameObject.AddComponent<RhythmBeatFX>();
            if (beatFx.conductor == null) beatFx.conductor = conductor;
            if (fishSpawner != null) { fishSpawner.spawnOnStart = false; fishSpawner.StopSpawning(); }
            initialized = true;
            if (swimmer.gameObject.activeInHierarchy && swimmer.IsHeadUnderwater) OnHeadDived();
        }

        void OnHeadDived()
        {
            if (phase == GamePhase.AwaitDive) BeginFishing();
        }
        [ContextMenu("Debug/Begin fishing")]
        public void BeginFishing()
        {
            if (!initialized || phase != GamePhase.AwaitDive) return;
            if (!StartTrack(fishingTrack, true, soundConfig != null ? soundConfig.FishingGamelan : null)) return;
            SetPhase(GamePhase.Fishing);
            fishSpawner?.StartSpawning(); onFishingStarted.Invoke(); FishingStarted?.Invoke();
        }
        bool StartTrack(RhythmTrack track, bool loop, AudioClipConfig config)
        {
            return conductor.PlayTrack(track, loop, config != null ? config.Volume : musicVolume,
                config != null ? config.IgnoreUnderwater : musicBypassesUnderwaterFilter);
        }
        void SetPhase(GamePhase next) { phase = next; phaseStarted = AudioSettings.dspTime; }
        void Update()
        {
            if (!initialized) return;
            bossHitPulse = Mathf.MoveTowards(bossHitPulse, 0f, Time.unscaledDeltaTime / Mathf.Max(.05f,bossHitFadeSeconds));
            float holdShare = Mathf.Clamp(bossHitHoldSeconds / Mathf.Max(.05f,bossHitFadeSeconds), 0f, .95f);
            float hitStrength = Mathf.Clamp01(bossHitPulse / (1f-holdShare));
            Shader.SetGlobalVector("_RhythmHitVignette", new Vector4(bossHitColour.r,bossHitColour.g,bossHitColour.b,hitStrength*bossHitIntensity));
            Shader.SetGlobalFloat("_RhythmHitInner", bossHitVignetteInner);
            Shader.SetGlobalTexture("_RhythmHitMask", bossHitMask != null ? bossHitMask : Texture2D.whiteTexture);
            Shader.SetGlobalVector("_RhythmHitMaskParams", new Vector4(bossHitMask != null ? bossHitMaskStrength : 0f,
                bossHitMaskPower, bossHitMaskInvert ? 1f : 0f, Mathf.Max(bossHitVignetteInner+.01f,bossHitVignetteOuter)));
            Shader.SetGlobalFloat("_RhythmHitGlow", bossHitGlow);
            WaterLensFeature.RhythmHitActive = bossHitPulse > .001f;
            if (phase == GamePhase.Ended) return;   // nothing may restart the scene under an ending
            if(naga!=null)
            {
                var guard=naga.GetComponent<NagaEnvironmentSafety>();
                if(guard!=null&&guard.EnvironmentUnavailable)
                { swimmer.SurfaceBlocked=false;breath.SurfaceRefillBlocked=false;RestartGame();return; }
            }
            if (phase == GamePhase.BossFight && !EnsureBossDepth(false))
            { swimmer.SurfaceBlocked = false; breath.SurfaceRefillBlocked = false; RestartGame(); return; }
            if (phase == GamePhase.AwaitDive && swimmer.gameObject.activeInHierarchy && swimmer.IsHeadUnderwater) BeginFishing();
            double elapsed = AudioSettings.dspTime - phaseStarted;
            if (phase == GamePhase.Fishing && elapsed >= goldenAppearsAfterSeconds && swimmer.IsHeadUnderwater) SpawnGoldenFish();
            else if (phase == GamePhase.GoldenFish)
            {
                // FishAI and FishAnimation own swimming, just as on an ordinary tuna.
                if (elapsed >= goldenIgnoreSeconds)
                {
                    if (goldenIgnoredEnding != null) ShowEnding(goldenIgnoredEnding);
                    else RestartGame();
                }
            }
            else if (phase == GamePhase.FinalCharge && naga != null)
            {
                Vector3 look = naga.HeadWorldPosition - cameraView.transform.position;
                if (look.sqrMagnitude > .01f) cameraView.transform.rotation = Quaternion.Slerp(cameraView.transform.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-12f * Time.deltaTime));
                if (naga.HasReachedHead || Vector3.Distance(naga.HeadWorldPosition, headPoint.position) <= cutToBlackDistance) EndInBlack();
            }
        }

        [ContextMenu("Debug/Spawn golden fish")]
        public void SpawnGoldenFish()
        {
            if (!initialized || phase != GamePhase.Fishing) return;
            FishingEnded?.Invoke(); // before the music stops, so listeners can still fade it out
            conductor.StopMusic(); fishSpawner?.StopSpawning();
            Vector3 goldCentre = cameraView.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(cameraView.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            gold = Instantiate(goldenFishPrefab, SafeOceanPoint(goldCentre - forward * goldenDistance, .7f), Quaternion.LookRotation(forward));
            gold.name = "GoldenFish_RhythmOpportunity";
            var ai = gold.GetComponent<FishAI>();
            if (ai != null)
            {
                float level = WaterSurface.FindAt(goldCentre)?.WaterLevel ?? 0f;
                ai.SetDepth(Mathf.Max(4f, level-goldCentre.y));
                ai.Init(cameraView.transform);
            }
            var target = gold.AddComponent<RhythmSpecialTarget>(); target.game = this; target.goldenFish = true;
            bool hasBodyCollider = false;
            foreach (var c in gold.GetComponentsInChildren<Collider>()) if (c.enabled && !c.isTrigger) hasBodyCollider = true;
            // GoldenFish_Static includes a trigger for its local glow Volume, not a shootable fish body.
            if (!hasBodyCollider)
            {
                var collider = gold.AddComponent<SphereCollider>(); collider.radius = .8f;
            }
            SetPhase(GamePhase.GoldenFish); onGoldenAppeared.Invoke();
        }
        public void OnGoldenFishHit(HarpoonProjectile arrow)
        {
            if (phase != GamePhase.GoldenFish || arrow == null || gold == null) return;
            // The hit arrow must survive destroying its temporary parent.
            arrow.transform.SetParent(null, true);
            Destroy(gold); gold = null;
            BeginBossFight();
        }

        [ContextMenu("Debug/Begin boss fight")]
        public void BeginBossFight()
        {
            if (!initialized || (phase != GamePhase.GoldenFish && phase != GamePhase.Fishing)) return;
            if (gold != null) Destroy(gold); gold = null;
            fishSpawner?.StopSpawning();
            foreach (var fish in FindObjectsByType<FishAI>(FindObjectsSortMode.None))
                if (fish.gameObject.scene == gameObject.scene) Destroy(fish.gameObject);
            if (!StartTrack(nagaTrack, false, soundConfig != null ? soundConfig.NagaGamelan : null)) { RestartGame(); return; }
            SetPhase(GamePhase.BossFight); rewardedBeats.Clear(); SuccessfulBossHits = 0;
            swimmer.SurfaceBlocked = true; breath.SurfaceRefillBlocked = true;
            hasDeepBossPosition = false;
            if (!EnsureBossDepth(true))
            { swimmer.SurfaceBlocked = false; breath.SurfaceRefillBlocked = false; RestartGame(); return; }
            breath.maxBreathSeconds = bossBreathSeconds;
            breath.RestoreFullBreath();
            GameObject monster = Instantiate(nagaPrefab);
            monster.name = "Naga_RhythmBoss"; monster.transform.localScale *= nagaScale;
            naga = monster.GetComponent<NagaRhythmMotion>();
            if (naga == null) naga = monster.AddComponent<NagaRhythmMotion>();
            var skin = monster.GetComponentInChildren<SkinnedMeshRenderer>();
            Transform head = FindBone(monster, "Bone.032"), jaw = FindBone(monster, "Bone.034");
            naga.ConfigureRig(head, jaw);
            if (head != null)
            {
                Vector3 mouthWorld = monster.transform.TransformPoint(new Vector3(0f, .2f, 24.5f));
                naga.SetMouthOffset(head.InverseTransformPoint(mouthWorld));
            }
            naga.OrbitRadius = nagaOrbitRadius; naga.AngularSpeed = nagaAngularSpeed; naga.ChargeDuration = finalChargeSeconds;
            var safety = monster.GetComponent<NagaEnvironmentSafety>();
            if (safety != null) safety.Configure(oceanTerrain, gameObject.scene);
            var bossTarget = monster.AddComponent<RhythmSpecialTarget>(); bossTarget.game = this;
            if (skin != null)
            {
                // Cheap capsules attached to bones: no per-frame skinned MeshCollider rebuild.
                foreach (Transform bone in skin.bones)
                {
                    if (bone == jaw || bone.name == "Bone.033" || bone.name == "Bone.034") continue;
                    var hit = new GameObject("HarpoonHitCapsule"); hit.layer = monster.layer; hit.transform.SetParent(bone, false);
                    var capsule = hit.AddComponent<CapsuleCollider>(); capsule.direction = 1;
                    capsule.radius = .8f; capsule.height = 1.9f; capsule.center = new Vector3(0f,.5f,0f);
                    // The snout extends beyond the last spine joint; facial bones are intentionally not separate targets.
                    if (bone == head) { capsule.radius = 1.05f; capsule.height = 3.8f; capsule.center = new Vector3(0f,1.1f,0f); }
                    else if (bone.name == "Bone.037") { capsule.height = 2.6f; capsule.center = new Vector3(0f,.7f,0f); }
                }
            }
            Vector3 spawnHead = SafeOceanPoint(cameraView.transform.position - cameraView.transform.forward * (nagaOrbitRadius + 4f), 2f);
            monster.transform.position += spawnHead - naga.HeadWorldPosition;
            naga.BeginOrbit(cameraView.transform);
            if(safety!=null&&!safety.Constrain()&&!safety.HasSafePose)
            {
                Destroy(monster);naga=null;swimmer.SurfaceBlocked=false;breath.SurfaceRefillBlocked=false;
                Debug.LogWarning("RhythmGameFlow: no room for the whole Naga below water and above the seabed; restarting safely.",this);
                RestartGame();return;
            }
            onBossStarted.Invoke(); NagaAppeared?.Invoke(monster.transform);
        }
        static Transform FindBone(GameObject root, string name)
        { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
        bool DeepBossPoint(Vector3 p, out Vector3 safe)
        {
            safe = p;
            var water = WaterSurface.FindAt(p);
            float top = (water != null ? water.WaterLevel : 0f) - swimmer.eyeHeight - swimmer.cursedHeadDepth - bossWaveClearanceMetres;
            float floor = float.NegativeInfinity;
            if (oceanTerrain != null)
            {
                Vector3 o = oceanTerrain.transform.position, size = oceanTerrain.terrainData.size;
                safe.x = Mathf.Clamp(p.x, o.x+2f, o.x+size.x-2f); safe.z = Mathf.Clamp(p.z, o.z+2f, o.z+size.z-2f);
                floor = oceanTerrain.SampleHeight(safe) + o.y + .15f;
                if (floor > top) return false;
            }
            safe.y = Mathf.Clamp(p.y, floor, top);
            return true;
        }
        bool EnsureBossDepth(bool entering)
        {
            Vector3 current = swimmer.transform.position, safe;
            bool deepHere = DeepBossPoint(current, out safe);
            bool withinBounds = Mathf.Abs(safe.x-current.x) < .01f && Mathf.Abs(safe.z-current.z) < .01f;
            if (deepHere && !entering && withinBounds) { lastDeepBossPosition = safe; hasDeepBossPosition = true; return true; }
            if (!deepHere)
            {
                if (!(hasDeepBossPosition && DeepBossPoint(lastDeepBossPosition, out safe)))
                {
                    bool found = false;
                    // Bounded fallback only on encounter entry / unsafe shallows. No paths or ongoing search.
                    for (float radius = 4f; radius <= 512f && !found; radius *= 2f)
                        for (int direction = 0; direction < 8 && !found; direction++)
                        {
                            float angle = direction * Mathf.PI * .25f;
                            found = DeepBossPoint(current + new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle))*radius, out safe);
                        }
                    if (!found)
                    { Debug.LogWarning("RhythmGameFlow: terrain has no safe submerged boss location; restarting instead of entering the seabed.", this); return false; }
                }
            }
            var controller = swimmer.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            swimmer.transform.position = safe;
            if (wasEnabled) controller.enabled = true;
            lastDeepBossPosition = safe; hasDeepBossPosition = true;
            return true;
        }
        public void OnNagaHit(HarpoonProjectile arrow)
        {
            if (phase != GamePhase.BossFight || arrow == null || !arrow.IsRhythmShot || arrow.RhythmSession != conductor.Session) return;
            if (!rewardedBeats.Add(arrow.RhythmBeat)) return;
            SuccessfulBossHits++; breath.RestoreFullBreath(); naga?.PlayHitReaction();
            bossHitPulse = 1f;
            onBossHit.Invoke();
        }
        void OnMusicFinished()
        {
            if (phase != GamePhase.BossFight || breath.IsOutOfAir) return;
            SetPhase(GamePhase.FinalCharge);
            // No escape/refill after surviving the track. Freeze breath for the final cinematic second.
            breath.enabled = false; swimmer.SurfaceBlocked = true;
            swimmer.enabled = false; playerWeapon.enabled = false;
            // CameraShake owns localRotation in LateUpdate even at zero shake; relinquish it for the final look-at.
            var shake = cameraView.GetComponent<CameraShake>();
            if (shake != null) shake.enabled = false;
            naga.BeginCharge(headPoint); onFinalCharge.Invoke();
        }
        void EndInBlack()
        {
            SetPhase(GamePhase.Ended); naga.StopMotion(false);
            NagaDisappeared?.Invoke();
            ownsEndFade = true; Shader.SetGlobalFloat("_ScreenFade", 1f); WaterLensFeature.ScreenActive = true;
            if (eatenEnding != null) EndingScreen.Show(eatenEnding);
            onEnded.Invoke();
        }
        void OnOutOfAir()
        {
            if (phase == GamePhase.Failed || phase == GamePhase.Ended) return;
            if (naga != null) NagaDisappeared?.Invoke(); // before the music stops, so listeners can still fade it out
            if (phase == GamePhase.BossFight && drownedEnding != null) { ShowEnding(drownedEnding); return; }
            SetPhase(GamePhase.Failed); conductor.StopMusic(); fishSpawner?.StopSpawning();
            naga?.StopMotion(false);
        }
        // The game is over for good: no music, no shooting, no passing out (BreathHolding reads that flag right
        // after its out-of-air event, so the ending replaces the reload), and the ending's subtitles on black.
        void ShowEnding(EndingData ending)
        {
            SetPhase(GamePhase.Ended); conductor.StopMusic(); fishSpawner?.StopSpawning();
            naga?.StopMotion(false);
            breath.passOutWhenOutOfAir = false; playerWeapon.enabled = false;
            EndingScreen.Show(ending);
            onEnded.Invoke();
        }
        [ContextMenu("Restart game")]
        public void RestartGame()
        {
            if (!Application.isPlaying || !initialized || Blackout.IsRunning) return;
            conductor.StopMusic(); SetPhase(GamePhase.Failed); fishSpawner?.StopSpawning();
            Blackout.ReloadScene(.3f, 2f);
        }
        Vector3 SafeOceanPoint(Vector3 p, float clearance)
        {
            var water = WaterSurface.FindAt(p);
            float top = water != null ? water.WaterLevel - 1.5f : -1.5f;
            if (oceanTerrain != null)
            {
                Vector3 o = oceanTerrain.transform.position, size = oceanTerrain.terrainData.size;
                p.x = Mathf.Clamp(p.x, o.x+2f, o.x+size.x-2f); p.z = Mathf.Clamp(p.z, o.z+2f, o.z+size.z-2f);
                float bottom = oceanTerrain.SampleHeight(p) + o.y + clearance;
                p.y = Mathf.Clamp(p.y, Mathf.Min(bottom,top), top);
            }
            else p.y = Mathf.Min(p.y,top);
            return p;
        }
        void OnDestroy()
        {
            Shader.SetGlobalVector("_RhythmHitVignette", Vector4.zero); WaterLensFeature.RhythmHitActive = false;
            if (!initialized) return;
            conductor.Completed -= OnMusicFinished;
            if (menu != null) menu.onEnteredWater.RemoveListener(BeginFishing);
            if (swimmer != null) { swimmer.onHeadUnderwater.RemoveListener(OnHeadDived); swimmer.SurfaceBlocked = false; }
            if (breath != null) { breath.onOutOfAir.RemoveListener(OnOutOfAir); breath.maxBreathSeconds = originalBreathSeconds; breath.SurfaceRefillBlocked = false; }
            if (ownsEndFade && !Blackout.IsRunning) { Shader.SetGlobalFloat("_ScreenFade",0f); WaterLensFeature.ScreenActive = false; }
        }
    }
}

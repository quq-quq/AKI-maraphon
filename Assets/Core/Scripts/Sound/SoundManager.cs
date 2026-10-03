using System.Collections;
using System.Collections.Generic;
using AKI.Fish;
using AKI.Menu;
using AKI.Player;
using AKI.Rhythm;
using AKI.Weapons;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.Scripts.Sound
{
    /// <summary>
    /// All game sound. Its private handlers subscribe in OnEnable to static game events (the manager outlives scene
    /// reloads, so it never listens to one scene's objects) and unsubscribe in OnDisable:
    /// <list type="bullet">
    /// <item>OnStartGame — SceneManager.sceneLoaded</item>
    /// <item>OnMenuEnded — MainMenuController.MenuEnded (he gets up to dive)</item>
    /// <item>OnFishingStarted / OnFishingEnded — RhythmGameFlow.FishingStarted / FishingEnded (golden fish)</item>
    /// <item>OnWaterDown — MainMenuController.EnteredWater, FirstPersonSwimController.HeadUnderwater</item>
    /// <item>OnWaterUp — FirstPersonSwimController.HeadAboveWater</item>
    /// <item>OnSwimming — FirstPersonSwimController.SwimStroke</item>
    /// <item>OnHarpoomTrigger — PlayerSpeargun.TriggerPulled</item>
    /// <item>OnArrowFired / OnArrowDissapeared / OnTargetHit — HarpoonProjectile.Launched / Vanished / TargetHit</item>
    /// <item>OnFishSpawned / OnFishDied — FishAI.Spawned / Harpooned</item>
    /// <item>OnNagaAppered / OnNagaDissapeared — RhythmGameFlow.NagaAppeared / NagaDisappeared</item>
    /// </list>
    /// Fishing and Naga music: where a <see cref="RhythmConductor"/> plays them (beat-synced), it owns them and this
    /// manager does not start a second copy.
    /// </summary>
    public partial class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }
        public SoundConfig Config => _soundConfig;

        [SerializeField] private SoundConfig _soundConfig;
        [SerializeField] private Transform _wavesTransform;
        [SerializeField] private Transform _seagoolTransform;
        [SerializeField] private WaterListenerAudio _underwaterAudio = new WaterListenerAudio();
        [Tooltip("Seconds music takes to fade out, and the next music to fade in, when it changes.")]
        [SerializeField, Min(0f)] private float _musicFadeSeconds = 1.5f;
        [Tooltip("The trigger and the shot happen in the player's hands and reach the ear through the body: under water " +
                 "they skip the water's muffling (which leaves nothing of a click) and are only softened to this cutoff (Hz).")]
        [SerializeField, Min(500f)] private float _inHandsUnderwaterCutoff = 3500f;
        [Tooltip("The player's own strokes under water: a soft swish around him, muffled to this cutoff (Hz) with a " +
                 "light watery reverb instead of the listener's heavy muffling.")]
        [SerializeField, Min(300f)] private float _strokesUnderwaterCutoff = 1400f;

        [Header("Creatures under water")]
        [Tooltip("The Naga is in the water with the player: its sounds skip the listener's muffling " +
                 "(which leaves only a hum at 450 Hz) and get their own gentler one: muffled to this cutoff (Hz) with a " +
                 "light watery reverb, so they sound under water and still carry.")]
        [SerializeField, Min(500f)] private float _creaturesUnderwaterCutoff = 1800f;
        [Tooltip("The tuna's own underwater muffling (Hz): a swish heard through the water, duller than the Naga.")]
        [SerializeField, Min(300f)] private float _tunaUnderwaterCutoff = 1100f;
        [Tooltip("A tuna is heard at full volume up to X metres and fades out linearly to Y metres. X stays below the " +
                 "~6 m it circles at, so it gets louder and quieter as it swims nearer and away.")]
        [SerializeField] private Vector2 _tunaHearing = new Vector2(2f, 25f);
        [Tooltip("The Naga is far bigger and louder: full volume up to X metres, silent at Y metres.")]
        [SerializeField] private Vector2 _nagaHearing = new Vector2(15f, 120f);

        private AudioSource _menuGamelanSource;
        private AudioSource _fishingGamelanSource;
        private AudioSource _nagaGamelanSource;
        private AudioSource _waterAmbientSource;
        private AudioSource _nagaSwimmingSource;
        private AudioSource _arrowMoveSource;
        private AudioSource _wavesSource;
        private AudioSource _seagullSource;
        private Coroutine _nagaCoroutine;
        private bool _headUnderwater;
        private bool _subscribed;
        private int _startedSceneHandle;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // Blackout reloads the gameplay scene. Keep the singleton; it takes this scene's anchors and
                // restarts the scene sounds from sceneLoaded (OnStartGame).
                Instance.BindScene(_soundConfig, _wavesTransform, _seagoolTransform);
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            if (_soundConfig == null)
            {
                Debug.LogWarning("SoundManager: no SoundConfig assigned, sounds are off.", this);
                return;
            }

            SceneManager.sceneLoaded += OnStartGame;
            MainMenuController.MenuEnded += OnMenuEnded;
            MainMenuController.EnteredWater += OnWaterDown;
            RhythmGameFlow.FishingStarted += OnFishingStarted;
            RhythmGameFlow.FishingEnded += OnFishingEnded;
            RhythmGameFlow.NagaAppeared += OnNagaAppered;
            RhythmGameFlow.NagaDisappeared += OnNagaDissapeared;
            FirstPersonSwimController.HeadUnderwater += OnWaterDown;
            FirstPersonSwimController.HeadAboveWater += OnWaterUp;
            FirstPersonSwimController.SwimStroke += OnSwimming;
            PlayerSpeargun.TriggerPulled += OnHarpoomTrigger;
            HarpoonProjectile.Launched += OnArrowFired;
            HarpoonProjectile.Vanished += OnArrowDissapeared;
            HarpoonProjectile.TargetHit += OnTargetHit;
            FishAI.Spawned += OnFishSpawned;
            FishAI.Harpooned += OnFishDied;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                SceneManager.sceneLoaded -= OnStartGame;
                MainMenuController.MenuEnded -= OnMenuEnded;
                MainMenuController.EnteredWater -= OnWaterDown;
                RhythmGameFlow.FishingStarted -= OnFishingStarted;
                RhythmGameFlow.FishingEnded -= OnFishingEnded;
                RhythmGameFlow.NagaAppeared -= OnNagaAppered;
                RhythmGameFlow.NagaDisappeared -= OnNagaDissapeared;
                FirstPersonSwimController.HeadUnderwater -= OnWaterDown;
                FirstPersonSwimController.HeadAboveWater -= OnWaterUp;
                FirstPersonSwimController.SwimStroke -= OnSwimming;
                PlayerSpeargun.TriggerPulled -= OnHarpoomTrigger;
                HarpoonProjectile.Launched -= OnArrowFired;
                HarpoonProjectile.Vanished -= OnArrowDissapeared;
                HarpoonProjectile.TargetHit -= OnTargetHit;
                FishAI.Spawned -= OnFishSpawned;
                FishAI.Harpooned -= OnFishDied;
                _subscribed = false;
            }

            if (_nagaCoroutine != null)
                StopCoroutine(_nagaCoroutine);
            _nagaCoroutine = null;

            _underwaterAudio.Reset();
        }

        // The first scene may finish loading before OnEnable subscribed: start it here (once).
        private void Start()
        {
            if (Instance == this && _soundConfig != null) OnStartGame(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void Update()
        {
            _underwaterAudio.Tick();
        }

        // The rhythm conductor plays the fishing / Naga music in sync with its beat map.
        private static bool RhythmOwnsMusic => RhythmConductor.Active != null && RhythmConductor.Active.IsPlaying;

        private Transform Listener => Camera.main != null ? Camera.main.transform : transform;

        #region SoundsRealization
        private void OnStartGame(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.handle == _startedSceneHandle) return;
            _startedSceneHandle = scene.handle;

            // a reloaded scene starts from scratch: nothing of the last run keeps playing
            OnFishingEnded();
            OnNagaDissapeared();
            StopLoop(ref _menuGamelanSource);
            StopLoop(ref _waterAmbientSource);
            StopLoop(ref _arrowMoveSource);
            StopLoop(ref _wavesSource);
            StopLoop(ref _seagullSource);
            _headUnderwater = false;
            _underwaterAudio.Reset();

            _wavesSource = PlayLoopSound(_soundConfig.WavesSound, _wavesTransform != null ? _wavesTransform : Listener);
            _seagullSource = PlayLoopSound(_soundConfig.SeagoolSound, _seagoolTransform != null ? _seagoolTransform : Listener);
            // menu music only where there is a menu (gameplay test scenes start straight in the water)
            if (FindFirstObjectByType<MainMenuController>() != null)
            {
                _menuGamelanSource = PlayLoopSound(_soundConfig.MenuGamelan, Listener);
                FadeIn(_menuGamelanSource);
            }
        }

        private void BindScene(SoundConfig config, Transform waves, Transform seagulls)
        {
            if (config != null) _soundConfig = config;
            _wavesTransform = waves;
            _seagoolTransform = seagulls;
        }

        private void OnMenuEnded()
        {
            FadeOutLoop(ref _menuGamelanSource);
        }

        public void TakeRhythmMusicControl()
        {
            FadeOutLoop(ref _menuGamelanSource);
            FadeOutLoop(ref _fishingGamelanSource);
            FadeOutLoop(ref _nagaGamelanSource);
        }

        public void SetHeadUnderwater(bool underwater)
        {
            if (underwater) OnWaterDown();
            else OnWaterUp();
        }

        private void OnFishingStarted()
        {
            FadeOutLoop(ref _menuGamelanSource);

            if (RhythmOwnsMusic)
                FadeIn(RhythmConductor.Active.musicSource);
            else if (_fishingGamelanSource == null)
            {
                _fishingGamelanSource = PlayLoopSound(_soundConfig.FishingGamelan, Listener);
                FadeIn(_fishingGamelanSource);
            }
        }

        private void OnFishingEnded()
        {
            FadeOutLoop(ref _fishingGamelanSource);
            FadeOutRhythmMusic();
        }

        private void OnWaterDown()
        {
            // the dive and the player's own head report the same moment: one splash
            if (_headUnderwater) return;
            _headUnderwater = true;

            _underwaterAudio.Enter();
            if (_waterAmbientSource == null) _waterAmbientSource = PlayLoopSound(_soundConfig.WaterAmbient, Listener);
            PlayRandom(_soundConfig.SplashSound, Listener.position);
        }

        private void OnWaterUp()
        {
            if (!_headUnderwater) return;
            _headUnderwater = false;

            _underwaterAudio.Exit();
            StopLoop(ref _waterAmbientSource);

            PlayRandom(_soundConfig.SplashSound, Listener.position);
            PlayRandom(_soundConfig.BreatheOutSound, Listener.position);
        }

        private void OnArrowFired(Transform arrowTransform)
        {
            StopLoop(ref _arrowMoveSource);
            _arrowMoveSource = PlayLoopSound(_soundConfig.ArrowMoveSound, arrowTransform, true);
            InHands(PlaySound(_soundConfig.ShootSound, Listener.position));
        }

        private void OnArrowDissapeared()
        {
            StopLoop(ref _arrowMoveSource);
        }

        private void OnFishSpawned(Transform fishTransform)
        {
            // rides on the fish and goes with it; there can be two (the last tuna and the golden fish)
            InWater(PlayLoopSound(_soundConfig.FishSwimmingSound, fishTransform, true), _tunaHearing, _tunaUnderwaterCutoff);
        }

        private void OnFishDied(Transform fishTransform)
        {
            StopLoopsOn(fishTransform, _soundConfig.FishSwimmingSound);
            InWater(PlaySound(_soundConfig.FishDeathSound, fishTransform.position), _tunaHearing, _tunaUnderwaterCutoff);
        }

        private void OnNagaAppered(Transform nagaTransform)
        {
            // leftovers of an earlier Naga only; the music that has just started must stay
            StopLoop(ref _nagaSwimmingSource);
            if (_nagaCoroutine != null) StopCoroutine(_nagaCoroutine);
            _nagaCoroutine = null;

            if (RhythmOwnsMusic)
                FadeIn(RhythmConductor.Active.musicSource);
            else if (_nagaGamelanSource == null)
            {
                _nagaGamelanSource = PlayLoopSound(_soundConfig.NagaGamelan, Listener);
                FadeIn(_nagaGamelanSource);
            }
            _nagaSwimmingSource = PlayLoopSound(_soundConfig.NagaSwimmingSound, nagaTransform, true);
            InWater(_nagaSwimmingSource, _nagaHearing, _creaturesUnderwaterCutoff);
            NagaRhythmMotion naga = nagaTransform.GetComponent<NagaRhythmMotion>();

            _nagaCoroutine = StartCoroutine(GrowlLoop());

            IEnumerator GrowlLoop()
            {
                while (nagaTransform != null)
                {
                    // a while between the roars, never the same twice, so it doesn't tick like a clock
                    yield return new WaitForSeconds(_soundConfig.NagaDistanceSeconds * UnityEngine.Random.Range(0.75f, 1.25f));

                    // it roars from its mouth, not from the middle of its body
                    if (nagaTransform != null)
                        InWater(PlayRandom(_soundConfig.NagaSounds, naga != null ? naga.HeadWorldPosition : nagaTransform.position), _nagaHearing, _creaturesUnderwaterCutoff);
                }
                _nagaCoroutine = null;
            }
        }

        private void OnNagaDissapeared()
        {
            FadeOutLoop(ref _nagaGamelanSource);
            FadeOutRhythmMusic();
            StopLoop(ref _nagaSwimmingSource);

            if (_nagaCoroutine != null)
                StopCoroutine(_nagaCoroutine);
            _nagaCoroutine = null;
        }

        private void OnSwimming()
        {
            AudioSource stroke = PlayRandom(_soundConfig.SwimmingSound, Listener.position);
            if (_headUnderwater) Underwater(stroke, _strokesUnderwaterCutoff);
        }

        private void OnHarpoomTrigger()
        {
            InHands(PlaySound(_soundConfig.TriggerSound, Listener.position));
        }

        private void OnTargetHit(Transform targetTransform)
        {
            StopLoop(ref _arrowMoveSource);
            PlaySound(_soundConfig.HitSound, targetTransform.position);
        }

        #endregion

        private AudioSource PlayRandom(List<AudioClipConfig> clips, Vector3 position)
        {
            if (clips == null || clips.Count == 0) return null;
            return PlaySound(clips[Random.Range(0, clips.Count)], position);
        }

        // A sound made in the player's hands: under water it skips the listener's water filter, only softened.
        private void InHands(AudioSource source)
        {
            if (source == null || !_headUnderwater || source.bypassListenerEffects) return;
            source.bypassListenerEffects = true;
            source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = _inHandsUnderwaterCutoff;
        }

        // A creature in the water with the player: sounds under water without losing itself in the listener's
        // muffling, and is heard over the given distances. Linear: Unity's default curve leaves a tenth at 10 m.
        private void InWater(AudioSource source, Vector2 hearing, float cutoff)
        {
            if (source == null) return;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Max(0.1f, hearing.x);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, hearing.y);
            source.spatialBlend = 1f;   // always in the water around the player, never in his head
            source.dopplerLevel = 0f;   // a darting tuna must not warble
            Underwater(source, cutoff);
        }

        // Its own water instead of the listener's: muffled to the cutoff, then a light watery reverb (the listener's
        // Underwater-based settings, quieter), so the sound keeps its volume.
        private static void Underwater(AudioSource source, float cutoff)
        {
            if (source == null || source.bypassListenerEffects) return;
            source.bypassListenerEffects = true;
            source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = cutoff;
            AudioReverbFilter reverb = source.gameObject.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;
            reverb.room = -1600f;
            reverb.roomHF = -4000f;
            reverb.roomLF = 0f;
            reverb.decayTime = 1.1f;
            reverb.decayHFRatio = 0.1f;
            reverb.reflectionsLevel = -449f;
            reverb.reflectionsDelay = 0.007f;
            reverb.reverbLevel = 600f;
            reverb.reverbDelay = 0.011f;
            reverb.diffusion = 100f;
            reverb.density = 100f;
            reverb.hfReference = 5000f;
        }

        private void FadeIn(AudioSource source)
        {
            if (source == null) return;
            DOTween.Kill(source);
            if (_musicFadeSeconds <= 0f) return;
            float volume = source.volume;
            source.volume = 0f;
            DOTween.To(() => source.volume, v => source.volume = v, volume, _musicFadeSeconds)
                .SetTarget(source).SetLink(source.gameObject);
        }

        private void FadeOutLoop(ref AudioSource source)
        {
            if (source == null) return;
            AudioSource fading = source;
            source = null;
            DOTween.Kill(fading);
            if (_musicFadeSeconds <= 0f)
            {
                Destroy(fading.gameObject);
                return;
            }
            DOTween.To(() => fading.volume, v => fading.volume = v, 0f, _musicFadeSeconds)
                .SetTarget(fading).SetLink(fading.gameObject)
                .OnComplete(() => Destroy(fading.gameObject));
        }

        // The conductor stops its beat-synced track at once (its beat clock must end there): a copy picks the
        // track up at the same sample and fades it away.
        private void FadeOutRhythmMusic()
        {
            if (!RhythmOwnsMusic) return;
            AudioSource music = RhythmConductor.Active.musicSource;
            if (music == null || !music.isPlaying || music.clip == null) return;

            AudioSource tail = new GameObject($"MusicFadeOut{music.clip.name}").AddComponent<AudioSource>();
            tail.clip = music.clip;
            tail.volume = music.volume;
            tail.outputAudioMixerGroup = music.outputAudioMixerGroup;
            tail.bypassListenerEffects = music.bypassListenerEffects;
            tail.spatialBlend = 0f;
            tail.timeSamples = music.timeSamples;
            tail.Play();
            FadeOutLoop(ref tail);
        }

        private static void StopLoop(ref AudioSource source)
        {
            if (source != null) Destroy(source.gameObject);
            source = null;
        }

        private static void StopLoopsOn(Transform owner, AudioClipConfig clipConfig)
        {
            if (owner == null || clipConfig == null || clipConfig.Clip == null) return;
            foreach (AudioSource source in owner.GetComponentsInChildren<AudioSource>())
                if (source.loop && source.clip == clipConfig.Clip) Destroy(source.gameObject);
        }

        /// <param name="follow">Rides on <paramref name="anchor"/> (an arrow, a fish, the Naga) and is removed with it.
        /// Otherwise it stays where it was started and belongs to the scene (not a child of the cutscene camera,
        /// which is destroyed at hand-over).</param>
        private AudioSource PlayLoopSound(AudioClipConfig clipConfig, Transform anchor, bool follow = false)
        {
            if (clipConfig == null || clipConfig.Clip == null || anchor == null)
                return null;

            GameObject sourceObj = new GameObject($"LoopAudio{clipConfig.Clip.name}");
            if (follow) sourceObj.transform.SetParent(anchor, false);
            else sourceObj.transform.position = anchor.position;

            AudioSource audioSource = sourceObj.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.spatialBlend = clipConfig.Is3D ? 1f : 0f;
            audioSource.bypassListenerEffects = clipConfig.IgnoreUnderwater;
            audioSource.volume = clipConfig.Volume;
            audioSource.clip = clipConfig.Clip;
            audioSource.Play();

            return audioSource;
        }

        public AudioSource PlaySound(AudioClipConfig clipConfig, Vector3 position)
        {
            if (clipConfig == null || clipConfig.Clip == null) return null;
            GameObject tempObj = new GameObject("TempAudio");
            tempObj.transform.position = position;

            AudioSource audioSource = tempObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = clipConfig.Is3D ? 1f : 0f;
            audioSource.bypassListenerEffects = clipConfig.IgnoreUnderwater;
            audioSource.pitch = clipConfig.GetPitch();
            audioSource.volume = clipConfig.GetVolume();
            audioSource.PlayOneShot(clipConfig.Clip);

            StartCoroutine(DestroyAfterPlay(audioSource, clipConfig.Clip.length));
            return audioSource;
        }

        public void PlaySound(AudioClipConfig clipConfig, Transform parentTransform)
        {
            if (clipConfig == null || clipConfig.Clip == null) return;
            GameObject tempObj = new GameObject("TempAudio");
            tempObj.transform.SetParent(parentTransform, false);

            AudioSource audioSource = tempObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = clipConfig.Is3D ? 1f : 0f;
            audioSource.bypassListenerEffects = clipConfig.IgnoreUnderwater;
            audioSource.pitch = clipConfig.GetPitch();
            audioSource.volume = clipConfig.GetVolume();
            audioSource.PlayOneShot(clipConfig.Clip);

            StartCoroutine(DestroyAfterPlay(audioSource, clipConfig.Clip.length));
        }

        private IEnumerator DestroyAfterPlay(AudioSource source, float clipLength)
        {
            yield return new WaitForSeconds(clipLength / Mathf.Abs(source.pitch));
            if (source != null) Object.Destroy(source.gameObject);
        }
    }
}

using System.Collections;
using UnityEngine;

namespace Core.Scripts.Sound
{
    public partial class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }
        public SoundConfig Config => _soundConfig;
        /// <summary>Only music is transferred to the DSP rhythm clock; ambient and one-shots keep playing.</summary>
        public void TakeRhythmMusicControl()
        {
            OnMenuEnded(); OnFishingEnded(); OnNagaDissapeared();
        }

        [SerializeField] private SoundConfig _soundConfig;
        [SerializeField] private Transform _wavesTransform;
        [SerializeField] private Transform _seagoolTransform;
        [SerializeField] private WaterListenerAudio _underwaterAudio = new WaterListenerAudio();

        private AudioSource _menuGamelanSource;
        private AudioSource _fishingGamelanSource;
        private AudioSource _waterAmbientSource;
        private AudioSource _fishSwimmingSource;
        private AudioSource _nagaSwimmingSource;
        private AudioSource _arrowMoveSource;
        private AudioSource _wavesSource;
        private AudioSource _seagullSource;
        private AudioListener _audiolistener;
        private Coroutine _nagaCoroutine;
        private bool _headUnderwater;

        /// <summary>Scene player drives this; repeated notifications do not create duplicate ambient loops.</summary>
        public void SetHeadUnderwater(bool underwater)
        {
            if (_soundConfig == null || _headUnderwater == underwater) return;
            _headUnderwater = underwater;
            if (underwater) OnWaterDown(); else OnWaterUp();
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // Blackout reloads the gameplay scene. Keep the singleton, but bind the new scene's anchors.
                Instance.RebindScene(_soundConfig, _wavesTransform, _seagoolTransform);
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

            OnMenuStarted();
            OnStartGame(_wavesTransform, _seagoolTransform);
        }

        private void OnDisable()
        {
            if (_nagaCoroutine != null)
                StopCoroutine(_nagaCoroutine);

            _underwaterAudio.Reset();
        }

        private void Update()
        {
            _underwaterAudio.Tick();
        }

        #region SoundsRealization
        private void OnMenuStarted()
        {
            _menuGamelanSource = PlayLoopSound(_soundConfig.MenuGamelan, Camera.main != null ? Camera.main.transform : null);
        }

        private void OnMenuEnded()
        {
            if (_menuGamelanSource != null)
                Destroy(_menuGamelanSource.gameObject);
            _menuGamelanSource = null;
        }

        private void OnFishingStarted()
        {
            _fishingGamelanSource = PlayLoopSound(_soundConfig.FishingGamelan, Camera.main.transform);
        }

        private void OnFishingEnded()
        {
            if (_fishingGamelanSource != null)
                Destroy(_fishingGamelanSource.gameObject);
            _fishingGamelanSource = null;
        }

        private void OnWaterDown()
        {
            _underwaterAudio.Enter();
            var listener = Camera.main != null ? Camera.main.transform : transform;
            if (_waterAmbientSource == null) _waterAmbientSource = PlayLoopSound(_soundConfig.WaterAmbient, listener);
            if (_soundConfig.SplashSound.Count > 0)
                PlaySound(_soundConfig.SplashSound[Random.Range(0, _soundConfig.SplashSound.Count)], listener.position);
        }

        private void OnWaterUp()
        {
            _underwaterAudio.Exit();

            if(_waterAmbientSource != null)
                Destroy(_waterAmbientSource.gameObject);
            _waterAmbientSource = null;

            var listener = Camera.main != null ? Camera.main.transform : transform;
            if (_soundConfig.SplashSound.Count > 0)
                PlaySound(_soundConfig.SplashSound[Random.Range(0, _soundConfig.SplashSound.Count)], listener.position);
            if (_soundConfig.BreatheOutSound.Count > 0)
                PlaySound(_soundConfig.BreatheOutSound[Random.Range(0, _soundConfig.BreatheOutSound.Count)], listener.position);
        }

        private void OnStartGame(Transform wavesTransform, Transform seagoolTransform)
        {
            var fallback = Camera.main != null ? Camera.main.transform : transform;
            _wavesSource = PlayLoopSound(_soundConfig.WavesSound, wavesTransform != null ? wavesTransform : fallback);
            _seagullSource = PlayLoopSound(_soundConfig.SeagoolSound, seagoolTransform != null ? seagoolTransform : fallback);
        }

        private void RebindScene(SoundConfig config, Transform waves, Transform seagulls)
        {
            TakeRhythmMusicControl();
            if (_wavesSource != null) Destroy(_wavesSource.gameObject);
            if (_seagullSource != null) Destroy(_seagullSource.gameObject);
            if (_waterAmbientSource != null) Destroy(_waterAmbientSource.gameObject);
            _waterAmbientSource = null; _headUnderwater = false;
            if (config != null) _soundConfig = config;
            _wavesTransform = waves; _seagoolTransform = seagulls;
            _underwaterAudio.Reset();
            if (_soundConfig == null) return;
            OnMenuStarted(); OnStartGame(waves, seagulls);
        }

        private void OnArrowFired(Transform arrowTransform)
        {
            _arrowMoveSource = PlayLoopSound(_soundConfig.ArrowMoveSound, arrowTransform);
        }

        private void OnArrowDissapeared()
        {
            if (_arrowMoveSource != null)
                Destroy(_arrowMoveSource.gameObject);
            _arrowMoveSource = null;
        }

        private void OnFishSpawned(Transform fishTransform)
        {
            _fishSwimmingSource = PlayLoopSound(_soundConfig.FishSwimmingSound, fishTransform);
        }

        private void OnFishDied()
        {
            if (_fishSwimmingSource != null)
                Destroy(_fishSwimmingSource.gameObject);
            _fishSwimmingSource = null;
        }

        private void OnNagaAppered(Transform NagaTransform)
        {
            _nagaSwimmingSource = PlayLoopSound(_soundConfig.NagaGamelan, Camera.main.transform);

            PlayLoopSound(_soundConfig.NagaSwimmingSound, NagaTransform);
            PlayLoopSound(_soundConfig.SeagoolSound, NagaTransform);

            _nagaCoroutine = StartCoroutine(GrowlLoop());

            IEnumerator GrowlLoop()
            {
                while (true)
                {
                    yield return new WaitForSeconds(_soundConfig.NagaDistanceSeconds);

                    if (_soundConfig.NagaSounds != null && _soundConfig.NagaSounds.Count > 0)
                    {
                        int randomIndex = Random.Range(0, _soundConfig.NagaSounds.Count);
                        PlaySound(_soundConfig.NagaSounds[randomIndex], NagaTransform.position);
                    }
                }
            }
        }

        private void OnNagaDissapeared()
        {
            if (_nagaSwimmingSource != null)
                Destroy(_nagaSwimmingSource.gameObject);
            _nagaSwimmingSource = null;

            if (_nagaCoroutine != null)
                StopCoroutine(_nagaCoroutine);
        }

        private void OnSwimming()
        {
            int randomIndex = Random.Range(0, _soundConfig.SwimmingSound.Count);
            PlaySound(_soundConfig.SwimmingSound[randomIndex], Camera.main.transform.position);
        }

        private void OnHarpoomTrigger()
        {
            int randomIndex = Random.Range(0, _soundConfig.TriggerSound.Count);
            PlaySound(_soundConfig.TriggerSound[randomIndex], Camera.main.transform.position);
        }

        private void OnTargetHit(Transform targetTransform)
        {
            PlaySound(_soundConfig.HitSound, targetTransform.position);
        }

        private void OnFishDeathSound(Transform targetTransform)
        {
            PlaySound(_soundConfig.HitSound, targetTransform.position);
        }

        #endregion

        private AudioSource PlayLoopSound(AudioClipConfig clipConfig, Transform parentTransform)
        {
            // not wired in this scene (no camera yet, no waves / seagull anchor)
            if (clipConfig == null || clipConfig.Clip == null || parentTransform == null)
                return null;

            GameObject sourceObj = new GameObject($"LoopAudio{clipConfig.Clip.name}");
            // Scene-owned loop, not a child of the cutscene camera (that camera is destroyed at hand-over).
            sourceObj.transform.position = parentTransform.position;

            AudioSource audioSource = sourceObj.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.spatialBlend = clipConfig.Is3D ? 1f : 0f;
            audioSource.bypassListenerEffects = clipConfig.IgnoreUnderwater;
            audioSource.volume = clipConfig.Volume;
            audioSource.clip = clipConfig.Clip;
            audioSource.Play();

            return audioSource;
        }

        public void PlaySound(AudioClipConfig clipConfig, Vector3 position)
        {
            if (clipConfig == null || clipConfig.Clip == null) return;
            GameObject tempObj = new GameObject("TempAudio");
            tempObj.transform.position = position;

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

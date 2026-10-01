using System.Collections;
using UnityEngine;

namespace Core.Scripts.Sound
{
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [SerializeField] private SoundConfig _soundConfig;

        private AudioSource _menuGamelanSource;
        private AudioSource _fishingGamelanSource;
        private AudioSource _waterAmbientSource;
        private AudioSource _fishSwimmingSource;
        private AudioSource _nagaSwimmingSource;
        private AudioSource _arrowMoveSource;
        private Coroutine _nagaCoroutine;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            OnMenuStarted();
            OnStartGame();
        }

        private void OnDisable()
        {
            if (_nagaCoroutine != null)
                StopCoroutine(_nagaCoroutine);
        }

        #region SoundsRealization
        private void OnMenuStarted()
        {
            _menuGamelanSource = PlayLoopSound(_soundConfig.MenuGamelan, Camera.main.transform);
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
            _waterAmbientSource = PlayLoopSound(_soundConfig.WaterAmbient, Camera.main.transform);

            int randomIndex = Random.Range(0, _soundConfig.SplashSound.Count);
            PlaySound(_soundConfig.SplashSound[randomIndex], Camera.main.transform.position);

        }

        private void OnWaterUp()
        {
            if(_waterAmbientSource != null)
                Destroy(_waterAmbientSource.gameObject);
            _waterAmbientSource = null;

            int randomIndexSplash = Random.Range(0, _soundConfig.SplashSound.Count);
            PlaySound(_soundConfig.SplashSound[randomIndexSplash], Camera.main.transform.position);

            int randomIndexBreatheOut = Random.Range(0, _soundConfig.BreatheOutSound.Count);
            PlaySound(_soundConfig.BreatheOutSound[randomIndexBreatheOut], Camera.main.transform.position);
        }

        private void OnStartGame(Transform wavesTransform, Transform seagoolTransform)
        {
            PlayLoopSound(_soundConfig.WaterAmbient, wavesTransform);
            PlayLoopSound(_soundConfig.SeagoolSound, seagoolTransform);
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
            PlayLoopSound(_soundConfig.HitSound, targetTransform);
        }

        private void OnFishDeathSound(Transform targetTransform)
        {
            PlayLoopSound(_soundConfig.HitSound, targetTransform);
        }

        #endregion

        private AudioSource PlayLoopSound(AudioClipConfig clipConfig, Transform parentTransform)
        {
            GameObject sourceObj = new GameObject($"LoopAudio{clipConfig.Clip.name}");
            sourceObj.transform.position = parentTransform.position;

            AudioSource audioSource = sourceObj.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.volume = clipConfig.Volume;
            audioSource.Play();

            return audioSource;
        }

        public void PlaySound(AudioClipConfig clipConfig, Vector3 position)
        {
            GameObject tempObj = new GameObject("TempAudio");
            tempObj.transform.position = position;

            AudioSource source = tempObj.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.pitch = clipConfig.GetPitch();
            source.volume = clipConfig.GetVolume();
            source.PlayOneShot(clipConfig.Clip);

            StartCoroutine(DestroyAfterPlay(source, clipConfig.Clip.length));
        }

        private IEnumerator DestroyAfterPlay(AudioSource source, float clipLength)
        {
            yield return new WaitForSeconds(clipLength / Mathf.Abs(source.pitch));
            Object.Destroy(source.gameObject);
        }
    }
}
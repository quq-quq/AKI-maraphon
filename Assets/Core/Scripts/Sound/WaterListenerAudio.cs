using System;
using DG.Tweening;
using UnityEngine;

namespace Core.Scripts.Sound
{
    public partial class SoundManager
    {
        /// <summary>
        /// Underwater hearing. Lives inside <see cref="SoundManager"/> and is driven only by it
        /// (<see cref="OnWaterDown"/> / <see cref="OnWaterUp"/>), nothing else can reach it.
        ///
        /// Puts a low-pass + reverb chain on the current <see cref="AudioListener"/>, so the whole mix is affected:
        ///   - water swallows the highs: heavy low-pass with a slight resonant "boom";
        ///   - everything gets quieter and duller: dry signal is lowered;
        ///   - sound bounces around in the water: short dark reverb tail;
        ///   - the water around the head moves: the cutoff drifts slowly.
        /// Every parameter is derived from one <see cref="_strength"/> slider (0 = no effect, 1 = fully muffled).
        /// Sources with <see cref="AudioClipConfig.IgnoreUnderwater"/> bypass it (AudioSource.bypassListenerEffects).
        /// </summary>
        [Serializable]
        private class WaterListenerAudio
        {
            [Tooltip("How strong the underwater effect is. 0 = sounds like above water, 1 = fully muffled.")]
            [SerializeField, Range(0f, 1f)] private float _strength = 1f;

            // ---- values at strength 1 (strength 0 = untouched sound)
            private const float OpenCutoff = 22000f;           // Hz, filter fully open
            private const float MuffledCutoff = 450f;          // Hz
            private const float MaxResonance = 1.6f;           // Q, 1 = neutral
            private const float MaxDryDrop = -600f;            // mB (-6 dB)
            private const float MinRoom = -2500f;              // mB, reverb barely audible
            private const float MaxRoom = -1000f;              // mB
            private const float MinDecay = 0.9f;               // s
            private const float MaxDecay = 1.5f;               // s
            private const float CutoffWobble = 0.12f;          // fraction of the cutoff

            // going under is instant, water leaves the ears slower
            private const float FadeInSeconds = 0.25f;
            private const float FadeOutSeconds = 0.7f;

            private AudioListener _listener;
            private AudioLowPassFilter _lowPass;
            private AudioReverbFilter _reverb;
            private float _amount;                             // 0 = above water, 1 = under water (tweened)
            private Tween _fadeTween;

            public void Enter()
            {
                FadeTo(1f, FadeInSeconds, Ease.OutQuad);
            }

            public void Exit()
            {
                FadeTo(0f, FadeOutSeconds, Ease.InOutSine);
            }

            /// <summary>Call every frame: applies the current amount and strength (live-tweakable in Play Mode).</summary>
            public void Tick()
            {
                float effect = _amount * _strength;

                if (effect <= 0.0001f)
                {
                    SetFiltersEnabled(false);
                    return;
                }

                if (!EnsureFilters())
                    return;

                SetFiltersEnabled(true);
                Apply(effect);
            }

            /// <summary>Stops the fade and returns the listener to clean sound.</summary>
            public void Reset()
            {
                _fadeTween?.Kill();
                _fadeTween = null;
                _amount = 0f;
                SetFiltersEnabled(false);
            }

            private void FadeTo(float target, float duration, Ease ease)
            {
                // continues from the current amount, so diving again mid-fade never jumps
                _fadeTween?.Kill();
                _fadeTween = DOTween.To(() => _amount, v => _amount = v, target, duration).SetEase(ease);
            }

            private void Apply(float effect)
            {
                float t = Time.unscaledTime;
                float wobble = (Mathf.Sin(t * 1.07f) * 0.6f + Mathf.Sin(t * 2.58f + 1.3f) * 0.4f) * CutoffWobble * effect;

                // exponential blend so the cutoff sweeps evenly across octaves
                float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(MuffledCutoff), effect));
                _lowPass.cutoffFrequency = Mathf.Clamp(cutoff * (1f + wobble), 10f, OpenCutoff);
                _lowPass.lowpassResonanceQ = Mathf.Lerp(1f, MaxResonance, effect);

                // based on the Underwater preset, scaled by the effect
                _reverb.dryLevel = MaxDryDrop * effect;
                _reverb.room = Mathf.Lerp(MinRoom, MaxRoom, effect);
                _reverb.decayTime = Mathf.Lerp(MinDecay, MaxDecay, effect);
            }

            private bool EnsureFilters()
            {
                // the listener changes between scenes, the SoundManager does not
                if (_listener == null || !_listener.isActiveAndEnabled)
                {
                    _listener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
                    _lowPass = null;
                    _reverb = null;
                    if (_listener == null)
                        return false;
                }

                // order matters: muffle first, then reverb the muffled sound
                if (_lowPass == null)
                    _lowPass = GetOrAdd<AudioLowPassFilter>(_listener.gameObject);

                if (_reverb == null)
                {
                    _reverb = GetOrAdd<AudioReverbFilter>(_listener.gameObject);
                    _reverb.reverbPreset = AudioReverbPreset.User;
                    _reverb.roomHF = -4000f;
                    _reverb.roomLF = 0f;
                    _reverb.decayHFRatio = 0.1f;
                    _reverb.reflectionsLevel = -449f;
                    _reverb.reflectionsDelay = 0.007f;
                    _reverb.reverbLevel = 1200f;
                    _reverb.reverbDelay = 0.011f;
                    _reverb.diffusion = 100f;
                    _reverb.density = 100f;
                    _reverb.hfReference = 5000f;
                }

                return true;
            }

            private void SetFiltersEnabled(bool value)
            {
                if (_lowPass != null && _lowPass.enabled != value) _lowPass.enabled = value;
                if (_reverb != null && _reverb.enabled != value) _reverb.enabled = value;
            }

            private static T GetOrAdd<T>(GameObject target) where T : Component
            {
                return target.TryGetComponent(out T component) ? component : target.AddComponent<T>();
            }
        }
    }
}

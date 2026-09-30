using AKI.Water;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace AKI.Player
{
    /// <summary>
    /// Holding your breath under water. While the head is under the surface the air runs out; after a while a dark
    /// vignette closes in and the view slowly fades towards black ("he's feeling bad"). Back above the water
    /// everything clears up smoothly (DOTween). Running out of air is visual only and fires <see cref="onOutOfAir"/>,
    /// so damage or death can be hooked up later.
    /// Drawn by the water lens pass (WaterLensFeature), so it doesn't touch the project's post-processing.
    /// </summary>
    [RequireComponent(typeof(FirstPersonSwimController))]
    public class BreathHolding : MonoBehaviour
    {
        [Tooltip("Seconds the player can stay under water.")]
        [Min(1f)] public float maxBreathSeconds = 30f;
        [Tooltip("Seconds to get the full breath back above water.")]
        [Min(0.1f)] public float refillSeconds = 3f;
        [Tooltip("Part of the breath used up before the vignette starts to show.")]
        [Range(0f, 0.9f)] public float effectStart = 0.35f;
        [Tooltip("Seconds for the vignette and darkness to fade after surfacing.")]
        [Min(0.05f)] public float recoverDuration = 1.5f;

        public UnityEvent onOutOfAir = new UnityEvent();
        public UnityEvent onBreathRestored = new UnityEvent();

        static readonly int EffectId = Shader.PropertyToID("_BreathEffect");

        FirstPersonSwimController swimmer;
        float air = 1f;
        float effect;
        bool outOfAir;
        bool wasUnder;
        Tween recoverTween;

        /// <summary>1 = full lungs, 0 = no air left.</summary>
        public float Air01 => air;

        /// <summary>Strength of the on-screen suffocation effect (0..1).</summary>
        public float Effect => effect;

        public bool IsOutOfAir => outOfAir;

        void Awake()
        {
            swimmer = GetComponent<FirstPersonSwimController>();
        }

        void OnDisable()
        {
            recoverTween?.Kill();
            effect = 0f;
            Push();
        }

        void Update()
        {
            bool under = swimmer.IsHeadUnderwater;

            if (under)
            {
                if (!wasUnder) recoverTween?.Kill();                  // dived again: continue from what is left on screen

                air = Mathf.Max(0f, air - Time.deltaTime / maxBreathSeconds);
                float used = 1f - air;
                float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(effectStart, 1f, used));
                effect = Mathf.Max(effect, target);

                if (air <= 0f && !outOfAir)
                {
                    outOfAir = true;
                    onOutOfAir.Invoke();
                }
            }
            else
            {
                if (wasUnder)
                {
                    // surfaced: the effect fades out on its own curve
                    recoverTween?.Kill();
                    recoverTween = DOTween.To(() => effect, v => effect = v, 0f, recoverDuration)
                        .SetEase(Ease.OutSine)
                        .SetTarget(this);
                }

                air = Mathf.Min(1f, air + Time.deltaTime / refillSeconds);
                if (outOfAir && air >= 1f)
                {
                    outOfAir = false;
                    onBreathRestored.Invoke();
                }
            }

            wasUnder = under;
            Push();
        }

        void Push()
        {
            Shader.SetGlobalFloat(EffectId, effect);
            WaterLensFeature.BreathActive = effect > 0.001f;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}

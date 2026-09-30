using UnityEngine;
using UnityEngine.Events;

namespace AKI.Water
{
    /// <summary>
    /// Put this on the object with the AudioListener (usually the player camera). While the listener is under water
    /// it muffles everything with a low-pass filter and lowers the volume, crossfading smoothly through the surface.
    /// It also exposes the underwater state for your own audio logic (ambience, splashes, mixer snapshots):
    ///   <see cref="IsUnderwater"/>, <see cref="Amount"/>, <see cref="onUnderwaterChanged"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioListener))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public class WaterListenerAudio : MonoBehaviour
    {
        [Header("Muffling")]
        [Tooltip("Low-pass cutoff (Hz) when fully under water.")]
        [Range(200f, 8000f)] public float underwaterCutoff = 900f;
        [Tooltip("Overall volume multiplier when fully under water.")]
        [Range(0f, 1f)] public float underwaterVolume = 0.85f;
        [Tooltip("How quickly the effect follows the water (higher = snappier).")]
        [Min(0.1f)] public float responsiveness = 8f;

        [Header("State")]
        [Tooltip("Invoked when this listener goes below (true) or above (false) the water surface.")]
        public UnityEvent<bool> onUnderwaterChanged = new UnityEvent<bool>();

        AudioLowPassFilter lowPass;
        float amount;
        bool underwater;

        /// <summary>0 = fully above water, 1 = fully under water (smoothed).</summary>
        public float Amount => amount;

        /// <summary>True while this listener is below any water surface.</summary>
        public bool IsUnderwater => underwater;

        void Awake()
        {
            lowPass = GetComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;
        }

        void OnDisable()
        {
            if (lowPass != null) lowPass.cutoffFrequency = 22000f;
            AudioListener.volume = 1f;
        }

        void Update()
        {
            float target = WaterSurface.GetUnderwaterAmount(transform.position);
            amount = Mathf.MoveTowards(amount, target, responsiveness * Time.unscaledDeltaTime);

            // exponential blend so the cutoff sweeps evenly across octaves
            lowPass.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(underwaterCutoff), amount));
            AudioListener.volume = Mathf.Lerp(1f, underwaterVolume, amount);

            bool now = amount > 0.5f;
            if (now != underwater)
            {
                underwater = now;
                onUnderwaterChanged.Invoke(underwater);
            }
        }
    }
}

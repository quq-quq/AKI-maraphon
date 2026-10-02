using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Water
{
    /// <summary>
    /// Put on a global <see cref="Volume"/> holding the underwater grading. Fades its weight in while the camera's
    /// <see cref="WaterCameraEffects"/> reports it is under the wave, so the above-water profile underneath takes
    /// over again on surfacing.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public class UnderwaterPostVolume : MonoBehaviour
    {
        [Tooltip("Camera whose submersion drives the weight. Found on the main camera when empty.")]
        public WaterCameraEffects cameraEffects;
        [Tooltip("Seconds to fade the underwater grading in when diving.")]
        [Min(0f)] public float fadeIn = 0.15f;
        [Tooltip("Seconds to fade it out after surfacing.")]
        [Min(0f)] public float fadeOut = 0.35f;

        Volume volume;
        float weight;

        void OnEnable()
        {
            volume = GetComponent<Volume>();
            if (cameraEffects == null) cameraEffects = GetComponentInParent<WaterCameraEffects>();
            weight = Target();
            volume.weight = weight;
        }

        void LateUpdate()
        {
            if (cameraEffects == null && Camera.main != null) cameraEffects = Camera.main.GetComponent<WaterCameraEffects>();
            float target = Target();
            float time = target > weight ? fadeIn : fadeOut;
            float dt = Application.isPlaying ? Time.deltaTime : 1f;
            weight = time > 0f ? Mathf.MoveTowards(weight, target, dt / time) : target;
            volume.weight = weight;
        }

        float Target() => cameraEffects != null && cameraEffects.Submerged ? 1f : 0f;
    }
}

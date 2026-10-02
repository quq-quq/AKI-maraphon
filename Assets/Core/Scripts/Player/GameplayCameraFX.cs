using AKI.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Player
{
    /// <summary>
    /// Put on the player camera's post-processing object. Fades the gameplay grading volumes in and out:
    ///  - a punch of colour fringes and lens bulge when the speargun fires (<see cref="ShotPunch"/>),
    ///  - the eyes adjusting to the light: a bright, warm glare right after surfacing and a moment of dark after diving,
    ///  - running out of air: colour fringes, a squeezed view and grain pulsing with a heartbeat that speeds up (on top
    ///    of the vignette and darkness the water lens draws for <see cref="BreathHolding"/>).
    /// Each is a global <see cref="Volume"/> with its own profile; this only sets their weights.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameplayCameraFX : MonoBehaviour
    {
        [Tooltip("Whose surfacing and diving drive the eye adjustment. Found in the parents when empty.")]
        public WaterCameraEffects cameraEffects;
        [Tooltip("Whose air drives the low-air grading. Found in the parents when empty.")]
        public BreathHolding breath;

        [Header("Volumes")]
        public Volume shotVolume;
        public Volume surfaceGlareVolume;
        public Volume diveDarkVolume;
        public Volume lowAirVolume;

        [Header("Timing")]
        [Tooltip("Seconds the punch of a shot takes to fade.")]
        [Min(0.01f)] public float shotSeconds = 0.2f;
        [Tooltip("Seconds the eyes take to get used to the daylight after surfacing.")]
        [Min(0.05f)] public float glareSeconds = 1.6f;
        [Tooltip("Seconds the eyes take to get used to the dark after diving.")]
        [Min(0.05f)] public float darkSeconds = 1.1f;
        [Tooltip("Heartbeats per minute when the air starts to run out, and right before passing out.")]
        public Vector2 heartRate = new Vector2(70f, 125f);

        float shot, glare, dark;
        float beatPhase;
        bool wasSubmerged, started;

        /// <summary>A shot was fired: a short punch of colour fringes (strength 0..1).</summary>
        public void ShotPunch(float strength = 1f) => shot = Mathf.Max(shot, Mathf.Clamp01(strength));

        void Awake()
        {
            if (cameraEffects == null) cameraEffects = GetComponentInParent<WaterCameraEffects>();
            if (breath == null) breath = GetComponentInParent<BreathHolding>();
        }

        void OnDisable()
        {
            shot = glare = dark = 0f;
            Apply(0f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (cameraEffects != null)
            {
                bool submerged = cameraEffects.Submerged;
                if (started && submerged != wasSubmerged)
                {
                    if (submerged) { dark = 1f; glare = 0f; }
                    else { glare = 1f; dark = 0f; }
                }
                wasSubmerged = submerged;
                started = true;
            }
            shot = Mathf.Max(0f, shot - dt / shotSeconds);
            glare = Mathf.Max(0f, glare - dt / glareSeconds);
            dark = Mathf.Max(0f, dark - dt / darkSeconds);

            // the heart beats faster as the air runs out; each beat is a quick double thump
            float air = breath != null ? breath.Effect : 0f;
            beatPhase = Mathf.Repeat(beatPhase + dt * Mathf.Lerp(heartRate.x, heartRate.y, air) / 60f, 1f);
            Apply(air);
        }

        void Apply(float air)
        {
            float echo = beatPhase > 0.22f ? Mathf.Exp(-(beatPhase - 0.22f) * 18f) : 0f;
            float thump = Mathf.Exp(-beatPhase * 18f) + 0.6f * echo;
            SetWeight(shotVolume, shot * shot);
            SetWeight(surfaceGlareVolume, glare * glare * (3f - 2f * glare));   // eases out: the glare lingers, then goes
            SetWeight(diveDarkVolume, dark * dark);
            SetWeight(lowAirVolume, air * (0.65f + 0.35f * Mathf.Clamp01(thump)));
        }

        static void SetWeight(Volume volume, float weight)
        {
            if (volume == null) return;
            volume.weight = weight;
            volume.enabled = weight > 0.001f;   // a volume at weight 0 still costs its blending every frame
        }
    }
}

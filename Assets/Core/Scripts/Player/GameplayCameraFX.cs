using AKI.Water;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AKI.Player
{
    /// <summary>
    /// Put on the player camera's post-processing object. Fades the gameplay grading volumes in and out:
    ///  - a punch of colour fringes and lens bulge when the speargun fires (<see cref="ShotPunch"/>),
    ///  - a bubble bursting right on the camera (<see cref="BubblePop"/>): its own volume, made here - the lens bulges
    ///    hard and wobbles back like a popped film, the colours split and a flash of light goes through,
    ///  - the eyes adjusting to the light: a bright, warm glare right after surfacing and a moment of dark after diving,
    ///  - running out of air: colour fringes, a squeezed view and grain pulsing with a heartbeat that speeds up (on top
    ///    of the vignette and darkness the water lens draws for <see cref="BreathHolding"/>).
    /// Each is a global <see cref="Volume"/> with its own profile; this only sets their weights (the bubble's values too).
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

        [Header("A bubble bursting on the camera")]
        [Tooltip("Seconds until the lens has settled again.")]
        [Min(0.1f)] public float bubblePopSeconds = 1.3f;
        [Tooltip("How hard the lens bulges at first (URP Lens Distortion intensity, 0..1).")]
        [Range(0f, 1f)] public float bubblePopBulge = 0.75f;
        [Tooltip("Wobbles per second as the bulge springs back and forth.")]
        [Min(0f)] public float bubblePopWobble = 3.2f;
        [Tooltip("Flash of light through the bursting film (stops of exposure at its brightest).")]
        [Range(0f, 3f)] public float bubblePopFlash = 1.4f;
        [Tooltip("Kick of the view (degrees) and shake (degrees) when it bursts.")]
        public Vector2 bubblePopKick = new Vector2(5f, 1.6f);

        float shot, glare, dark;
        float popTime = float.MaxValue;   // seconds since a bubble burst on the camera
        float beatPhase;
        bool wasSubmerged, started;
        Volume popVolume;
        LensDistortion popBulge;
        ChromaticAberration popFringes;
        ColorAdjustments popFlash;

        /// <summary>A shot was fired: a short punch of colour fringes (strength 0..1).</summary>
        public void ShotPunch(float strength = 1f) => shot = Mathf.Max(shot, Mathf.Clamp01(strength));

        /// <summary>A bubble bursts right on the camera: the shot's lens bulge and colour fringes at full strength,
        /// held a moment longer and settling slowly, and the water swirls over the lens as when diving.</summary>
        public void BubblePop()
        {
            if (popVolume == null) BuildPopVolume();
            popTime = 0f;
            glare = Mathf.Max(glare, 0.6f);   // and the warm glare lingers after the flash
            if (cameraEffects != null) cameraEffects.Swirl();
            CameraShake shake = GetComponentInParent<CameraShake>();
            if (shake != null)
            {
                shake.Kick(new Vector3(-bubblePopKick.x, Random.Range(-1f, 1f) * bubblePopKick.x * 0.5f, Random.Range(-1f, 1f) * bubblePopKick.x * 0.4f));
                shake.Shake(bubblePopKick.y, bubblePopSeconds * 0.5f);
            }
        }

        // A volume of its own above the others: the shot's is far too gentle for this.
        void BuildPopVolume()
        {
            var go = new GameObject("Bubble Pop Volume");
            go.transform.SetParent(transform, false);
            popVolume = go.AddComponent<Volume>();
            popVolume.isGlobal = true;
            popVolume.priority = 20f;
            popVolume.weight = 0f;
            popVolume.enabled = false;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Bubble Pop (runtime)";
            popBulge = profile.Add<LensDistortion>(true);
            popFringes = profile.Add<ChromaticAberration>(true);
            popFlash = profile.Add<ColorAdjustments>();
            popFlash.postExposure.overrideState = true;
            popVolume.profile = profile;
        }

        void UpdatePop(float dt)
        {
            if (popVolume == null) return;
            if (popTime >= bubblePopSeconds)
            {
                SetWeight(popVolume, 0f);
                return;
            }
            popTime += dt;
            float k = Mathf.Clamp01(1f - popTime / bubblePopSeconds);
            float fade = k * k;
            // the film bulges into the lens and springs back past flat a few times, smaller each time
            popBulge.intensity.value = bubblePopBulge * fade * Mathf.Cos(2f * Mathf.PI * bubblePopWobble * popTime);
            popFringes.intensity.value = fade;
            popFlash.postExposure.value = bubblePopFlash * Mathf.Exp(-popTime * 7f);
            SetWeight(popVolume, 1f);
        }

        void Awake()
        {
            if (cameraEffects == null) cameraEffects = GetComponentInParent<WaterCameraEffects>();
            if (breath == null) breath = GetComponentInParent<BreathHolding>();
        }

        void OnDestroy()
        {
            if (popVolume != null) Destroy(popVolume.profile);   // made here, not an asset
        }

        void OnDisable()
        {
            shot = glare = dark = 0f;
            popTime = float.MaxValue;
            if (popVolume != null) SetWeight(popVolume, 0f);
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
            UpdatePop(dt);
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

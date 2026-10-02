using UnityEngine;

namespace AKI.Player
{
    /// <summary>
    /// Kicks and shakes the camera it sits on, by turning it only: the swim controller owns the camera's position
    /// and the pivot's rotation, this component owns the camera's own local rotation.
    /// <see cref="Kick"/> throws the view on a spring that swings it back (a shot's recoil), <see cref="Shake"/> adds
    /// a short jitter that dies away.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraShake : MonoBehaviour
    {
        [Tooltip("How fast the view springs back after a kick (rad/s of the spring).")]
        [Min(1f)] public float kickSpring = 14f;
        [Tooltip("1 = settles without swinging past, less = a little overshoot.")]
        [Range(0.2f, 1.5f)] public float kickDamping = 0.6f;
        [Tooltip("How fast the shake jitters (Hz).")]
        [Min(1f)] public float shakeFrequency = 22f;
        [Tooltip("Never shakes more than this (degrees), whatever is asked.")]
        [Min(0f)] public float maxShake = 3f;

        // peak of an underdamped spring kicked from rest, as a share of v0 / spring
        const float KickPeak = 0.52f;

        Vector3 kick;
        Vector3 kickVelocity;
        float shakeStrength;
        float shakeSeconds;
        float shakeLeft;
        float seed;

        void Awake()
        {
            seed = Random.value * 100f;
        }

        void OnDisable()
        {
            kick = kickVelocity = Vector3.zero;
            shakeLeft = 0f;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Throws the view by about <paramref name="degrees"/> (x = pitch, negative is up), then it swings back.</summary>
        public void Kick(Vector3 degrees)
        {
            kickVelocity += degrees * (kickSpring / KickPeak);
        }

        /// <summary>A jitter of up to <paramref name="degrees"/> that dies away over <paramref name="seconds"/>.</summary>
        public void Shake(float degrees, float seconds)
        {
            // a new shake never cuts a stronger one short
            float now = CurrentShake();
            if (degrees < now) return;
            shakeStrength = Mathf.Min(degrees, maxShake);
            shakeSeconds = shakeLeft = Mathf.Max(seconds, 0.01f);
        }

        float CurrentShake()
        {
            if (shakeLeft <= 0f) return 0f;
            float k = shakeLeft / shakeSeconds;
            return shakeStrength * k * k;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // damped spring back to rest
            float w = kickSpring;
            kickVelocity += (-w * w * kick - 2f * kickDamping * w * kickVelocity) * dt;
            kick += kickVelocity * dt;

            Vector3 jitter = Vector3.zero;
            float amount = CurrentShake();
            if (amount > 0f)
            {
                float t = Time.time * shakeFrequency;
                jitter = new Vector3(Mathf.PerlinNoise(seed, t) - 0.5f,
                                     Mathf.PerlinNoise(seed + 17f, t) - 0.5f,
                                     (Mathf.PerlinNoise(seed + 31f, t) - 0.5f) * 0.6f) * (2f * amount);
                shakeLeft -= dt;
            }

            transform.localRotation = Quaternion.Euler(kick + jitter);
        }
    }
}

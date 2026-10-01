using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// Blood of a hit fish: a dark red cloud with specks bursting out of the wound on every hit, then a trail of blood
    /// that keeps coming from the wound while the fish swims and slowly dries up. Put the VFX_TunaBlood prefab under the
    /// fish and call <see cref="Hit"/> from the harpoon. Every hit makes the bleeding stronger (up to <see cref="maxBleed"/>).
    /// </summary>
    public class TunaBlood : MonoBehaviour
    {
        [Tooltip("Played at the wound on every hit: the blood cloud and the specks.")]
        public ParticleSystem[] hitBurst;
        [Tooltip("Keeps emitting from the wound while the fish swims.")]
        public ParticleSystem bleedTrail;
        [Tooltip("Seconds until a wound stops bleeding.")]
        [Min(0.1f)] public float bleedSeconds = 15f;
        [Tooltip("Bleeding strength over the bleed time (1 = right after the hit).")]
        public AnimationCurve bleedFalloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.3f, 0.7f), new Keyframe(1f, 0f));
        [Tooltip("Repeated hits make the fish bleed harder, up to this strength.")]
        [Min(1f)] public float maxBleed = 2f;

        float trailRateTime;
        float trailRateDistance;
        float hitStrength;
        float sinceHit;
        float bleed;

        /// <summary>Current bleeding strength (0 = not bleeding, 1 = one fresh wound).</summary>
        public float Bleed => bleed;

        public bool IsBleeding => bleed > 0.01f;

        void Awake()
        {
            if (bleedTrail == null) return;
            var emission = bleedTrail.emission;
            trailRateTime = emission.rateOverTimeMultiplier;
            trailRateDistance = emission.rateOverDistanceMultiplier;
            emission.enabled = false;
        }

        /// <summary>
        /// A hit at <paramref name="point"/> (world). <paramref name="direction"/> is where the harpoon was travelling;
        /// the blood spurts back out of the wound against it.
        /// </summary>
        public void Hit(Vector3 point, Vector3 direction)
        {
            Quaternion spurt = direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-direction) : transform.rotation;
            foreach (ParticleSystem ps in hitBurst)
            {
                if (ps == null) continue;
                ps.transform.SetPositionAndRotation(point, spurt);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);   // restart the bursts, keep the old cloud
                ps.Play(false);
            }

            if (bleedTrail != null)
            {
                bleedTrail.transform.position = point;   // under the fish, so the wound moves with it
                if (!bleedTrail.isPlaying) bleedTrail.Play(false);
            }

            hitStrength = Mathf.Min(maxBleed, bleed + 1f);
            sinceHit = 0f;
            Apply();
        }

        public void StopBleeding()
        {
            hitStrength = 0f;
            Apply();
        }

        /// <summary>The fish is gone: stop bleeding and leave the blood in the water until it has faded.</summary>
        public void StopAndDetach()
        {
            StopBleeding();
            transform.SetParent(null, true);
            float life = 0f;
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                life = Mathf.Max(life, ps.main.startLifetime.constantMax);
            }
            Destroy(gameObject, life + 0.5f);
        }

        void Update()
        {
            if (hitStrength <= 0f) return;
            sinceHit += Time.deltaTime;
            if (sinceHit >= bleedSeconds) hitStrength = 0f;
            Apply();
        }

        void Apply()
        {
            bleed = hitStrength > 0f ? hitStrength * Mathf.Max(0f, bleedFalloff.Evaluate(sinceHit / bleedSeconds)) : 0f;
            if (bleedTrail == null) return;
            var emission = bleedTrail.emission;
            emission.enabled = IsBleeding;
            emission.rateOverTimeMultiplier = trailRateTime * bleed;
            emission.rateOverDistanceMultiplier = trailRateDistance * bleed;
        }
    }
}

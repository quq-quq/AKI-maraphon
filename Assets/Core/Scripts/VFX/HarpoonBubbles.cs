using AKI.Water;
using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// Bubbles of a harpoon shot under water: a burst of bubbles and white fizz at the moment of the shot (out of the
    /// muzzle, all along the shaft and in a ring flung out sideways, so the water by the player boils up), then a light
    /// streak of bubbles left behind while it flies. Put the VFX_HarpoonBubbles prefab at the harpoon tip and call
    /// <see cref="Fire"/> when it is shot, <see cref="Stop"/> when it hits something. Particles live in world space,
    /// so the streak stays in the water and rises after the harpoon has gone.
    /// </summary>
    public class HarpoonBubbles : MonoBehaviour
    {
        [Tooltip("Played once on Fire(): the cloud of bubbles and white fizz at the muzzle.")]
        public ParticleSystem[] fireBurst;
        [Tooltip("The ring of bubbles bursting out sideways on the shot (part of Fire Burst): turned and tilted at random every shot.")]
        public Transform shotRing;
        [Tooltip("How far the ring may tilt off square to the harpoon (degrees).")]
        [Range(0f, 60f)] public float ringTilt = 25f;
        [Tooltip("Emit by distance while the harpoon flies: the bubble streak behind it.")]
        public ParticleSystem[] trail;
        [Tooltip("Call Fire() when the object gets enabled (for a harpoon spawned at the moment of the shot).")]
        public bool fireOnEnable;
        [Tooltip("Only bubble under water. With no water in the scene the effect always plays.")]
        public bool onlyUnderwater = true;

        // Frames the trail stays off after Fire(): a pooled harpoon jumping back to the gun must not leave a streak.
        const int ArmFrames = 2;

        bool flying;
        int armDelay;

        public bool IsFlying => flying;

        void OnEnable()
        {
            if (fireOnEnable) Fire();
        }

        void OnDisable()
        {
            flying = false;
            SetTrail(false);
        }

        /// <summary>The shot: bubble burst at the current position, then the streak follows the harpoon.</summary>
        public void Fire()
        {
            flying = true;
            armDelay = ArmFrames;
            SetTrail(false);
            foreach (ParticleSystem ps in trail)
                if (ps != null && !ps.isPlaying) ps.Play(false);

            if (!InWater()) return;
            if (shotRing != null)
                shotRing.localRotation = Quaternion.Euler(Random.Range(-ringTilt, ringTilt), Random.Range(-ringTilt, ringTilt), Random.Range(0f, 360f));
            foreach (ParticleSystem ps in fireBurst)
            {
                if (ps == null) continue;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);   // restart the bursts, keep the old bubbles
                ps.Play(false);
            }
        }

        /// <summary>Stop leaving bubbles (hit, stuck, picked up). The ones already in the water live out their life.</summary>
        public void Stop()
        {
            flying = false;
            SetTrail(false);
        }

        /// <summary>Stop and leave the effect in the world until its bubbles are gone (when the harpoon itself gets destroyed).</summary>
        public void StopAndDetach()
        {
            Stop();
            transform.SetParent(null, true);
            Destroy(gameObject, MaxLifetime());
        }

        void Update()
        {
            // bubbles already in the water drift with the current
            if (fireBurst != null) foreach (ParticleSystem ps in fireBurst) WaterCurrent.Drift(ps, transform.position);
            if (trail != null) foreach (ParticleSystem ps in trail) WaterCurrent.Drift(ps, transform.position);

            if (!flying) return;
            if (armDelay > 0)
            {
                armDelay--;
                return;
            }
            SetTrail(InWater());
        }

        bool InWater()
        {
            if (!onlyUnderwater || WaterSurface.Instances.Count == 0) return true;
            return WaterSurface.IsPointUnderwater(transform.position);
        }

        void SetTrail(bool on)
        {
            foreach (ParticleSystem ps in trail)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.enabled = on;
            }
        }

        float MaxLifetime()
        {
            float max = 0f;
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
                max = Mathf.Max(max, ps.main.startLifetime.constantMax);
            return max + 0.5f;
        }
    }
}

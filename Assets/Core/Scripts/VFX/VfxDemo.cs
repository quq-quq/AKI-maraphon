using AKI.Weapons;
using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// Test bench for the harpoon bubbles and the tuna blood until the real fish and the player's shooting exist:
    /// a stand-in tuna swims in a circle in front of this object and the speargun shoots at it every few seconds.
    /// Created by AKI > VFX > Add Harpoon Demo To Scene.
    /// </summary>
    public class VfxDemo : MonoBehaviour
    {
        public Speargun gun;
        public Transform tuna;

        [Min(0.5f)] public float shotInterval = 3f;
        [Tooltip("Distance in front of the gun to the centre of the tuna's circle.")]
        [Min(1f)] public float tunaDistance = 6f;
        [Min(0f)] public float tunaRadius = 2f;
        [Tooltip("Radians per second.")]
        public float tunaSpeed = 0.5f;

        float angle;
        float nextShot;

        void Start()
        {
            nextShot = Time.time + 1f;
        }

        void Update()
        {
            angle += tunaSpeed * Time.deltaTime;
            tuna.SetPositionAndRotation(TunaPosition(angle), Quaternion.LookRotation(TunaHeading(angle)));

            if (Time.time < nextShot || !gun.IsLoaded) return;
            Aim();
            gun.Shoot();
            nextShot = Time.time + shotInterval;
        }

        // lead the target: where will the tuna be when the harpoon gets there
        void Aim()
        {
            float speed = gun.projectilePrefab != null ? gun.projectilePrefab.speed * 0.9f : 20f;   // ~ average speed with drag
            Vector3 from = gun.muzzle.position;
            Vector3 aim = tuna.position;
            for (int k = 0; k < 3; k++)
                aim = TunaPosition(angle + tunaSpeed * Vector3.Distance(from, aim) / speed);

            // turn the whole gun so that its muzzle points at the target
            Quaternion delta = Quaternion.LookRotation(aim - from) * Quaternion.Inverse(gun.muzzle.rotation);
            gun.transform.rotation = delta * gun.transform.rotation;
        }

        Vector3 TunaPosition(float a)
        {
            Vector3 centre = transform.position + transform.forward * tunaDistance;
            Vector3 offset = (transform.right * Mathf.Cos(a) + transform.forward * Mathf.Sin(a)) * tunaRadius;
            return centre + offset + Vector3.up * (Mathf.Sin(a * 2f) * 0.3f);
        }

        Vector3 TunaHeading(float a)
        {
            Vector3 d = (-transform.right * Mathf.Sin(a) + transform.forward * Mathf.Cos(a)) * Mathf.Sign(tunaSpeed);
            return d.sqrMagnitude > 1e-6f ? d : transform.forward;
        }
    }
}

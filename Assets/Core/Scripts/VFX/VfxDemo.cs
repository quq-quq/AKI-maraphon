using AKI.Weapons;
using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// Test bench for the harpoon bubbles and the tuna blood until the real fish and the player's shooting exist:
    /// a stand-in tuna swims in a circle in front of this object and the speargun shoots at it every few seconds.
    /// A caught tuna melts away (<see cref="Catchable"/>) and a new one comes in.
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
        GameObject tunaTemplate;
        float respawnAt = -1f;

        void Start()
        {
            nextShot = Time.time + 1f;
            tunaTemplate = Instantiate(tuna.gameObject, tuna.parent);   // untouched copy for the next tuna
            tunaTemplate.name = tuna.name;
            tunaTemplate.SetActive(false);
        }

        void Update()
        {
            angle += tunaSpeed * Time.deltaTime;
            if (tuna == null && !Respawn()) return;
            tuna.SetPositionAndRotation(TunaPosition(angle), Quaternion.LookRotation(TunaHeading(angle)));

            if (Time.time < nextShot || !gun.IsLoaded) return;
            if (tuna.TryGetComponent(out Catchable prey) && prey.IsCaught) return;
            Aim();
            gun.Shoot();
            nextShot = Time.time + shotInterval;
        }

        // the caught one is gone: a new tuna a moment later
        bool Respawn()
        {
            if (respawnAt < 0f) respawnAt = Time.time + 1.5f;
            if (Time.time < respawnAt) return false;
            respawnAt = -1f;
            GameObject next = Instantiate(tunaTemplate, tunaTemplate.transform.parent);
            next.name = tunaTemplate.name;
            next.SetActive(true);
            tuna = next.transform;
            nextShot = Time.time + 1f;
            return true;
        }

        // lead the target: where will the tuna be when the harpoon gets there
        void Aim()
        {
            float speed = gun.projectilePrefab != null ? gun.projectilePrefab.speed * 0.9f : 20f;   // ~ average speed with drag
            Vector3 from = gun.muzzle.position;
            Vector3 aim = tuna.position;
            float t = 0f;
            for (int k = 0; k < 3; k++)
            {
                t = Vector3.Distance(from, aim) / speed;
                aim = TunaPosition(angle + tunaSpeed * t);
            }
            // aim above it by the drop of the arc (the bench is under water)
            float gravity = gun.projectilePrefab != null ? gun.projectilePrefab.waterGravityScale : 0.5f;
            aim += Vector3.up * (0.5f * Physics.gravity.magnitude * gravity * t * t);

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

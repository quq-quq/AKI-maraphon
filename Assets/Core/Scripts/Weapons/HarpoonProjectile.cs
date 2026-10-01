using AKI.VFX;
using AKI.Water;
using UnityEngine;

namespace AKI.Weapons
{
    /// <summary>
    /// A harpoon arrow in flight. Its origin is the tip and it flies along its forward axis on a ballistic arc: under
    /// water it loses speed and drops, in the air it keeps its speed and falls faster. On hitting a collider it sticks in;
    /// a fish with <see cref="TunaBlood"/> starts bleeding, and a <see cref="Catchable"/> one is caught (it melts away
    /// together with the arrow and its line). The bubble streak (<see cref="HarpoonBubbles"/> at the tip) starts with
    /// the shot and stops on the hit.
    /// </summary>
    public class HarpoonProjectile : MonoBehaviour
    {
        public HarpoonBubbles bubbles;
        [Min(1f)] public float speed = 25f;

        [Header("Under water")]
        [Tooltip("Speed lost per second (fraction of the current speed).")]
        [Min(0f)] public float waterDrag = 0.3f;
        [Tooltip("Share of normal gravity under water (buoyancy takes the rest).")]
        [Range(0f, 1f)] public float waterGravityScale = 0.5f;

        [Header("In the air")]
        [Min(0f)] public float airDrag = 0.02f;
        [Range(0f, 1f)] public float airGravityScale = 1f;

        [Header("Hitting")]
        [Min(0f)] public float hitRadius = 0.02f;
        [Tooltip("How deep the tip goes into what it hits (m).")]
        [Min(0f)] public float penetration = 0.06f;
        public LayerMask hitMask = Physics.DefaultRaycastLayers;
        [Tooltip("Seconds before a harpoon that hit nothing is removed.")]
        [Min(0.5f)] public float flightLifetime = 6f;
        [Tooltip("Seconds a harpoon stays stuck in its target.")]
        [Min(0.5f)] public float stuckLifetime = 10f;

        Vector3 velocity;
        float age;
        bool flying;
        Speargun gun;
        Transform ignoreRoot;
        HarpoonRope rope;
        Vector3 tailLocal;
        bool caught;

        public bool IsFlying => flying;

        public Vector3 Velocity => velocity;

        /// <summary>This arrow's line (null without one, or once it is gone).</summary>
        public HarpoonRope Rope => rope;

        /// <summary>Starts the flight from the current pose. Colliders under the gun's owner are ignored.</summary>
        public void Launch(Speargun from)
        {
            gun = from;
            ignoreRoot = from != null ? from.Owner : null;
            velocity = transform.forward * speed;
            flying = true;
            age = 0f;
            if (bubbles != null) bubbles.Fire();

            tailLocal = TailLocal();
            if (from != null && from.RopeAnchor != null) rope = HarpoonRope.Create(from, this, tailLocal);
        }

        /// <summary>Leaves the bubbles already in the water behind (the arrow is about to disappear).</summary>
        public void DetachEffects()
        {
            if (bubbles != null) bubbles.StopAndDetach();
            bubbles = null;
        }

        /// <summary>The gun fired again: cut this arrow's line at the gun.</summary>
        public void CutRope()
        {
            if (rope != null) rope.Cut();
        }

        // The line is tied near the back of the shaft: the rearmost point of the arrow mesh.
        Vector3 TailLocal()
        {
            float back = 0f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                          : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    back = Mathf.Min(back, transform.InverseTransformPoint(r.transform.TransformPoint(corner)).z);
                }
            }
            return new Vector3(0f, 0f, back * 0.9f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (!flying)
            {
                if (age >= stuckLifetime && !caught) Remove();   // a caught fish takes the arrow with it
                return;
            }
            if (age >= flightLifetime)
            {
                Remove();
                return;
            }

            float dt = Time.deltaTime;
            bool inWater = WaterSurface.IsPointUnderwater(transform.position);
            velocity *= Mathf.Max(0f, 1f - (inWater ? waterDrag : airDrag) * dt);
            velocity += Physics.gravity * ((inWater ? waterGravityScale : airGravityScale) * dt);

            Vector3 from = transform.position;
            Vector3 step = velocity * dt;
            if (rope != null && step.sqrMagnitude > 1e-10f)
            {
                // at the end of its line the arrow is held back
                Vector3 tailWorld = from + step + Quaternion.LookRotation(step) * tailLocal;
                step += rope.Tether(tailWorld, ref velocity);
            }
            float length = step.magnitude;
            if (length < 1e-5f) return;

            if (CastAhead(from, step / length, length, out RaycastHit hit))
            {
                StickInto(hit, step / length);
                return;
            }

            transform.SetPositionAndRotation(from + step, Quaternion.LookRotation(step));
        }

        bool CastAhead(Vector3 from, Vector3 dir, float length, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            RaycastHit[] hits = Physics.SphereCastAll(from, hitRadius, dir, length, hitMask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.distance >= best) continue;
                if (ignoreRoot != null && h.transform.IsChildOf(ignoreRoot)) continue;
                best = h.distance;
                nearest = h;
            }
            return best < float.MaxValue;
        }

        void StickInto(RaycastHit hit, Vector3 dir)
        {
            flying = false;
            age = 0f;
            Vector3 point = hit.distance > 0f ? hit.point : transform.position;
            transform.SetPositionAndRotation(point + dir * penetration, Quaternion.LookRotation(dir));   // the tip goes in
            Transform carrier = hit.rigidbody != null ? hit.rigidbody.transform : hit.transform;
            if (IsUniform(carrier.lossyScale)) transform.SetParent(carrier, true);   // moves with the fish (a stretched parent would skew the arrow)
            if (bubbles != null) bubbles.Stop();

            TunaBlood blood = hit.collider.GetComponentInParent<TunaBlood>();
            if (blood == null) blood = hit.collider.GetComponentInChildren<TunaBlood>();
            if (blood != null) blood.Hit(point, dir);

            Catchable prey = hit.collider.GetComponentInParent<Catchable>();
            if (prey != null && !prey.IsCaught)
            {
                caught = true;
                prey.Catch(this);
            }

            if (gun != null) gun.onHit.Invoke(hit.collider);
        }

        static bool IsUniform(Vector3 s)
        {
            return Mathf.Abs(s.x - s.y) < 0.01f * Mathf.Abs(s.x) && Mathf.Abs(s.x - s.z) < 0.01f * Mathf.Abs(s.x);
        }

        void Remove()
        {
            if (bubbles != null) bubbles.StopAndDetach();   // the bubbles finish rising on their own
            Destroy(gameObject);
        }
    }
}

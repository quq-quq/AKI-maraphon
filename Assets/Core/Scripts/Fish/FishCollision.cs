using AKI.Weapons;
using UnityEngine;
using UnityEngine.Events;

namespace AKI.Fish
{
    /// <summary>
    /// The fish taking a harpoon: the arrow goes in up to the middle of the body, the fish stops swimming
    /// (<see cref="FishAI"/>), gets knocked back a little along the shot with a bit of spin, then drifts and slowly sinks
    /// while <see cref="Catchable"/> melts it away with the same noise dissolve as the arrows.
    /// Blood is not made here: <see cref="onHit"/> (and <see cref="WoundPosition"/>) is the hook for it.
    /// </summary>
    [RequireComponent(typeof(FishAI), typeof(Catchable))]
    public class FishCollision : MonoBehaviour, IHarpoonTarget
    {
        [Header("Harpoon")]
        [Tooltip("The body the arrow goes into. Empty = the first collider on the fish.")]
        public Collider body;
        [Tooltip("How far past the middle of the body the tip stops (m, negative = short of it).")]
        public float depthOffset = 0f;

        [Header("Knockback")]
        [Tooltip("Push along the shot for an arrow at full speed (m/s). A slowed-down arrow pushes less.")]
        [Min(0f)] public float knockbackSpeed = 3.5f;
        [Tooltip("Spin from an off-centre hit (degrees per second).")]
        [Min(0f)] public float knockbackSpin = 180f;
        [Tooltip("How quickly the water eats the push and the spin (1/s).")]
        [Min(0f)] public float waterDrag = 2.2f;
        [Tooltip("Sinking speed once hit (m/s).")]
        [Min(0f)] public float sinkSpeed = 0.15f;

        [Header("Events")]
        [Tooltip("Hit by a harpoon: wound position (world) and the direction the harpoon travelled. Hook the blood effect here.")]
        public UnityEvent<Vector3, Vector3> onHit = new UnityEvent<Vector3, Vector3>();

        FishAI ai;
        Vector3 velocity;
        Vector3 spinAxis = Vector3.up;
        float spin;
        Vector3 woundLocal;
        Vector3 hitDirection;

        public bool IsHit { get; private set; }

        /// <summary>Where the harpoon went in (world, moves with the fish). Only valid once <see cref="IsHit"/>.</summary>
        public Vector3 WoundPosition => transform.TransformPoint(woundLocal);

        void Awake()
        {
            ai = GetComponent<FishAI>();
            if (body == null) body = GetComponentInChildren<Collider>();
        }

        // ------------------------------------------------------------------ IHarpoonTarget

        public float GetPenetration(Vector3 point, Vector3 direction, float defaultPenetration)
        {
            // the tip stops where the shot line passes closest to the middle of the body
            return Mathf.Max(defaultPenetration, Vector3.Dot(Middle() - point, direction) + depthOffset);
        }

        public void OnHarpoonHit(HarpoonProjectile arrow, Vector3 point, Vector3 direction)
        {
            IsHit = true;
            ai.Stop();
            woundLocal = transform.InverseTransformPoint(point);
            hitDirection = direction;

            // a slow arrow (long shot, eaten by the water) pushes less
            float power = arrow != null && arrow.speed > 0f ? Mathf.Clamp(arrow.Velocity.magnitude / arrow.speed, 0.3f, 1f) : 1f;
            velocity += direction * (knockbackSpeed * power);

            // off-centre hits turn the fish around the middle
            Vector3 axis = Vector3.Cross(point - Middle(), direction);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(direction, Vector3.up);
            if (axis.sqrMagnitude > 1e-6f) spinAxis = axis.normalized;
            spin += knockbackSpin * power;

            onHit.Invoke(point, direction);
        }

        // ------------------------------------------------------------------ drifting after the hit

        void Update()
        {
            if (!IsHit) return;
            float dt = Time.deltaTime;
            float damping = Mathf.Exp(-waterDrag * dt);
            velocity *= damping;
            spin *= damping;

            transform.position += (velocity + Vector3.down * sinkSpeed) * dt;
            transform.rotation = Quaternion.AngleAxis(spin * dt, spinAxis) * transform.rotation;
        }

        Vector3 Middle()
        {
            if (body == null) return transform.position;
            Vector3 local = body switch
            {
                CapsuleCollider capsule => capsule.center,
                SphereCollider sphere => sphere.center,
                BoxCollider box => box.center,
                _ => body.transform.InverseTransformPoint(body.bounds.center),
            };
            return body.transform.TransformPoint(local);
        }

        void OnDrawGizmos()
        {
            if (!IsHit) return;
            Vector3 wound = WoundPosition;
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 1f);
            Gizmos.DrawWireSphere(wound, 0.05f);
            Gizmos.DrawLine(wound - hitDirection * 0.6f, wound);   // where the shot came from
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
            Gizmos.DrawLine(transform.position, transform.position + velocity);   // the push
        }
    }
}

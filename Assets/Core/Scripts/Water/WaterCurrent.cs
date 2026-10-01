using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// The sea's current: a slow horizontal flow that drifts harpoons, their line, bubbles and blood, and carries the
    /// specks and streaks of <see cref="WaterCurrentView"/> that show which way it goes. Strongest at the surface,
    /// weaker in the deep; its direction and strength wander a little across the sea and over time.
    /// One per scene, next to the <see cref="WaterSurface"/>. Ask it with <see cref="At"/>.
    /// </summary>
    public class WaterCurrent : MonoBehaviour
    {
        [Tooltip("Where the water flows to (degrees around Y: 0 = +Z, 90 = +X).")]
        [Range(0f, 360f)] public float direction = 60f;
        [Tooltip("Speed at the surface (m/s).")]
        [Min(0f)] public float speed = 1f;
        [Tooltip("Depth (m) over which the current eases off towards Deep Share.")]
        [Min(0.1f)] public float depthFalloff = 12f;
        [Tooltip("Share of the surface speed left far below.")]
        [Range(0f, 1f)] public float deepShare = 0.35f;

        [Header("Wandering")]
        [Tooltip("How far the direction swings either way (degrees).")]
        [Range(0f, 90f)] public float directionVariation = 25f;
        [Tooltip("How much the speed rises and falls (share).")]
        [Range(0f, 1f)] public float speedVariation = 0.35f;
        [Tooltip("Size of the patches the current changes over (m).")]
        [Min(1f)] public float variationScale = 30f;
        [Tooltip("How quickly the pattern drifts (1/s).")]
        [Min(0f)] public float variationSpeed = 0.04f;

        static WaterCurrent active;

        /// <summary>The scene's current, or null.</summary>
        public static WaterCurrent Active => active;

        /// <summary>Velocity of the water at <paramref name="worldPoint"/> (m/s); zero out of the water or with no current.</summary>
        public static Vector3 At(Vector3 worldPoint) => active != null ? active.Sample(worldPoint) : Vector3.zero;

        void OnEnable()
        {
            active = this;
        }

        void OnDisable()
        {
            if (active == this) active = null;
        }

        public Vector3 Sample(Vector3 p)
        {
            if (!TryDepth(p, out float depth)) return Vector3.zero;

            float t = Time.time * variationSpeed;
            float u = p.x / variationScale, v = p.z / variationScale;
            float swing = Mathf.PerlinNoise(u + t, v - 0.7f * t) * 2f - 1f;
            float surge = Mathf.PerlinNoise(v + 37.1f - 0.5f * t, u + 11.3f + 0.3f * t) * 2f - 1f;

            float angle = (direction + swing * directionVariation) * Mathf.Deg2Rad;
            float strength = speed * Mathf.Max(0f, 1f + surge * speedVariation);
            strength *= Mathf.Lerp(deepShare, 1f, Mathf.Exp(-depth / depthFalloff));
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * strength;
        }

        /// <summary>The main flow direction (no wandering), for gizmos and UI.</summary>
        public Vector3 MainDirection => Quaternion.Euler(0f, direction, 0f) * Vector3.forward;

        // depth below the (mean) surface of the water the point is in
        static bool TryDepth(Vector3 p, out float depth)
        {
            var waters = WaterSurface.Instances;
            for (int i = 0; i < waters.Count; i++)
            {
                if (!waters[i].ContainsPoint(p)) continue;
                depth = waters[i].WaterLevel - p.y;
                return true;
            }
            depth = 0f;
            return false;
        }

        /// <summary>Lets a particle system drift with the water at <paramref name="where"/> (all its particles alike).</summary>
        public static void Drift(ParticleSystem ps, Vector3 where, float amount = 1f)
        {
            if (ps == null) return;
            Vector3 flow = At(where) * amount;
            var velocity = ps.velocityOverLifetime;
            if (flow.sqrMagnitude < 1e-6f && !velocity.enabled) return;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = flow.x;
            velocity.y = flow.y;
            velocity.z = flow.z;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f);
            Vector3 o = transform.position + Vector3.down;
            Vector3 d = MainDirection * (2f + speed * 3f);
            Gizmos.DrawLine(o, o + d);
            Gizmos.DrawLine(o + d, o + d - Quaternion.Euler(0f, 25f, 0f) * d * 0.25f);
            Gizmos.DrawLine(o + d, o + d - Quaternion.Euler(0f, -25f, 0f) * d * 0.25f);
        }
    }
}

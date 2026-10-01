using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// How the water moves under the surface. Two parts:
    /// <list type="bullet">
    /// <item>the swell's push and pull: under passing waves the water swings to and fro (and a little up and down) with
    /// the wave period, along the waves' direction, fading with depth - taken from the FFT ocean so it matches the
    /// waves you see (their period, direction and size);</item>
    /// <item>a weak steady drift that wanders across the sea and over time.</item>
    /// </list>
    /// The whole thing swells and eases off slowly, so it is never constant. It carries the swimmer, harpoons, their
    /// line, bubbles and blood, and the specks of <see cref="WaterCurrentView"/>. One per scene, next to the
    /// <see cref="WaterSurface"/>. Ask it with <see cref="At"/>.
    /// </summary>
    public class WaterCurrent : MonoBehaviour
    {
        const float G = 9.81f;

        [Header("Waves (push and pull)")]
        [Tooltip("Peak speed of the to-and-fro swing right under the surface (m/s), times the ocean's wave scale.")]
        [Min(0f)] public float surge = 0.6f;
        [Tooltip("Up and down part of the swing, as a share of the horizontal one.")]
        [Range(0f, 1f)] public float surgeVertical = 0.4f;
        [Tooltip("Without an FFT ocean: direction the waves run to (degrees around Y: 0 = +X, 90 = +Z) and their period (s).")]
        [Range(0f, 360f)] public float fallbackWaveDirection = 30f;
        [Min(1f)] public float fallbackWavePeriod = 8f;

        [Header("Steady drift")]
        [Tooltip("Where the drift flows to (degrees around Y: 0 = +Z, 90 = +X).")]
        [Range(0f, 360f)] public float direction = 60f;
        [Tooltip("Drift speed at the surface (m/s).")]
        [Min(0f)] public float speed = 0.25f;
        [Tooltip("How far the drift direction swings either way (degrees).")]
        [Range(0f, 90f)] public float directionVariation = 25f;
        [Tooltip("Size of the patches the drift changes over (m).")]
        [Min(1f)] public float variationScale = 30f;
        [Tooltip("How quickly the drift pattern changes (1/s).")]
        [Min(0f)] public float variationSpeed = 0.04f;

        [Header("Depth and gusts")]
        [Tooltip("Depth (m) over which the drift eases off towards Deep Share (the swell fades on its own, by wavelength).")]
        [Min(0.1f)] public float depthFalloff = 12f;
        [Range(0f, 1f)] public float deepShare = 0.35f;
        [Tooltip("How much the whole current swells and eases off over time (share).")]
        [Range(0f, 1f)] public float gusts = 0.45f;
        [Tooltip("Seconds of a typical gust.")]
        [Min(1f)] public float gustPeriod = 20f;

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
            float t = Time.time;
            float gust = 1f + gusts * (Mathf.PerlinNoise(t / gustPeriod, 7.3f) * 2f - 1f);
            return (Drift(p, depth, t) + Swell(p, depth, t)) * Mathf.Max(0f, gust);
        }

        // steady flow, wandering a little in direction and strength across the sea
        Vector3 Drift(Vector3 p, float depth, float t)
        {
            if (speed <= 0f) return Vector3.zero;
            float tv = t * variationSpeed;
            float u = p.x / variationScale, v = p.z / variationScale;
            float swing = Mathf.PerlinNoise(u + tv, v - 0.7f * tv) * 2f - 1f;
            float surgeNoise = Mathf.PerlinNoise(v + 37.1f - 0.5f * tv, u + 11.3f + 0.3f * tv);
            float angle = (direction + swing * directionVariation) * Mathf.Deg2Rad;
            float strength = speed * (0.6f + 0.8f * surgeNoise) * Mathf.Lerp(deepShare, 1f, Mathf.Exp(-depth / depthFalloff));
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * strength;
        }

        // water under the dominant waves swings along their direction with their period (deep-water orbits:
        // horizontal under crests and troughs, vertical in between), fading as e^(-k depth). A shorter, slightly
        // turned second wave keeps the rhythm from being perfectly regular.
        Vector3 Swell(Vector3 p, float depth, float t)
        {
            if (surge <= 0f) return Vector3.zero;
            OceanFFT ocean = OceanFFT.Active;
            float omega, scale;
            Vector2 dir;
            if (ocean != null)
            {
                omega = ocean.PeakOmega;
                dir = ocean.WaveDirection;
                scale = ocean.waveScale;
            }
            else
            {
                omega = 2f * Mathf.PI / fallbackWavePeriod;
                float a = fallbackWaveDirection * Mathf.Deg2Rad;
                dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                scale = 1f;
            }

            Vector3 v = Orbit(p, depth, t, dir, omega, 1f, 0f);
            Vector2 dir2 = Rotate(dir, 25f * Mathf.Deg2Rad);
            v += Orbit(p, depth, t, dir2, omega * 1.6f, 0.45f, 1.7f);
            return v * (surge * scale);
        }

        Vector3 Orbit(Vector3 p, float depth, float t, Vector2 dir, float omega, float amplitude, float phase0)
        {
            float k = omega * omega / G;   // deep water dispersion
            float phase = k * (p.x * dir.x + p.z * dir.y) - omega * t + phase0;
            float decay = Mathf.Exp(-k * depth) * amplitude;
            float c = Mathf.Cos(phase), s = Mathf.Sin(phase);
            return new Vector3(dir.x * c, s * surgeVertical, dir.y * c) * decay;
        }

        static Vector2 Rotate(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The steady drift's main direction (no wandering), for gizmos and UI.</summary>
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
            Vector3 d = MainDirection * (2f + speed * 6f);
            Gizmos.DrawLine(o, o + d);
            Gizmos.DrawLine(o + d, o + d - Quaternion.Euler(0f, 25f, 0f) * d * 0.25f);
            Gizmos.DrawLine(o + d, o + d - Quaternion.Euler(0f, -25f, 0f) * d * 0.25f);
        }
    }
}

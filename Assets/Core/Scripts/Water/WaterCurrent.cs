using System.Collections.Generic;
using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// How the water moves under the surface. Two parts:
    /// <list type="bullet">
    /// <item>a steady drift along the <see cref="Wind"/>, turning with it (so things go round in wide loops), wandering
    /// a little across the sea and easing off with depth;</item>
    /// <item>the swell's push and pull: under passing waves the water swings to and fro (and a little up and down) with
    /// the wave period, along the waves' direction, fading with depth - taken from the FFT ocean so it matches the
    /// waves you see.</item>
    /// </list>
    /// The whole thing swells and eases off slowly. It carries every non-kinematic Rigidbody under water, the swimmer,
    /// harpoons, their line, bubbles and blood, and the specks of <see cref="WaterCurrentView"/>. One per scene, next
    /// to the <see cref="WaterSurface"/>. Ask it with <see cref="At"/>.
    /// </summary>
    public class WaterCurrent : MonoBehaviour
    {
        const float G = 9.81f;

        [Header("Waves (push and pull)")]
        [Tooltip("Peak speed of the to-and-fro swing right under the surface (m/s), times the ocean's wave scale.")]
        [Min(0f)] public float surge = 0.15f;
        [Tooltip("Up and down part of the swing, as a share of the horizontal one.")]
        [Range(0f, 1f)] public float surgeVertical = 0.4f;
        [Tooltip("Without an FFT ocean: direction the waves run to (degrees around Y: 0 = +X, 90 = +Z) and their period (s).")]
        [Range(0f, 360f)] public float fallbackWaveDirection = 30f;
        [Min(1f)] public float fallbackWavePeriod = 8f;

        [Header("Steady drift")]
        [Tooltip("Where the drift flows to without a Wind in the scene (degrees around Y: 0 = +Z, 90 = +X).")]
        [Range(0f, 360f)] public float direction = 60f;
        [Tooltip("Drift speed right under the surface (m/s).")]
        [Min(0f)] public float speed = 0.9f;
        [Tooltip("How far the drift direction swings either way across the sea (degrees).")]
        [Range(0f, 90f)] public float directionVariation = 10f;
        [Tooltip("Size of the patches the drift changes over (m).")]
        [Min(1f)] public float variationScale = 30f;
        [Tooltip("How quickly the drift pattern changes (1/s).")]
        [Min(0f)] public float variationSpeed = 0.04f;

        [Header("Depth and gusts")]
        [Tooltip("Depth (m) over which the drift eases off towards Deep Share (the swell fades on its own, by wavelength).")]
        [Min(0.1f)] public float depthFalloff = 25f;
        [Range(0f, 1f)] public float deepShare = 0.85f;
        [Tooltip("How much the whole current swells and eases off over time (share).")]
        [Range(0f, 1f)] public float gusts = 0.2f;
        [Tooltip("Seconds of a typical gust.")]
        [Min(1f)] public float gustPeriod = 20f;

        [Header("Rigidbodies")]
        [Tooltip("How quickly the water drags rigidbodies along with it (1/s). 0 = leave them alone.")]
        [Min(0f)] public float bodyDrag = 1.2f;

        static WaterCurrent active;
        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        float nextBodyScan;

        // What a sample needs that doesn't depend on the point (gusts and the ocean's waves), worked out once instead of
        // for every speck and body asking: again when the time step or the waves change.
        float cachedTime = float.NaN;
        float gustNow, tvNow;
        OceanFFT cachedOcean;
        Vector4 cachedSea = new Vector4(float.NaN, 0f, 0f, 0f);
        bool swellOn;
        Vector2 swellDir1, swellDir2;
        float swellOmega1, swellOmega2, swellK1, swellK2, swellScale;

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
            return TryDepth(p, out float depth) ? SampleAtDepth(p, depth) : Vector3.zero;
        }

        /// <summary>
        /// Velocity of the water at <paramref name="worldPoint"/>, and whether the point is under water at all (the
        /// same test <see cref="WaterSurface.IsPointUnderwater"/> makes), in one go.
        /// </summary>
        public static bool TrySample(Vector3 worldPoint, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (!TryDepth(worldPoint, out float depth)) return false;
            if (active != null) velocity = active.SampleAtDepth(worldPoint, depth);
            return true;
        }

        Vector3 SampleAtDepth(Vector3 p, float depth)
        {
            Step();
            return AtDepth(p, depth, Heading);
        }

        float Heading => Wind.Active != null ? Wind.Active.heading : direction;

        // the parts of a sample that don't depend on the point, for the time it is now
        void Step()
        {
            float t = Time.time;
            if (t != cachedTime)
            {
                cachedTime = t;
                gustNow = Mathf.Max(0f, 1f + gusts * (Mathf.PerlinNoise(t / gustPeriod, 7.3f) * 2f - 1f));
                tvNow = t * variationSpeed;
            }
            OceanFFT ocean = OceanFFT.Active;
            Vector4 sea = ocean != null ? new Vector4(ocean.windSpeed, ocean.fetchKm, ocean.windDirection, ocean.waveScale)
                                        : new Vector4(-1f, fallbackWavePeriod, fallbackWaveDirection, surge);
            // exact comparison (Vector4's == forgives small differences)
            if (!ReferenceEquals(ocean, cachedOcean) || !sea.Equals(cachedSea) || swellOn != surge > 0f) CacheSwell(ocean, sea);
        }

        Vector3 AtDepth(Vector3 p, float depth, float heading)
        {
            return (Drift(p, depth, heading) + Swell(p, depth, cachedTime)) * gustNow;
        }

        // ---- many samples at one moment (the specks of WaterCurrentView): the waters, the time, the wind and the
        // waves are read once for the lot instead of for every sample. Nothing moves in between, so every answer is the
        // one TrySample would give.
        struct Area { public float x, z, half, level; }
        static Area[] areas = new Area[2];
        static int areaCount;
        static WaterCurrent prepared;
        static float preparedHeading;

        /// <summary>Reads the waters and the current once, for a batch of <see cref="TrySamplePrepared"/> calls made right after.</summary>
        public static void PrepareSamples()
        {
            IReadOnlyList<WaterSurface> waters = WaterSurface.Instances;
            if (areas.Length < waters.Count) areas = new Area[waters.Count];
            areaCount = waters.Count;
            for (int i = 0; i < areaCount; i++)
            {
                Vector3 c = waters[i].transform.position;
                areas[i] = new Area { x = c.x, z = c.z, half = waters[i].size * 0.5f, level = c.y };
            }
            prepared = active != null ? active : null;
            if (prepared is null) return;
            prepared.Step();
            preparedHeading = prepared.Heading;
        }

        /// <summary><see cref="TrySample"/> for a batch read by <see cref="PrepareSamples"/>.</summary>
        public static bool TrySamplePrepared(Vector3 worldPoint, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            for (int i = 0; i < areaCount; i++)
            {
                Area a = areas[i];
                // WaterSurface.ContainsPoint: inside the square and below the mean level
                if (Mathf.Abs(worldPoint.x - a.x) > a.half || Mathf.Abs(worldPoint.z - a.z) > a.half || worldPoint.y >= a.level) continue;
                if (!(prepared is null)) velocity = prepared.AtDepth(worldPoint, a.level - worldPoint.y, preparedHeading);
                return true;
            }
            return false;
        }

        void CacheSwell(OceanFFT ocean, Vector4 sea)
        {
            cachedOcean = ocean;
            cachedSea = sea;
            swellOn = surge > 0f;
            if (!swellOn) return;
            float omega;
            Vector2 dir;
            if (ocean != null)
            {
                omega = ocean.PeakOmega;
                dir = ocean.WaveDirection;
                swellScale = ocean.waveScale;
            }
            else
            {
                omega = 2f * Mathf.PI / fallbackWavePeriod;
                float a = fallbackWaveDirection * Mathf.Deg2Rad;
                dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                swellScale = 1f;
            }
            swellDir1 = dir;
            swellOmega1 = omega;
            swellK1 = omega * omega / G;   // deep water dispersion
            swellDir2 = Rotate(dir, 25f * Mathf.Deg2Rad);
            swellOmega2 = omega * 1.6f;
            swellK2 = swellOmega2 * swellOmega2 / G;
        }

        // steady flow, wandering a little in direction and strength across the sea
        Vector3 Drift(Vector3 p, float depth, float heading)
        {
            if (speed <= 0f) return Vector3.zero;
            float tv = tvNow;
            float u = p.x / variationScale, v = p.z / variationScale;
            float swing = Mathf.PerlinNoise(u + tv, v - 0.7f * tv) * 2f - 1f;
            float surgeNoise = Mathf.PerlinNoise(v + 37.1f - 0.5f * tv, u + 11.3f + 0.3f * tv);
            float angle = (heading + swing * directionVariation) * Mathf.Deg2Rad;
            float strength = speed * (0.6f + 0.8f * surgeNoise) * Mathf.Lerp(deepShare, 1f, Mathf.Exp(-depth / depthFalloff));
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * strength;
        }

        // water under the dominant waves swings along their direction with their period (deep-water orbits:
        // horizontal under crests and troughs, vertical in between), fading as e^(-k depth). A shorter, slightly
        // turned second wave keeps the rhythm from being perfectly regular.
        Vector3 Swell(Vector3 p, float depth, float t)
        {
            if (!swellOn) return Vector3.zero;
            Vector3 v = Orbit(p, depth, t, swellDir1, swellOmega1, swellK1, 1f, 0f);
            v += Orbit(p, depth, t, swellDir2, swellOmega2, swellK2, 0.45f, 1.7f);
            return v * (surge * swellScale);
        }

        Vector3 Orbit(Vector3 p, float depth, float t, Vector2 dir, float omega, float k, float amplitude, float phase0)
        {
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
        public Vector3 MainDirection => Quaternion.Euler(0f, Wind.Active != null ? Wind.Active.heading : direction, 0f) * Vector3.forward;

        // Every loose rigidbody under water is dragged towards the water's own velocity.
        void FixedUpdate()
        {
            if (bodyDrag <= 0f) return;
            if (Time.time >= nextBodyScan)
            {
                bodies.Clear();
                bodies.AddRange(FindObjectsByType<Rigidbody>(FindObjectsSortMode.None));
                nextBodyScan = Time.time + 1f;
            }
            foreach (Rigidbody body in bodies)
            {
                if (body == null || body.isKinematic) continue;
                Vector3 flow = Sample(body.worldCenterOfMass);
                if (flow == Vector3.zero) continue;   // out of the water
                body.AddForce((flow - body.linearVelocity) * bodyDrag, ForceMode.Acceleration);
            }
        }

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

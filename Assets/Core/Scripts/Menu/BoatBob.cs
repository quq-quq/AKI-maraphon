using AKI.Water;
using UnityEngine;

namespace AKI.Menu
{
    /// <summary>
    /// A boat floating on the real waves like a body with weight. The water is asked for its wavy surface
    /// (<see cref="WaterProbe"/>, the same GPU wave height the player floats on) on a 3 x 3 grid over the hull, and a
    /// plane is fitted through those points (least squares), so the hull bridges the ripples. Buoyancy then pulls the
    /// boat's height and its lean along and across towards that plane like a damped spring: it has inertia, so it rides
    /// the swell smoothly and doesn't twitch with every short wave or with the wave heights arriving from the GPU in
    /// steps (its speed only ever changes gradually).
    /// Put it on the object that should move (everything under it rides along, e.g. the grandfather sitting in it).
    /// </summary>
    public class BoatBob : MonoBehaviour
    {
        [Tooltip("The hull: its size sets where the waves are measured. Empty = all renderers under this object.")]
        public Renderer hull;
        [Tooltip("1 = rises and falls with the full height of the waves.")]
        [Range(0f, 1.5f)] public float heave = 1f;
        [Tooltip("1 = leans with the full slope of the waves.")]
        [Range(0f, 1.5f)] public float tilt = 1f;
        [Tooltip("Largest lean (degrees).")]
        [Range(0f, 60f)] public float maxTiltDegrees = 35f;
        [Tooltip("Seconds of one free bob up and down (heavier boat = longer: smoother, follows short waves less).")]
        [Min(0.2f)] public float heavePeriod = 1f;
        [Tooltip("Seconds of one free rock (pitch / roll).")]
        [Min(0.2f)] public float tiltPeriod = 1f;
        [Tooltip("How quickly the water calms the bobbing down (0 = keeps bobbing, 1 = no overshoot).")]
        [Range(0.1f, 1.5f)] public float damping = 0.6f;
        [Tooltip("Extra height above (+) or below (-) where it was placed (m), if the hull sits too deep or too high.")]
        public float draft = 0f;

        const int Grid = 3;
        const float MaxStep = 1f / 120f;   // the spring is integrated in steps no longer than this

        // A damped spring towards a moving target: the value has a velocity and only accelerates.
        struct Spring
        {
            public float value, velocity;

            public void Step(float target, float omega, float zeta, float dt)
            {
                float acceleration = omega * omega * (target - value) - 2f * zeta * omega * velocity;
                velocity += acceleration * dt;
                value += velocity * dt;
            }
        }

        readonly WaterProbe[] probes = new WaterProbe[Grid * Grid];
        readonly Vector2[] probeUV = new Vector2[Grid * Grid];   // along / across the hull (m), from its middle
        readonly Vector3[] points = new Vector3[Grid * Grid];
        Vector3 hullCentre;      // in the rest pose's space
        Vector3 alongLocal, acrossLocal;
        Vector3 restPosition;
        Quaternion restRotation;
        Spring height, slopeAlong, slopeAcross;
        bool started;

        void Awake()
        {
            for (int i = 0; i < probes.Length; i++) probes[i] = new WaterProbe();
        }

        void OnEnable()
        {
            foreach (WaterProbe probe in probes) WaterProbe.Register(probe);
        }

        void OnDisable()
        {
            foreach (WaterProbe probe in probes) WaterProbe.Unregister(probe);
        }

        void Start()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;

            // grid over the hull along its longer horizontal side (bow-stern) and across it
            Bounds local = HullBounds();
            hullCentre = new Vector3(local.center.x, 0f, local.center.z);
            Vector3 e = local.extents * 0.9f;
            bool longX = e.x > e.z;
            alongLocal = longX ? Vector3.right : Vector3.forward;
            acrossLocal = longX ? Vector3.forward : Vector3.right;
            float halfLength = longX ? e.x : e.z;
            float halfWidth = longX ? e.z : e.x;
            for (int a = 0; a < Grid; a++)
                for (int c = 0; c < Grid; c++)
                    probeUV[a * Grid + c] = new Vector2((a - 1) * halfLength, (c - 1) * halfWidth);
            PlaceProbes();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float level = WaterLevel();
            PlaceProbes();

            // least-squares plane y = h + sa*u + sc*v over the symmetric grid
            float sumY = 0f, sumUY = 0f, sumVY = 0f, sumUU = 0f, sumVV = 0f;
            for (int i = 0; i < probes.Length; i++)
            {
                float y = probes[i].HeightOr(level);
                points[i] = new Vector3(probes[i].position.x, y, probes[i].position.z);
                Vector2 uv = probeUV[i];
                sumY += y;
                sumUY += uv.x * y;
                sumVY += uv.y * y;
                sumUU += uv.x * uv.x;
                sumVV += uv.y * uv.y;
            }
            float targetHeight = (sumY / probes.Length - level) * heave;
            float targetAlong = sumUU > 1e-6f ? sumUY / sumUU * tilt : 0f;
            float targetAcross = sumVV > 1e-6f ? sumVY / sumVV * tilt : 0f;

            if (!started)
            {
                height.value = targetHeight;
                slopeAlong.value = targetAlong;
                slopeAcross.value = targetAcross;
                started = true;
            }

            // buoyancy: springs pulling the hull onto the water plane
            float heaveOmega = 2f * Mathf.PI / heavePeriod;
            float tiltOmega = 2f * Mathf.PI / tiltPeriod;
            int steps = Mathf.CeilToInt(dt / MaxStep);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                height.Step(targetHeight, heaveOmega, damping, h);
                slopeAlong.Step(targetAlong, tiltOmega, damping, h);
                slopeAcross.Step(targetAcross, tiltOmega, damping, h);
            }

            // the plane's normal from its slopes, as a lean
            Vector3 alongWorld = restRotation * alongLocal;
            Vector3 acrossWorld = restRotation * acrossLocal;
            Vector3 normal = (Vector3.up - alongWorld * slopeAlong.value - acrossWorld * slopeAcross.value).normalized;
            Quaternion lean = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), maxTiltDegrees);

            transform.SetPositionAndRotation(
                new Vector3(transform.position.x, restPosition.y + height.value + draft, transform.position.z),
                lean * restRotation);
        }

        // Measured where the hull floats (its rest heading, at its current place), not where it leans to.
        void PlaceProbes()
        {
            Vector3 middle = new Vector3(transform.position.x, 0f, transform.position.z) + restRotation * hullCentre;
            Vector3 alongWorld = restRotation * alongLocal;
            Vector3 acrossWorld = restRotation * acrossLocal;
            for (int i = 0; i < probes.Length; i++) probes[i].position = middle + alongWorld * probeUV[i].x + acrossWorld * probeUV[i].y;
        }

        float WaterLevel()
        {
            WaterSurface water = WaterSurface.FindAt(transform.position);
            if (water == null && WaterSurface.Instances.Count > 0) water = WaterSurface.Instances[0];
            return water != null ? water.WaterLevel : restPosition.y;
        }

        // The hull's box in this object's space (taken at the rest pose).
        Bounds HullBounds()
        {
            Renderer[] renderers = hull != null ? new[] { hull } : GetComponentsInChildren<Renderer>();
            var local = new Bounds(Vector3.zero, new Vector3(1f, 0.5f, 3f));
            bool any = false;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(corner);
                    if (any) local.Encapsulate(p);
                    else local = new Bounds(p, Vector3.zero);
                    any = true;
                }
            }
            return local;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            foreach (Vector3 p in points) Gizmos.DrawWireSphere(p, 0.06f);   // the measured wave surface
        }
    }
}

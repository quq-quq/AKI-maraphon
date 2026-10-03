using AKI.Water;
using UnityEngine;

namespace AKI.Menu
{
    /// <summary>
    /// A boat floating on the real waves like a body with weight. The water is asked for its wavy surface
    /// (<see cref="WaterProbe"/>, the same GPU wave height the player floats on) on a 3 x 3 grid over the hull, and a
    /// plane is fitted through those points (least squares), so the hull bridges the ripples. The springs follow the
    /// velocity of that plane, not a stationary target, and their tracking error is bounded: a falling wave must not
    /// leave the boat suspended in the air. GPU readback prediction comes from WaterProbe, not another time offset.
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
        [Tooltip("Maximum vertical lag behind the sampled hull waterline (m), on both rising and falling waves.")]
        [Min(0f)] public float maxHeightLag = 0.04f;
        [Tooltip("In play mode the hull gets a solid box collider around its mesh, so the player can't swim through the boat.")]
        public bool solidHull = true;
        [Tooltip("Maximum angular lag behind the water plane (degrees). Keeps a long hull from dipping its ends.")]
        [Range(0f, 10f)] public float maxTiltLagDegrees = 1.5f;

        const int Grid = 3;
        const float MaxStep = 1f / 120f;   // the spring is integrated in steps no longer than this

        // A damped spring towards a moving target: the value has a velocity and only accelerates.
        struct Spring
        {
            public float value, velocity;

            public void Step(float target, float targetVelocity, float omega, float zeta, float dt)
            {
                // Damping relative to the moving water, otherwise it resists the wave's own motion and lags.
                float acceleration = omega * omega * (target - value) - 2f * zeta * omega * (velocity - targetVelocity);
                velocity += acceleration * dt;
                value += velocity * dt;
            }

            public void LimitLag(float target, float targetVelocity, float limit)
            {
                float error = value - target;
                if (Mathf.Abs(error) <= limit) return;
                value = target + Mathf.Clamp(error, -limit, limit);
                // Do not retain momentum that would push farther away from the water next frame.
                if (error * (velocity - targetVelocity) > 0f) velocity = targetVelocity;
            }
        }

        readonly WaterProbe[] probes = new WaterProbe[Grid * Grid];
        readonly Vector2[] probeUV = new Vector2[Grid * Grid];   // along / across the hull (m), from its middle
        readonly Vector3[] points = new Vector3[Grid * Grid];
        Vector3 hullCentre;      // in the rest pose's space
        Vector3 alongLocal, acrossLocal;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 restScale;
        Spring height, slopeAlong, slopeAcross;
        bool started;

        void Awake()
        {
            for (int i = 0; i < probes.Length; i++) probes[i] = new WaterProbe();
        }

        void OnEnable()
        {
            // Awake is not called again after an editor script reload; readonly arrays are recreated empty.
            for (int i = 0; i < probes.Length; i++)
                if (probes[i] == null) probes[i] = new WaterProbe();
            if (restScale.sqrMagnitude > 0f) InitializeGrid();
            started = false;
            foreach (WaterProbe probe in probes) WaterProbe.Register(probe);
        }

        void OnDisable()
        {
            foreach (WaterProbe probe in probes) WaterProbe.Unregister(probe);
        }

        void Start()
        {
            if (solidHull) MakeHullSolid();
            restPosition = transform.position;
            restRotation = transform.rotation;
            restScale = transform.lossyScale;
            InitializeGrid();
        }

        // A box around the hull's mesh (it rides along with the boat); skipped when the hull has a collider already.
        // A box, not the mesh: the hull has too many faces for a convex mesh collider.
        void MakeHullSolid()
        {
            Renderer body = hull != null ? hull : GetComponentInChildren<MeshRenderer>();
            if (body == null || body.GetComponent<Collider>() != null) return;
            MeshFilter filter = body.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            BoxCollider solid = body.gameObject.AddComponent<BoxCollider>();
            solid.center = filter.sharedMesh.bounds.center;
            solid.size = filter.sharedMesh.bounds.size;
        }

        void InitializeGrid()
        {
            // grid over the hull along its longer horizontal side (bow-stern) and across it
            Bounds local = HullBounds();
            hullCentre = new Vector3(local.center.x, 0f, local.center.z);
            Vector3 e = Vector3.Scale(local.extents, restScale) * 0.9f;
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

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float level = WaterLevel();
            PlaceProbes();

            // least-squares plane y = h + sa*u + sc*v over the symmetric grid
            float sumY = 0f, sumUY = 0f, sumVY = 0f, sumUU = 0f, sumVV = 0f;
            float sumRate = 0f, sumURate = 0f, sumVRate = 0f;
            bool allReady = true;
            for (int i = 0; i < probes.Length; i++)
            {
                float y = probes[i].HeightOr(level);
                allReady &= probes[i].HasData;
                points[i] = new Vector3(probes[i].position.x, y, probes[i].position.z);
                Vector2 uv = probeUV[i];
                sumY += y;
                sumUY += uv.x * y;
                sumVY += uv.y * y;
                sumUU += uv.x * uv.x;
                sumVV += uv.y * uv.y;
                float rate = probes[i].HasData ? probes[i].HeightRate : 0f;
                sumRate += rate;
                sumURate += uv.x * rate;
                sumVRate += uv.y * rate;
            }
            // Do not initialize from the flat fallback while the first GPU query is still pending.
            if (!allReady) return;
            float targetHeight = (sumY / probes.Length - level) * heave;
            float targetAlong = sumUU > 1e-6f ? sumUY / sumUU * tilt : 0f;
            float targetAcross = sumVV > 1e-6f ? sumVY / sumVV * tilt : 0f;
            float heightRate = sumRate / probes.Length * heave;
            float alongRate = sumUU > 1e-6f ? sumURate / sumUU * tilt : 0f;
            float acrossRate = sumVV > 1e-6f ? sumVRate / sumVV * tilt : 0f;

            if (!started)
            {
                height.value = targetHeight;
                slopeAlong.value = targetAlong;
                slopeAcross.value = targetAcross;
                height.velocity = heightRate;
                slopeAlong.velocity = alongRate;
                slopeAcross.velocity = acrossRate;
                started = true;
            }

            // buoyancy: springs pulling the hull onto the water plane
            float heaveOmega = 2f * Mathf.PI / Mathf.Max(0.2f, heavePeriod);
            float tiltOmega = 2f * Mathf.PI / Mathf.Max(0.2f, tiltPeriod);
            dt = Mathf.Min(dt, 0.1f); // a stalled frame must not trigger hundreds of spring substeps
            int steps = Mathf.CeilToInt(dt / MaxStep);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float remaining = dt - (i + 1) * h;
                height.Step(targetHeight - heightRate * remaining, heightRate, heaveOmega, damping, h);
                slopeAlong.Step(targetAlong - alongRate * remaining, alongRate, tiltOmega, damping, h);
                slopeAcross.Step(targetAcross - acrossRate * remaining, acrossRate, tiltOmega, damping, h);
            }
            height.LimitLag(targetHeight, heightRate, Mathf.Max(0f, maxHeightLag));

            // the plane's normal from its slopes, as a lean
            Vector3 alongWorld = restRotation * alongLocal;
            Vector3 acrossWorld = restRotation * acrossLocal;
            Vector3 normal = (Vector3.up - alongWorld * slopeAlong.value - acrossWorld * slopeAcross.value).normalized;
            Quaternion lean = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), maxTiltDegrees);
            Vector3 targetNormal = (Vector3.up - alongWorld * targetAlong - acrossWorld * targetAcross).normalized;
            Quaternion targetLean = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, targetNormal), maxTiltDegrees);
            lean = Quaternion.RotateTowards(targetLean, lean, Mathf.Max(0f, maxTiltLagDegrees));
            // Store the limited slope as well, so the springs cannot accumulate hidden angular overshoot.
            Vector3 limitedNormal = lean * Vector3.up;
            slopeAlong.value = -Vector3.Dot(limitedNormal, alongWorld) / Mathf.Max(0.01f, limitedNormal.y);
            slopeAcross.value = -Vector3.Dot(limitedNormal, acrossWorld) / Mathf.Max(0.01f, limitedNormal.y);

            // Float about the measured hull centre, not an arbitrary prefab origin.
            Vector3 centreOffset = restRotation * Vector3.Scale(hullCentre, restScale);
            float pivotCorrection = centreOffset.y - (lean * centreOffset).y;

            transform.SetPositionAndRotation(
                new Vector3(transform.position.x, restPosition.y + height.value + draft + pivotCorrection, transform.position.z),
                lean * restRotation);
        }

        // Measured where the hull floats (its rest heading, at its current place), not where it leans to.
        void PlaceProbes()
        {
            Vector3 middle = new Vector3(transform.position.x, 0f, transform.position.z) + restRotation * Vector3.Scale(hullCentre, restScale);
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
                // Never inverse-transform a world AABB: yaw expands it and places probes outside a narrow hull.
                Bounds b = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
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

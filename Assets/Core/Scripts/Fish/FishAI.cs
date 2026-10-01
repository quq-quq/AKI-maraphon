using System.Collections.Generic;
using AKI.Water;
using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// Tuna movement: keeps around the player at a set distance and moves in jerky darts along the circle around them:
    /// a quick flick to the next spot, a short hold with a small tremble, the next flick (sometimes back the other way).
    /// It never comes closer than <see cref="minDistance"/>, stays under the water and above the bottom.
    /// A fresh fish first swims in from where the spawner put it. Darts are planned a few steps ahead, and the gizmos
    /// draw the path: where the fish has been, the current dart and the planned ones.
    /// </summary>
    public class FishAI : MonoBehaviour
    {
        enum State { Approach, Dart, Hold, Stopped }

        // A spot on the circle around the player: angle (degrees around Y), horizontal distance, height above the eyes.
        struct Waypoint
        {
            public float angle, radius, height;
            public float dartSeconds;   // time to get here
            public float holdSeconds;   // time to stay here
        }

        const float TrailStep = 0.05f;
        const int ArcSegments = 16;

        [Tooltip("What the fish circles (the player's camera). Empty = the main camera.")]
        public Transform target;

        [Header("Distance")]
        [Tooltip("Usual distance from the player (horizontal, m).")]
        [Min(0.5f)] public float orbitRadius = 6f;
        [Tooltip("Each dart picks a distance within ± this of the orbit radius.")]
        [Min(0f)] public float radiusSpread = 1.5f;
        [Tooltip("The fish never comes closer than this to the player (m).")]
        [Min(0.5f)] public float minDistance = 3.5f;
        [Tooltip("How far above / below the player's eyes the fish may go (m).")]
        [Min(0f)] public float heightRange = 2f;
        [Tooltip("Seconds the circle's centre lags behind the player, so the fish doesn't copy every move.")]
        [Min(0f)] public float followLag = 0.6f;

        [Header("Darts")]
        [Tooltip("Degrees around the player per dart (min, max).")]
        public Vector2 dartAngle = new Vector2(20f, 65f);
        [Tooltip("Seconds per dart (min, max).")]
        public Vector2 dartSeconds = new Vector2(0.35f, 0.8f);
        [Tooltip("Seconds of holding still between darts (min, max).")]
        public Vector2 holdSeconds = new Vector2(0.2f, 1f);
        [Tooltip("Largest height change per dart (m).")]
        [Min(0f)] public float heightStep = 1f;
        [Tooltip("Chance that the next dart goes back the other way.")]
        [Range(0f, 1f)] public float turnBackChance = 0.3f;

        [Header("Twitching")]
        [Tooltip("Size of the constant small tremble (m).")]
        [Min(0f)] public float jitterStrength = 0.06f;
        [Tooltip("Speed of the tremble (Hz).")]
        [Min(0f)] public float jitterFrequency = 7f;

        [Header("Swimming")]
        [Tooltip("Speed when coming in from the spawn point (m/s).")]
        [Min(0.1f)] public float approachSpeed = 4f;
        [Tooltip("How quickly the fish turns to where it swims (higher = snappier).")]
        [Min(0.1f)] public float turnSharpness = 10f;
        [Tooltip("Tail wag (degrees) at full speed.")]
        [Min(0f)] public float wiggleDegrees = 8f;
        [Tooltip("Tail wags per second at full speed.")]
        [Min(0f)] public float wiggleFrequency = 3f;

        [Header("Staying in the water")]
        [Tooltip("Kept at least this far below the mean water level (m).")]
        [Min(0f)] public float surfaceMargin = 0.8f;
        [Tooltip("Kept at least this far above whatever is under it (m). 0 = off.")]
        [Min(0f)] public float bottomClearance = 0.6f;
        [Tooltip("What counts as the bottom (everything but Ignore Raycast and Water).")]
        public LayerMask groundMask = ~((1 << 2) | (1 << 4));

        [Header("Gizmos")]
        public bool drawGizmos = true;
        [Tooltip("Seconds of the path behind the fish to draw.")]
        [Min(0f)] public float trailSeconds = 3f;
        [Tooltip("How many darts are planned (and drawn) ahead.")]
        [Range(1, 8)] public int plannedDarts = 3;

        static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        State state = State.Approach;
        readonly List<Waypoint> plan = new List<Waypoint>();
        readonly Queue<Vector3> trail = new Queue<Vector3>();
        Waypoint from, to;              // the current dart (or the spot held, which is 'to')
        float timer;
        int direction = 1;
        Vector3 centre, centreVelocity;
        Vector3 basePosition;           // where the fish swims, without the tremble
        Vector3 lastBase;
        Quaternion heading;
        float wigglePhase;
        float noiseSeed;
        float trailTimer;

        public bool IsStopped => state == State.Stopped;

        /// <summary>Movement speed without the tremble (m/s).</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Called by the spawner right after the fish is created.</summary>
        public void Init(Transform player)
        {
            target = player;
        }

        /// <summary>Stops all movement (the fish was hit): whoever stopped it moves it from now on.</summary>
        public void Stop()
        {
            state = State.Stopped;
            Velocity = Vector3.zero;
        }

        void Start()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;

            centre = TargetPosition();
            basePosition = lastBase = transform.position;
            heading = transform.rotation;
            noiseSeed = Random.value * 100f;
            direction = Random.value < 0.5f ? -1 : 1;

            // the first spot on the circle: the side of the player the fish comes from
            Vector3 offset = transform.position - centre;
            to = new Waypoint
            {
                angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg,
                radius = RandomRadius(),
                height = Mathf.Clamp(offset.y, -heightRange, heightRange),
                holdSeconds = Random.Range(holdSeconds.x, holdSeconds.y),
            };
            from = to;
            FillPlan();
            state = State.Approach;
        }

        void Update()
        {
            if (state == State.Stopped) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 player = TargetPosition();
            centre = followLag > 0f ? Vector3.SmoothDamp(centre, player, ref centreVelocity, followLag) : player;

            switch (state)
            {
                case State.Approach: Approach(dt); break;
                case State.Hold: Hold(dt); break;
                case State.Dart: Dart(dt); break;
            }

            Velocity = (basePosition - lastBase) / dt;
            lastBase = basePosition;

            Vector3 position = Constrain(basePosition + Tremble(Time.time), player);
            transform.SetPositionAndRotation(position, Turn(dt));
            RecordTrail(position, dt);
        }

        // ------------------------------------------------------------------ states

        void Approach(float dt)
        {
            Vector3 spot = OrbitPoint(to);
            basePosition = Vector3.MoveTowards(basePosition, spot, approachSpeed * dt);
            if ((basePosition - spot).sqrMagnitude < 0.01f)
            {
                state = State.Hold;
                timer = 0f;
            }
        }

        void Hold(float dt)
        {
            basePosition = OrbitPoint(to);
            timer += dt;
            if (timer < to.holdSeconds) return;

            // flick to the next planned spot
            from = to;
            FillPlan();
            to = plan[0];
            plan.RemoveAt(0);
            FillPlan();
            timer = 0f;
            state = State.Dart;
        }

        void Dart(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / Mathf.Max(0.01f, to.dartSeconds));
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // full speed at once, then brakes: a jerk, not a glide
            basePosition = OrbitPoint(Lerp(from, to, eased));
            if (t < 1f) return;
            state = State.Hold;
            timer = 0f;
        }

        // ------------------------------------------------------------------ planning

        void FillPlan()
        {
            while (plan.Count > plannedDarts) plan.RemoveAt(plan.Count - 1);
            while (plan.Count < plannedDarts) plan.Add(Next(plan.Count > 0 ? plan[plan.Count - 1] : to));
        }

        Waypoint Next(Waypoint previous)
        {
            if (Random.value < turnBackChance) direction = -direction;
            return new Waypoint
            {
                angle = previous.angle + direction * Random.Range(dartAngle.x, dartAngle.y),
                radius = RandomRadius(),
                height = Mathf.Clamp(previous.height + Random.Range(-heightStep, heightStep), -heightRange, heightRange),
                dartSeconds = Random.Range(dartSeconds.x, dartSeconds.y),
                holdSeconds = Random.Range(holdSeconds.x, holdSeconds.y),
            };
        }

        float RandomRadius()
        {
            return Mathf.Max(minDistance, orbitRadius + Random.Range(-radiusSpread, radiusSpread));
        }

        static Waypoint Lerp(Waypoint a, Waypoint b, float t)
        {
            return new Waypoint
            {
                angle = Mathf.Lerp(a.angle, b.angle, t),
                radius = Mathf.Lerp(a.radius, b.radius, t),
                height = Mathf.Lerp(a.height, b.height, t),
            };
        }

        Vector3 OrbitPoint(Waypoint w)
        {
            float a = w.angle * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Sin(a) * w.radius, w.height, Mathf.Cos(a) * w.radius);
        }

        // ------------------------------------------------------------------ motion helpers

        Vector3 TargetPosition()
        {
            return target != null ? target.position : centre;
        }

        Vector3 Tremble(float time)
        {
            if (jitterStrength <= 0f) return Vector3.zero;
            float t = time * jitterFrequency;
            return new Vector3(
                Mathf.PerlinNoise(t, noiseSeed) - 0.5f,
                Mathf.PerlinNoise(noiseSeed + 17.3f, t) - 0.5f,
                Mathf.PerlinNoise(t + 31.7f, noiseSeed + 5.1f) - 0.5f) * (2f * jitterStrength);
        }

        // Under the water, above the bottom, and never closer to the player than minDistance (that one wins).
        Vector3 Constrain(Vector3 p, Vector3 player)
        {
            WaterSurface water = WaterSurface.FindAt(p);
            if (water != null) p.y = Mathf.Min(p.y, water.WaterLevel - surfaceMargin);
            if (bottomClearance > 0f) p.y = Mathf.Max(p.y, BottomAt(p) + bottomClearance);

            Vector3 away = p - player;
            if (away.sqrMagnitude < minDistance * minDistance)
            {
                // push out sideways, keeping the height
                Vector3 flat = new Vector3(away.x, 0f, away.z);
                if (flat.sqrMagnitude < 1e-6f) flat = new Vector3(Mathf.Sin(to.angle * Mathf.Deg2Rad), 0f, Mathf.Cos(to.angle * Mathf.Deg2Rad));
                float side = Mathf.Sqrt(Mathf.Max(0f, minDistance * minDistance - away.y * away.y));
                p = new Vector3(player.x, p.y, player.z) + flat.normalized * side;
            }
            return p;
        }

        float BottomAt(Vector3 p)
        {
            const float above = 2f;
            int count = Physics.RaycastNonAlloc(p + Vector3.up * above, Vector3.down, GroundHits, above + bottomClearance, groundMask, QueryTriggerInteraction.Ignore);
            float bottom = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Transform hit = GroundHits[i].transform;
                if (hit.IsChildOf(transform) || (target != null && hit.IsChildOf(target.root))) continue;   // itself, a stuck arrow, the player
                bottom = Mathf.Max(bottom, GroundHits[i].point.y);
            }
            return bottom;
        }

        // Faces where it swims (pitch kept under 45°) and wags its tail faster the faster it goes.
        Quaternion Turn(float dt)
        {
            Vector3 v = Velocity;
            float flat = new Vector2(v.x, v.z).magnitude;
            if (v.sqrMagnitude > 0.25f && flat > 1e-3f)
            {
                v.y = Mathf.Clamp(v.y, -flat, flat);
                heading = Quaternion.Slerp(heading, Quaternion.LookRotation(v), 1f - Mathf.Exp(-turnSharpness * dt));
            }
            float speed01 = Mathf.Clamp01(Velocity.magnitude / approachSpeed);
            wigglePhase += dt * wiggleFrequency * Mathf.Lerp(0.4f, 1f, speed01) * 2f * Mathf.PI;
            return heading * Quaternion.Euler(0f, Mathf.Sin(wigglePhase) * wiggleDegrees * speed01, 0f);
        }

        void RecordTrail(Vector3 position, float dt)
        {
            trailTimer += dt;
            if (trailTimer < TrailStep) return;
            trailTimer = 0f;
            trail.Enqueue(position);
            int max = Mathf.CeilToInt(trailSeconds / TrailStep);
            while (trail.Count > max) trail.Dequeue();
        }

        // ------------------------------------------------------------------ gizmos

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            bool playing = Application.isPlaying;
            Vector3 circleCentre = playing ? centre : target != null ? target.position : transform.position;
            Vector3 player = playing ? TargetPosition() : circleCentre;

            // the circle the fish keeps to, and the distance it never comes closer than
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            DrawCircle(circleCentre, orbitRadius);
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.6f);
            DrawCircle(player, minDistance);
            if (!playing) return;

            // where it has been
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Vector3 previous = Vector3.zero;
            bool first = true;
            foreach (Vector3 p in trail)
            {
                if (!first) Gizmos.DrawLine(previous, p);
                previous = p;
                first = false;
            }
            if (!first) Gizmos.DrawLine(previous, transform.position);

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawLine(transform.position, player);
            if (state == State.Stopped) return;

            // where it is going now
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 1f);
            if (state == State.Approach) Gizmos.DrawLine(transform.position, OrbitPoint(to));
            else if (state == State.Dart) DrawArc(from, to);
            Gizmos.DrawWireSphere(OrbitPoint(to), 0.2f);

            // and the darts after that
            Waypoint last = to;
            for (int i = 0; i < plan.Count; i++)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.3f, Mathf.Lerp(0.9f, 0.3f, plan.Count > 1 ? i / (plan.Count - 1f) : 0f));
                DrawArc(last, plan[i]);
                Gizmos.DrawWireSphere(OrbitPoint(plan[i]), 0.12f);
                last = plan[i];
            }
        }

        void DrawArc(Waypoint a, Waypoint b)
        {
            Vector3 previous = OrbitPoint(a);
            for (int i = 1; i <= ArcSegments; i++)
            {
                Vector3 p = OrbitPoint(Lerp(a, b, i / (float)ArcSegments));
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }

        static void DrawCircle(Vector3 c, float radius)
        {
            const int segments = 48;
            Vector3 previous = c + new Vector3(0f, 0f, radius);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                Vector3 p = c + new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }
    }
}

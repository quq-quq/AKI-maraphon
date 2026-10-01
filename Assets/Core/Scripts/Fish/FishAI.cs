using System.Collections.Generic;
using AKI.Water;
using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// Tuna movement: swims slowly around the player along a sine wave that bends the path both sideways (distance
    /// from the player swings in and out) and up and down (around its own depth). Every fish picks its depth, distance,
    /// wave start and direction at random.
    /// Like the player (<see cref="AKI.Player.FirstPersonSwimController"/>) it asks the water for the real wavy surface
    /// above it (<see cref="WaterProbe"/>), and always keeps its back at least <see cref="surfaceMargin"/> under it.
    /// It never comes closer than <see cref="minDistance"/> (horizontally).
    /// A fresh fish first swims in from where the spawner put it. The gizmos draw the whole path and where it has been.
    /// </summary>
    public class FishAI : MonoBehaviour
    {
        enum State { Approach, Swim, Stopped }

        const float TrailStep = 0.05f;
        const int PathSegments = 128;

        [Tooltip("What the fish circles (the player's camera). Empty = the main camera.")]
        public Transform target;

        [Header("Depth")]
        [Tooltip("Average depth under the sea level (m, min / max). Every fish picks one at random.")]
        public Vector2 depthRange = new Vector2(4f, 7f);
        [Tooltip("The fish's back always stays at least this far under the real (wavy) surface (m).")]
        [Min(0f)] public float surfaceMargin = 1.5f;
        [Tooltip("From the fish's centre up to its back (m).")]
        [Min(0f)] public float bodyHalfHeight = 0.2f;

        [Header("Path: sideways wave")]
        [Tooltip("Average distance from the player (horizontal, m).")]
        [Min(0.5f)] public float orbitRadius = 6f;
        [Tooltip("Every fish picks its own average distance within ± this.")]
        [Min(0f)] public float radiusSpread = 1f;
        [Tooltip("How far the wave swings in and out of the circle (m).")]
        [Min(0f)] public float waveAmplitude = 1.5f;
        [Tooltip("Sideways waves per full lap around the player.")]
        [Range(1, 12)] public int waveCount = 4;

        [Header("Path: up-and-down wave")]
        [Tooltip("How far the wave swings above and below the fish's depth (m).")]
        [Min(0f)] public float verticalAmplitude = 1f;
        [Tooltip("Up-and-down waves per full lap (different from the sideways count = a less repetitive path).")]
        [Range(1, 12)] public int verticalWaveCount = 3;

        [Header("Distance")]
        [Tooltip("The fish never comes closer than this to the player (horizontal, m).")]
        [Min(0.5f)] public float minDistance = 3.5f;
        [Tooltip("Seconds the circle's centre lags behind the player, so the fish doesn't copy every move.")]
        [Min(0f)] public float followLag = 0.8f;

        [Header("Swimming")]
        [Tooltip("Speed along the path (m/s).")]
        [Min(0.1f)] public float swimSpeed = 1.2f;
        [Tooltip("Speed when coming in from the spawn point (m/s).")]
        [Min(0.1f)] public float approachSpeed = 2.5f;
        [Tooltip("How quickly the fish turns to where it swims (higher = snappier).")]
        [Min(0.1f)] public float turnSharpness = 4f;
        [Tooltip("Largest nose up / down angle (degrees).")]
        [Range(0f, 60f)] public float maxPitch = 25f;
        [Tooltip("Tail wag (degrees) at full speed.")]
        [Min(0f)] public float wiggleDegrees = 8f;
        [Tooltip("Tail wags per second at full speed.")]
        [Min(0f)] public float wiggleFrequency = 2f;

        [Header("Gizmos")]
        public bool drawGizmos = true;
        [Tooltip("Seconds of the path behind the fish to draw.")]
        [Min(0f)] public float trailSeconds = 4f;

        readonly WaterProbe probe = new WaterProbe();
        readonly Queue<Vector3> trail = new Queue<Vector3>();
        State state = State.Approach;
        float depth;           // this fish's average depth under the sea level
        float radius;          // this fish's average distance
        float phase;           // where on the sideways wave it starts
        float verticalPhase;   // where on the up-and-down wave it starts
        int direction = 1;     // clockwise or not
        float angle;           // degrees around the player
        Vector3 centre, centreVelocity;
        Vector3 lastPosition;
        Quaternion heading;
        float wigglePhase;
        float trailTimer;

        public bool IsStopped => state == State.Stopped;

        /// <summary>This fish's average depth under the sea level (m).</summary>
        public float Depth => depth;

        /// <summary>Metres from the fish's back up to the real wave surface (negative = sticking out).</summary>
        public float DepthUnderSurface { get; private set; }

        /// <summary>Same test as the player's head: the whole fish is under the wavy surface.</summary>
        public bool IsUnderwater => DepthUnderSurface > 0f;

        public Vector3 Velocity { get; private set; }

        /// <summary>Called by the spawner right after the fish is created.</summary>
        public void Init(Transform player)
        {
            target = player;
            centre = player != null ? player.position : transform.position;
            angle = AngleAround(transform.position);
            Vector3 p = transform.position;
            p.y = PathHeight(angle);   // at its depth from the very first frame
            transform.position = p;
        }

        /// <summary>Stops all movement (the fish was hit): whoever stopped it moves it from now on.</summary>
        public void Stop()
        {
            state = State.Stopped;
            Velocity = Vector3.zero;
        }

        void Awake()
        {
            // every fish its own depth, distance, wave start and direction
            depth = Random.Range(depthRange.x, depthRange.y);
            // the inner swing of the wave must stay outside the minimum distance
            radius = Mathf.Max(orbitRadius + Random.Range(-radiusSpread, radiusSpread), minDistance + waveAmplitude);
            phase = Random.Range(0f, 2f * Mathf.PI);
            verticalPhase = Random.Range(0f, 2f * Mathf.PI);
            direction = Random.value < 0.5f ? -1 : 1;
        }

        void OnEnable()
        {
            WaterProbe.Register(probe);
        }

        void OnDisable()
        {
            WaterProbe.Unregister(probe);
        }

        void Start()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;

            centre = TargetPosition();
            angle = AngleAround(transform.position);
            transform.position = lastPosition = new Vector3(transform.position.x, PathHeight(angle), transform.position.z);
            Vector3 look = Flat(PathPoint(angle) - transform.position);
            heading = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look) : transform.rotation;
            state = State.Approach;
        }

        void Update()
        {
            probe.position = transform.position;   // read back by the water for the next frames
            if (state == State.Stopped) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 player = TargetPosition();
            centre = followLag > 0f ? Vector3.SmoothDamp(centre, player, ref centreVelocity, followLag) : player;

            Vector3 position = state == State.Approach ? Approach(dt) : Swim(dt);
            position = KeepAway(position, player);
            position.y = KeepUnder(position);

            Velocity = (position - lastPosition) / dt;
            lastPosition = position;
            transform.SetPositionAndRotation(position, Turn(dt));
            RecordTrail(position, dt);
        }

        // ------------------------------------------------------------------ path

        Vector3 Approach(float dt)
        {
            Vector3 spot = PathPoint(angle);
            Vector3 position = Vector3.MoveTowards(transform.position, spot, approachSpeed * dt);
            if ((position - spot).sqrMagnitude < 0.01f) state = State.Swim;
            return position;
        }

        Vector3 Swim(float dt)
        {
            // constant speed along the curve: the angle step depends on how long the curve is here
            float a = angle * Mathf.Deg2Rad;
            float r = WaveRadius(a);
            float dr = waveAmplitude * waveCount * Mathf.Cos(waveCount * a + phase);
            float dy = verticalAmplitude * verticalWaveCount * Mathf.Cos(verticalWaveCount * a + verticalPhase);
            float step = swimSpeed * dt / Mathf.Max(0.1f, Mathf.Sqrt(r * r + dr * dr + dy * dy));
            angle += direction * step * Mathf.Rad2Deg;
            return PathPoint(angle);
        }

        float WaveRadius(float radians)
        {
            return radius + waveAmplitude * Mathf.Sin(waveCount * radians + phase);
        }

        float PathHeight(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return SeaLevel() - depth + verticalAmplitude * Mathf.Sin(verticalWaveCount * a + verticalPhase);
        }

        Vector3 PathPoint(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float r = WaveRadius(a);
            return new Vector3(centre.x + Mathf.Sin(a) * r, PathHeight(degrees), centre.z + Mathf.Cos(a) * r);
        }

        float AngleAround(Vector3 p)
        {
            return Mathf.Atan2(p.x - centre.x, p.z - centre.z) * Mathf.Rad2Deg;
        }

        // Mean sea level here. Without any water: y = 0.
        float SeaLevel()
        {
            WaterSurface water = WaterSurface.FindAt(centre);
            if (water == null && WaterSurface.Instances.Count > 0) water = WaterSurface.Instances[0];
            return water != null ? water.WaterLevel : 0f;
        }

        // The same check as the player's head: the real wave height above the fish (GPU probe; the mean level
        // until the first readback). The back of the fish stays surfaceMargin under it.
        float KeepUnder(Vector3 p)
        {
            float surface = probe.HeightOr(SeaLevel());
            float y = Mathf.Min(p.y, surface - surfaceMargin - bodyHalfHeight);
            DepthUnderSurface = surface - (y + bodyHalfHeight);
            return y;
        }

        Vector3 TargetPosition()
        {
            return target != null ? target.position : centre;
        }

        // The centre lags behind, so the player can catch up with the fish: push it back out sideways.
        Vector3 KeepAway(Vector3 p, Vector3 player)
        {
            Vector3 flat = new Vector3(p.x - player.x, 0f, p.z - player.z);
            if (flat.sqrMagnitude >= minDistance * minDistance) return p;
            if (flat.sqrMagnitude < 1e-6f) flat = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
            return new Vector3(player.x, p.y, player.z) + flat.normalized * minDistance;
        }

        static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        // ------------------------------------------------------------------ looks

        // Faces where it swims (nose up / down limited to maxPitch) and wags its tail faster the faster it goes.
        Quaternion Turn(float dt)
        {
            Vector3 v = Velocity;
            float flat = new Vector2(v.x, v.z).magnitude;
            if (v.sqrMagnitude > 0.01f && flat > 1e-3f)
            {
                v.y = Mathf.Clamp(v.y, -flat * Mathf.Tan(maxPitch * Mathf.Deg2Rad), flat * Mathf.Tan(maxPitch * Mathf.Deg2Rad));
                heading = Quaternion.Slerp(heading, Quaternion.LookRotation(v), 1f - Mathf.Exp(-turnSharpness * dt));
            }
            float speed01 = Mathf.Clamp01(Velocity.magnitude / Mathf.Max(swimSpeed, approachSpeed));
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
            Vector3 player = playing ? TargetPosition() : target != null ? target.position : transform.position;

            // the distance the fish never comes closer than (at the fish's height)
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.6f);
            DrawCircle(new Vector3(player.x, transform.position.y, player.z), minDistance);
            if (!playing) return;

            // the whole wavy path around the player
            Gizmos.color = state == State.Stopped ? new Color(1f, 0.8f, 0.2f, 0.25f) : new Color(1f, 0.8f, 0.2f, 0.8f);
            Vector3 previous = PathPoint(0f);
            for (int i = 1; i <= PathSegments; i++)
            {
                Vector3 p = PathPoint(i * 360f / PathSegments);
                Gizmos.DrawLine(previous, p);
                previous = p;
            }

            // where it has been
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            bool first = true;
            foreach (Vector3 p in trail)
            {
                if (!first) Gizmos.DrawLine(previous, p);
                previous = p;
                first = false;
            }
            if (!first) Gizmos.DrawLine(previous, transform.position);

            // up to the real surface: blue while under it, red if it would stick out
            Gizmos.color = IsUnderwater ? new Color(0.3f, 0.5f, 1f, 0.8f) : Color.red;
            Vector3 back = transform.position + Vector3.up * bodyHalfHeight;
            Gizmos.DrawLine(back, back + Vector3.up * DepthUnderSurface);

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawLine(transform.position, player);
            if (state == State.Approach)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.3f, 1f);
                Gizmos.DrawLine(transform.position, PathPoint(angle));
                Gizmos.DrawWireSphere(PathPoint(angle), 0.2f);
            }
        }

        static void DrawCircle(Vector3 c, float r)
        {
            const int segments = 48;
            Vector3 previous = c + new Vector3(0f, 0f, r);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                Vector3 p = c + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }
    }
}

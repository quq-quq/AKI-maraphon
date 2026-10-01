using AKI.Weapons;
using UnityEngine;
using UnityEngine.Events;

namespace AKI.Fish
{
    /// <summary>
    /// Keeps exactly one fish in the water. It appears far from the player and outside their view, where the murky water
    /// hides it, and swims in to circle them (<see cref="FishAI"/>). Once it is caught (<see cref="Catchable"/> has melted
    /// it away) the next one comes after <see cref="respawnDelay"/>.
    /// </summary>
    public class FishSpawner : MonoBehaviour
    {
        [Tooltip("The fish prefab (FishAI + FishCollision + Catchable).")]
        public FishAI fishPrefab;
        [Tooltip("Who the fish circles: the player's camera. Empty = the main camera.")]
        public Transform player;
        public bool spawnOnStart = true;
        [Min(0f)] public float firstSpawnDelay = 1f;
        [Tooltip("Seconds from a fish being gone to the next one appearing.")]
        [Min(0f)] public float respawnDelay = 2f;

        [Header("Where")]
        [Tooltip("Distance from the player where a new fish appears (m): far enough for the water to hide it.")]
        [Min(1f)] public float spawnDistance = 22f;
        [Tooltip("Half-angle in front of the player where fish never appear (degrees).")]
        [Range(0f, 179f)] public float hiddenAngle = 70f;

        [Header("Events")]
        [Tooltip("A new fish is in the water.")]
        public UnityEvent<Transform> onFishSpawned = new UnityEvent<Transform>();
        [Tooltip("The fish took a harpoon.")]
        public UnityEvent<Transform> onFishHit = new UnityEvent<Transform>();
        [Tooltip("The fish is caught and gone.")]
        public UnityEvent onFishCaught = new UnityEvent();

        [Header("Gizmos")]
        public bool drawGizmos = true;

        FishAI current;
        bool active;
        float spawnAt = -1f;

        /// <summary>The fish now in the water (null between fish).</summary>
        public FishAI CurrentFish => current;

        /// <summary>A fish is in the water and hasn't been hit yet.</summary>
        public bool IsFishAlive => current != null && !(current.TryGetComponent(out FishCollision hit) && hit.IsHit);

        void Start()
        {
            if (spawnOnStart) StartSpawning(firstSpawnDelay);
        }

        /// <summary>Starts keeping a fish in the water; the first one comes after <paramref name="delay"/>.</summary>
        public void StartSpawning(float delay = 0f)
        {
            active = true;
            if (current == null) spawnAt = Time.time + delay;
        }

        /// <summary>No new fish from now on (the one in the water stays).</summary>
        public void StopSpawning()
        {
            active = false;
            spawnAt = -1f;
        }

        void Update()
        {
            if (!active || current != null) return;
            if (spawnAt < 0f) spawnAt = Time.time + respawnDelay;   // the fish is gone (caught, or removed some other way)
            if (Time.time >= spawnAt) Spawn();
        }

        void Spawn()
        {
            spawnAt = -1f;
            if (fishPrefab == null)
            {
                Debug.LogWarning("FishSpawner: no fish prefab.", this);
                active = false;
                return;
            }

            Transform eye = Eye();
            if (eye == null) return;   // no player / camera yet: try again next frame

            Vector3 position = SpawnPoint(eye);
            Vector3 look = Vector3.ProjectOnPlane(eye.position - position, Vector3.up);
            current = Instantiate(fishPrefab, position, look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look) : Quaternion.identity);
            current.name = fishPrefab.name;
            current.Init(eye);

            Transform fish = current.transform;
            if (current.TryGetComponent(out FishCollision collision)) collision.onHit.AddListener((_, _) => onFishHit.Invoke(fish));
            if (current.TryGetComponent(out Catchable catchable)) catchable.onCaught.AddListener(onFishCaught.Invoke);
            onFishSpawned.Invoke(fish);
        }

        // Somewhere around the player, behind or beside them, never in the cone they look into.
        // The height doesn't matter: the fish puts itself at its own depth (FishAI.Init).
        Vector3 SpawnPoint(Transform eye)
        {
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            float yaw = Random.Range(hiddenAngle, 360f - hiddenAngle);
            Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * forward.normalized;
            return eye.position + direction * spawnDistance;
        }

        Transform Eye()
        {
            if (player != null) return player;
            return Camera.main != null ? Camera.main.transform : null;
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            Transform eye = Eye();
            if (eye == null) return;

            // the ring new fish appear on, without the part in front of the player
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.6f);
            const int segments = 48;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float yaw = Mathf.Lerp(hiddenAngle, 360f - hiddenAngle, i / (float)segments);
                Vector3 p = eye.position + Quaternion.AngleAxis(yaw, Vector3.up) * forward * spawnDistance;
                if (i > 0) Gizmos.DrawLine(previous, p);
                else Gizmos.DrawLine(eye.position, p);
                previous = p;
            }
            Gizmos.DrawLine(previous, eye.position);

            if (current != null)
            {
                Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.3f);
                Gizmos.DrawLine(eye.position, current.transform.position);
            }
        }
    }
}

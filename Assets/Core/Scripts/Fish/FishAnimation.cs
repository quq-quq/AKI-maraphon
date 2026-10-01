using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// Drives the fish model's animator (AC_Fish: Swim, and Struggle on the <see cref="HitTrigger"/> trigger).
    /// Swimming plays faster the faster the fish moves (<see cref="FishAI"/>). After a harpoon hit
    /// (<see cref="FishCollision.onHit"/>) the fish thrashes on the harpoon and its struggle slows down to a stop
    /// over <see cref="dieSeconds"/>, while <see cref="AKI.Weapons.Catchable"/> melts it away.
    /// </summary>
    [RequireComponent(typeof(FishAI))]
    public class FishAnimation : MonoBehaviour
    {
        public const string HitTrigger = "Hit";
        static readonly int HitId = Animator.StringToHash(HitTrigger);

        [Tooltip("The model's animator. Empty = the first one under the fish.")]
        public Animator animator;

        [Header("Swimming")]
        [Tooltip("Swim animation speed when the fish moves at its normal swim speed.")]
        [Min(0f)] public float swimPlayback = 1f;
        [Tooltip("Swim animation speed range (slowest, fastest), e.g. holding still vs coming in fast.")]
        public Vector2 swimPlaybackRange = new Vector2(0.5f, 2f);
        [Tooltip("How quickly the animation speed follows the fish's speed (higher = snappier).")]
        [Min(0.1f)] public float playbackSharpness = 4f;

        [Header("Dying")]
        [Tooltip("Struggle animation speed right after the hit.")]
        [Min(0f)] public float strugglePlayback = 1.4f;
        [Tooltip("Seconds until the struggle slows down to a stop.")]
        [Min(0.1f)] public float dieSeconds = 2.6f;
        [Tooltip("How the struggle dies down over Die Seconds (1 = full strength, 0 = still).")]
        public AnimationCurve dieDown = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.7f), new Keyframe(1f, 0f));

        FishAI ai;
        float playback;
        bool dying;
        float sinceHit;

        void Awake()
        {
            ai = GetComponent<FishAI>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (TryGetComponent(out FishCollision collision)) collision.onHit.AddListener(OnHit);
            playback = swimPlayback;
        }

        void OnHit(Vector3 point, Vector3 direction)
        {
            if (animator == null) return;
            if (!dying) animator.SetTrigger(HitId);   // a second arrow doesn't restart the struggle
            dying = true;
            sinceHit = 0f;
        }

        void Update()
        {
            if (animator == null) return;
            float dt = Time.deltaTime;

            if (dying)
            {
                sinceHit += dt;
                animator.speed = strugglePlayback * Mathf.Max(0f, dieDown.Evaluate(Mathf.Clamp01(sinceHit / dieSeconds)));
                return;
            }

            float relative = ai.swimSpeed > 0f ? ai.Velocity.magnitude / ai.swimSpeed : 1f;
            float target = Mathf.Clamp(swimPlayback * relative, swimPlaybackRange.x, swimPlaybackRange.y);
            playback = Mathf.Lerp(playback, target, 1f - Mathf.Exp(-playbackSharpness * dt));
            animator.speed = playback;
        }
    }
}

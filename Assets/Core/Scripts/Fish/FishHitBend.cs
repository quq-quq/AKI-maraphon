using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// Bends the fish's spine where the harpoon went in, mixed on top of whatever the animator plays (the struggle).
    /// The wound is pushed along the shot and the head and the tail swing back towards the shooter, so the body curves
    /// around the hit like a ")" seen with the shot coming from the left. Bones next to the wound bend the most.
    /// The bend punches in at the hit, rebounds a little and stays while the fish dies (<see cref="bendOverTime"/>).
    /// Applied in LateUpdate, after the animator has posed the bones, so it adds to the animation instead of replacing it.
    /// </summary>
    [RequireComponent(typeof(FishCollision))]
    public class FishHitBend : MonoBehaviour
    {
        [Tooltip("Spine bones from the middle of the body to the head, parent first. Empty = found by name (spine3, spine4).")]
        public Transform[] headChain;
        [Tooltip("Spine bones from the middle of the body to the tail, parent first. Empty = found by name (spine3.001, spine3.002).")]
        public Transform[] tailChain;

        [Tooltip("Largest bend per bone (degrees). Bones add up along the chain.")]
        [Range(0f, 45f)] public float maxBendDegrees = 18f;
        [Tooltip("Bend after the hit (seconds → share of the largest bend): a sharp punch, a small rebound, then it stays bent.")]
        public AnimationCurve bendOverTime = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.07f, 1f), new Keyframe(0.3f, 0.55f), new Keyframe(0.55f, 0.75f), new Keyframe(3f, 0.65f));
        [Range(0f, 1f), Tooltip("How much more the bones next to the wound bend than the far ones (0 = all the same).")]
        public float woundFocus = 0.6f;

        FishCollision collision;
        Animator animator;
        bool hit;
        float sinceHit;
        Vector3 localAway;   // where the head and the tail swing to, in the fish's space (across the body)
        float woundZ;        // where along the body the harpoon went in (fish space)
        float bodyLength = 1.4f;
        Transform[] bones;           // both chains, parents first
        Quaternion[] posed;          // each bone's pose before the bend (from the animator)
        Quaternion[] bent;           // each bone's pose as this script left it
        bool hasBent;

        void Awake()
        {
            collision = GetComponent<FishCollision>();
            collision.onHit.AddListener(OnHit);
            animator = GetComponentInChildren<Animator>();
            if (collision.body is CapsuleCollider capsule) bodyLength = Mathf.Max(0.1f, capsule.height);
            if (headChain == null || headChain.Length == 0) headChain = FindBones("spine3", "spine4");
            if (tailChain == null || tailChain.Length == 0) tailChain = FindBones("spine3.001", "spine3.002");

            bones = new Transform[headChain.Length + tailChain.Length];
            headChain.CopyTo(bones, 0);
            tailChain.CopyTo(bones, headChain.Length);
            posed = new Quaternion[bones.Length];
            bent = new Quaternion[bones.Length];
        }

        void OnHit(Vector3 point, Vector3 direction)
        {
            // only the part of the shot across the body bends it (a shot along the fish doesn't)
            Vector3 across = transform.InverseTransformDirection(direction);
            across.z = 0f;
            if (across.sqrMagnitude < 1e-4f) return;

            // a second harpoon bends it towards the mix of both shots and punches again
            localAway = hit ? (localAway - across.normalized).normalized : -across.normalized;
            if (localAway.sqrMagnitude < 1e-4f) localAway = -across.normalized;
            woundZ = transform.InverseTransformPoint(point).z;
            sinceHit = 0f;
            hit = true;

            // the bend is added on top of the animated pose every frame: the pose must be written every frame too
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        void LateUpdate()
        {
            if (!hit) return;
            sinceHit += Time.deltaTime;

            // Start from the animated pose. A bone the animation doesn't key is not written by the animator, so it
            // still holds last frame's bend: put it back first, or the bend would pile up frame after frame.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                if (hasBent && bones[i].localRotation == bent[i]) bones[i].localRotation = posed[i];
                else posed[i] = bones[i].localRotation;
            }

            float degrees = bendOverTime.Evaluate(sinceHit) * maxBendDegrees;
            Vector3 away = transform.TransformDirection(localAway);
            Bend(headChain, transform.forward, away, degrees);
            Bend(tailChain, -transform.forward, away, degrees);

            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bent[i] = bones[i].localRotation;
            hasBent = true;
        }

        // Turns each bone of the chain (parent first, so the turns add up into a curve) from pointing along the body
        // towards 'away', most where it is near the wound.
        void Bend(Transform[] chain, Vector3 along, Vector3 away, float degrees)
        {
            if (chain == null) return;
            Quaternion fullTurn = Quaternion.FromToRotation(along, away);   // a 90° turn across the body
            foreach (Transform bone in chain)
            {
                if (bone == null) continue;
                float z = transform.InverseTransformPoint(bone.position).z;
                float near = 1f - Mathf.Clamp01(Mathf.Abs(z - woundZ) / bodyLength);
                float weight = Mathf.Lerp(1f - woundFocus, 1f, near);
                bone.rotation = Quaternion.RotateTowards(Quaternion.identity, fullTurn, degrees * weight) * bone.rotation;
            }
        }

        Transform[] FindBones(params string[] names)
        {
            var found = new Transform[names.Length];
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < names.Length; i++)
                foreach (Transform t in all)
                    if (t.name == names[i])
                    {
                        found[i] = t;
                        break;
                    }
            return found;
        }
    }
}

using AKI.Weapons;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AKI.Player
{
    /// <summary>
    /// The swimmer's own body in first person. The rigged swimmer plays its swim clip under the camera (its head hidden,
    /// leaning into the stroke when swimming fast), and two arms are posed on top every frame:
    ///  - the right hand holds the speargun by its grip, wherever <see cref="PlayerSpeargun"/> puts the gun: the elbow
    ///    bends and the wrist turns as the gun comes up to the eye to aim,
    ///  - the left arm strokes while swimming (reach, glide, catch, pull, recover) at the pace of the swim and treads
    ///    water in small circles when holding still; while aiming (or out of the water) it hangs down by the side.
    /// Runs after the gun and the camera have been placed this frame, so the hand never lags behind the grip.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(FirstPersonSwimController))]
    public class PlayerBody : MonoBehaviour
    {
        [Tooltip("The rigged (humanoid) swimmer, a child of the player.")]
        public Animator body;
        [Tooltip("Played on the body (looping).")]
        public AnimationClip swimClip;
        [Tooltip("Where the head sits relative to the eye (camera space, m): a little back and down, so the camera " +
                 "never looks into the neck.")]
        public Vector3 headOffset = new Vector3(0f, -0.08f, -0.04f);
        [Tooltip("How far the body leans forward into the swim at full speed (degrees).")]
        [Range(0f, 80f)] public float swimLean = 30f;
        [Tooltip("Swimming speed (m/s) counted as full speed for the lean and the stroke rate.")]
        [Min(0.1f)] public float fullSpeed = 3f;

        [Header("Right hand on the speargun")]
        public PlayerSpeargun speargun;
        [Tooltip("Middle of the palm on the grip, in the gun's own space (m, before its scale).")]
        public Vector3 gripPoint = new Vector3(0f, -0.055f, -0.585f);
        [Tooltip("Where the knuckles point along the grip, in the gun's space.")]
        public Vector3 gripPointing = new Vector3(0f, -0.45f, 1f);
        [Tooltip("Where the right elbow drops to, from the shoulder in body space: out to the side and down.")]
        public Vector3 rightElbowHint = new Vector3(0.7f, -1f, -0.3f);

        [Header("Left arm")]
        [Tooltip("The left hand's stroke loop in camera space (m): reach forward into view, glide, sweep out to the side, " +
                 "pull down and back under the chest, recover under the chin.")]
        public Vector3[] strokePath =
        {
            new Vector3(-0.08f, -0.11f, 0.31f),
            new Vector3(-0.16f, -0.12f, 0.31f),
            new Vector3(-0.36f, -0.16f, 0.24f),
            new Vector3(-0.32f, -0.44f, 0.02f),
            new Vector3(-0.14f, -0.3f, 0.12f)
        };
        [Tooltip("Where the left hand hangs when it isn't stroking (aiming, out of the water), in camera space (m).")]
        public Vector3 restHand = new Vector3(-0.26f, -0.62f, -0.06f);
        [Tooltip("Where the left elbow points, from the shoulder in camera space.")]
        public Vector3 leftElbowHint = new Vector3(-1f, -0.6f, -0.3f);
        [Tooltip("Strokes per second at full speed, and treading water.")]
        public Vector2 strokeRate = new Vector2(0.9f, 0.35f);
        [Tooltip("Treading water: size of the circles as a share of the full stroke.")]
        [Range(0f, 1f)] public float treadSize = 0.3f;

        [Header("Hand bones")]
        [Tooltip("The hand bone's own axis running out along the fingers (Blender rigs: +Y).")]
        public Vector3 handPointAxis = Vector3.up;
        [Tooltip("The right hand bone's own axis the palm faces (the side the fingers curl to).")]
        public Vector3 handPalmAxis = Vector3.right;
        [Tooltip("The same for the left hand: the rig is mirrored, so it is the other way round.")]
        public Vector3 leftHandPalmAxis = Vector3.left;

        const float PalmLength = 0.08f;   // wrist to the middle of the palm (m)
        const float PalmDepth = 0.035f;   // middle of the hand to the palm's surface (m)

        FirstPersonSwimController swimmer;
        Transform cam;
        PlayableGraph graph;
        AnimationClipPlayable clipPlayable;
        Transform head, upperR, lowerR, handR, upperL, lowerL, handL;
        float lean, speed01, phase, swimWeight, aimWeight;

        void Start()
        {
            swimmer = GetComponent<FirstPersonSwimController>();
            cam = swimmer.playerCamera != null ? swimmer.playerCamera.transform : GetComponentInChildren<Camera>(true).transform;
            if (speargun == null) speargun = GetComponent<PlayerSpeargun>();
            if (body == null || !body.isHuman) { enabled = false; return; }

            head = body.GetBoneTransform(HumanBodyBones.Head);
            upperR = body.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerR = body.GetBoneTransform(HumanBodyBones.RightLowerArm);
            handR = body.GetBoneTransform(HumanBodyBones.RightHand);
            upperL = body.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lowerL = body.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            handL = body.GetBoneTransform(HumanBodyBones.LeftHand);

            // the camera is inside the head: hide it
            head.localScale = Vector3.zero;

            body.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            body.applyRootMotion = false;
            if (swimClip != null)
            {
                graph = PlayableGraph.Create("Player Body");
                graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                clipPlayable = AnimationClipPlayable.Create(graph, swimClip);
                var output = AnimationPlayableOutput.Create(graph, "Body", body);
                output.SetSourcePlayable(clipPlayable);
                graph.Play();
            }
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool swimming = swimmer.IsSwimming;
            float speed = swimming ? swimmer.Velocity.magnitude : 0f;
            speed01 = Mathf.MoveTowards(speed01, Mathf.Clamp01(speed / fullSpeed), dt * 2f);
            swimWeight = Mathf.MoveTowards(swimWeight, swimming ? 1f : 0f, dt * 3f);
            // aiming: the free hand stops stroking and drops out of the line of sight
            aimWeight = Mathf.MoveTowards(aimWeight, speargun != null && speargun.IsAiming ? 1f : 0f, dt * 4f);
            if (clipPlayable.IsValid()) clipPlayable.SetSpeed(Mathf.Lerp(0.6f, 1.4f, speed01));

            // the body hangs under the eye, turned with the view and leaning into the swim
            float yaw = cam.eulerAngles.y;
            float wantLean = swimmer.State == FirstPersonSwimController.MoveState.Underwater
                ? Mathf.Clamp(cam.eulerAngles.x > 180f ? cam.eulerAngles.x - 360f : cam.eulerAngles.x, -60f, 70f) * speed01 + swimLean * speed01
                : swimLean * 0.5f * speed01;
            lean = Mathf.Lerp(lean, Mathf.Clamp(wantLean, -30f, 85f), 1f - Mathf.Exp(-dt * 4f));
            body.transform.rotation = Quaternion.Euler(lean, yaw, 0f);
            body.transform.position += cam.TransformPoint(headOffset) - head.position;

            HoldGun();
            Stroke(dt);
        }

        // ------------------------------------------------------------------ right hand

        void HoldGun()
        {
            Speargun gun = speargun != null ? speargun.gun : null;
            if (gun == null || !gun.isActiveAndEnabled) return;
            Transform g = gun.transform;

            Vector3 grip = g.TransformPoint(gripPoint);
            Vector3 point = (g.rotation * gripPointing).normalized;
            Vector3 palm = -g.right;                                   // the palm wraps the grip from the right
            Vector3 wrist = grip - point * PalmLength - palm * PalmDepth;

            Vector3 hint = upperR.position + body.transform.rotation * rightElbowHint;
            SolveArm(upperR, lowerR, handR, wrist, hint);
            handR.rotation = HandRotation(point, palm, handPalmAxis);
        }

        // ------------------------------------------------------------------ left arm

        void Stroke(float dt)
        {
            if (strokePath == null || strokePath.Length < 3) return;
            // stroking while swimming; out of the water or aiming the arm hangs down by the side, out of view
            float weight = swimWeight * (1f - aimWeight);

            float rate = Mathf.Lerp(strokeRate.y, strokeRate.x, speed01);
            phase = Mathf.Repeat(phase + dt * rate, 1f);
            float size = Mathf.Lerp(treadSize, 1f, speed01);

            // the loop, shrunk towards its middle when treading water
            Vector3 centre = Vector3.zero;
            foreach (var p in strokePath) centre += p;
            centre /= strokePath.Length;
            Vector3 local = centre + (LoopPoint(phase) - centre) * size;
            Vector3 ahead = centre + (LoopPoint(Mathf.Repeat(phase + 0.02f, 1f)) - centre) * size;

            // the loop is laid out in view space, so the reach comes into sight however the body leans
            Quaternion bodyRot = cam.rotation;
            Vector3 shoulder = upperL.position;
            Vector3 target = Vector3.Lerp(cam.TransformPoint(restHand), cam.TransformPoint(local), weight);

            SolveArm(upperL, lowerL, handL, target, shoulder + bodyRot * leftElbowHint);

            // fingers lead along the stroke; the palm pushes back during the pull, faces down the rest of the way
            Vector3 motion = bodyRot * (ahead - local);
            Vector3 point = motion.sqrMagnitude > 1e-8f ? Vector3.Lerp(bodyRot * Vector3.forward, motion.normalized, 0.5f).normalized : bodyRot * Vector3.forward;
            float pull = Mathf.Clamp01(Vector3.Dot(motion.normalized, bodyRot * Vector3.back) * 1.5f);
            Vector3 palm = Vector3.Slerp(bodyRot * Vector3.down, bodyRot * Vector3.back, pull);
            Quaternion stroke = HandRotation(point, palm, leftHandPalmAxis);
            // at rest: fingers down along the leg, palm to the thigh
            Quaternion rest = HandRotation(bodyRot * new Vector3(0f, -1f, 0.15f), bodyRot * Vector3.right, leftHandPalmAxis);
            handL.rotation = Quaternion.Slerp(rest, stroke, weight);
        }

        // closed Catmull-Rom loop through the stroke points (t in 0..1)
        Vector3 LoopPoint(float t)
        {
            int n = strokePath.Length;
            float f = t * n;
            int i = Mathf.FloorToInt(f);
            float u = f - i;
            Vector3 p0 = strokePath[(i - 1 + n) % n], p1 = strokePath[i % n], p2 = strokePath[(i + 1) % n], p3 = strokePath[(i + 2) % n];
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        }

        // ------------------------------------------------------------------ helpers

        // The hand bone's rotation that points the fingers along `point` with the palm facing `palm` (world space).
        Quaternion HandRotation(Vector3 point, Vector3 palm, Vector3 palmAxis) =>
            Quaternion.LookRotation(point, palm) * Quaternion.Inverse(Quaternion.LookRotation(handPointAxis, palmAxis));

        // Two-bone IK: the hand on the target, the elbow bending towards the hint.
        static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position;
            float upperLength = (lower.position - a).magnitude;
            float lowerLength = (hand.position - lower.position).magnitude;
            Vector3 toTarget = target - a;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 1e-3f, upperLength + lowerLength - 1e-3f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;

            Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(lower.position - a, dir);
            bend.Normalize();
            float cos = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength) / (2f * upperLength * reach), -1f, 1f);
            Vector3 elbow = a + dir * (upperLength * cos) + bend * (upperLength * Mathf.Sqrt(1f - cos * cos));

            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            Vector3 end = a + dir * reach;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, end - lower.position) * lower.rotation;
        }
    }
}

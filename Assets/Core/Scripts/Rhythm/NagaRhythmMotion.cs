using System.Collections.Generic;
using UnityEngine;

namespace AKI.Rhythm
{
    /// <summary>
    /// Head-led swimming for the Naga. The head steers like a swimmer with a minimum turning circle and never stops;
    /// every spine joint is then placed on the path the head has already drawn (follow-the-leader), so a turn is a
    /// loop of the whole body instead of a rigid stick pivoting about the head. The encounter controller owns all
    /// timing and hits. Bones are posed every frame; do not animate the same bones with an Animator concurrently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NagaRhythmMotion : MonoBehaviour
    {
        private enum MotionState { Idle, Orbit, Charge, Arrived }

        [Header("Head and model axes")]
        [SerializeField] private Transform headBone;
        [SerializeField] private Transform jawBone;
        [Tooltip("Upper mouth branch from the same head joint; complements the lower jaw.")]
        [SerializeField] private Transform upperJawBone;
        [Tooltip("Offset in head-bone coordinates, e.g. the centre of the mouth. With no head bone, uses root coordinates.")]
        [SerializeField] private Vector3 headPointLocalOffset;
        [SerializeField] private Vector3 modelForwardAxis = Vector3.forward;
        [SerializeField] private Vector3 modelUpAxis = Vector3.up;

        [Header("Orbit around the moving player")]
        [Min(2f)] [SerializeField] private float orbitRadius = 16f;
        [Tooltip("Degrees per second around the player at the orbit radius: sets the swimming speed. The sign is only the preferred first direction.")]
        [SerializeField] private float angularSpeed = 27f;
        [Tooltip("Slow irregular change of swimming speed, as a share of it. The Naga never slows below the rest.")]
        [Range(0f, .4f)] [SerializeField] private float speedVariation = .12f;
        [SerializeField] private float heightOffset = 0.2f;
        [Min(0f)] [SerializeField] private float verticalAmplitude = 1.5f;
        [Min(0f)] [SerializeField] private float verticalFrequency = 0.22f;
        [Min(0f)] [SerializeField] private float radialAmplitude = 1.1f;
        [Min(0f)] [SerializeField] private float radialFrequency = 0.15f;
        [Tooltip("How far ahead along the circle the head aims, in radians of the orbit. Lower tracks the circle tighter.")]
        [Range(.2f, 1f)] [SerializeField] private float lookAhead = .7f;

        [Header("Turning")]
        [Tooltip("Tightest turning circle of the head, as a share of the orbit radius. U-turns are loops of this radius, always made on the side away from the player.")]
        [Range(.15f, .9f)] [SerializeField] private float turnRadiusFraction = .4f;
        [SerializeField] private Vector2 reverseEverySeconds = new Vector2(7f, 13f);
        [Tooltip("Height change (m) made with each U-turn, alternately up and down, so the returning head passes over or under its own body.")]
        [SerializeField] private Vector2 uTurnHeightShift = new Vector2(2.5f, 5f);
        [Tooltip("Steepest climb or dive of the head. The body follows the head's path, so this does not tilt the whole serpent.")]
        [Range(0f, 45f)] [SerializeField] private float maximumPitchDegrees = 25f;

        [Header("Swimming")]
        [Tooltip("Side-to-side swing of the head; the body follows it as a travelling wave.")]
        [Range(0f, 20f)] [SerializeField] private float swimWaveDegrees = 6f;
        [Min(0f)] [SerializeField] private float swimWaveFrequency = .45f;
        [Tooltip("Metres between recorded points of the head's path.")]
        [Min(.05f)] [SerializeField] private float trailSpacing = .3f;
        [Tooltip("Slow, small mouth movement while swimming, around the verified jaw axis.")]
        [Range(0f,25f)] [SerializeField] private float swimJawDegrees = 14f;
        [Min(0f)] [SerializeField] private float swimJawFrequency = .18f;
        [Range(0f,.5f)] [SerializeField] private float upperJawMotionShare = .25f;

        [Header("Final charge")]
        [Min(0.1f)] [SerializeField] private float chargeDuration = 1.05f;
        [Tooltip("Mouth stops this far in front of the target head, rather than putting the root pivot inside the player.")]
        [Min(0f)] [SerializeField] private float chargeStopDistance = 0.2f;
        [SerializeField] private Vector3 jawLocalAxis = Vector3.right;
        [Range(-90f, 90f)] [SerializeField] private float jawOpenDegrees = 38f;
        [Tooltip("Raise the head slightly for the final bite; keep the mouth anchored on its approach path.")]
        [Range(0f,20f)] [SerializeField] private float chargeHeadLiftDegrees = 9f;

        private struct SpineBone
        {
            public Transform bone;
            public int joint;                 // joint the bone sits on
            public int target;                // neighbouring joint its rest segment points at
            public Quaternion frameToBone;    // segment frame -> bone rotation, from the rest pose
        }

        private struct RestPose
        {
            public Transform bone;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private MotionState state;
        private Transform orbitTarget;
        private Transform chargeTarget;
        private bool initialized;
        private NagaEnvironmentSafety safety;
        private SkinnedMeshRenderer[] renderers = new SkinnedMeshRenderer[0];
        private Bounds[] rendererBounds = new Bounds[0];
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private Quaternion jawRestRotation;
        private Quaternion upperJawRestRotation;

        // Spine, ordered mouth (0) -> tail tip. Root-space rest joints; world lengths between them.
        private RestPose[] restPoses = new RestPose[0];
        private SpineBone[] spine = new SpineBone[0];
        private Vector3[] restJoints = new Vector3[0];
        private float[] segmentLengths = new float[0];
        private Vector3[] joints = new Vector3[0];
        private int rootJoint;
        private Quaternion rootFrameToRoot = Quaternion.identity;
        private float bodyLength;

        // Path of the head, newest point first (ring buffer).
        private Vector3[] trail = new Vector3[0];
        private int trailHead, trailCount;
        private bool needsSeed;
        private Vector3 headPosition, heading = Vector3.forward, moveDirection = Vector3.forward;

        private float motionTime, chargeElapsed, hitPulse, irregularPhase;
        private float dirSign = 1f, turnSide, turnElapsed, nextReverse, avoidCooldown;
        private float laneHeight, laneBoost, laneRelaxAt;
        // Signed degrees per second around the orbit centre; read by the editor QA sampler.
        private float currentAngularSpeed;
        private Vector3 orbitCentre, centreVelocity;
        private Vector3 chargeStartPosition, chargeInitialDirection, chargeApproachDirection, lastChargeTargetPosition;

        public float ChargeProgress { get; private set; }
        public bool HasReachedHead => state == MotionState.Arrived;
        public bool IsOrbiting => state == MotionState.Orbit;
        public bool IsCharging => state == MotionState.Charge;
        public bool HasJawBone => jawBone != null;
        public Transform HeadBone => headBone;
        public Transform JawBone => jawBone;
        public Vector3 HeadWorldPosition => (headBone != null ? headBone : transform).TransformPoint(headPointLocalOffset);
        public float OrbitRadius { get => orbitRadius; set => orbitRadius = Mathf.Max(2f, value); }
        public float AngularSpeed { get => angularSpeed; set => angularSpeed = value; }
        public float ChargeDuration { get => chargeDuration; set => chargeDuration = Mathf.Max(0.1f, value); }

        private void Awake() { EnsureInitialized(); safety = GetComponent<NagaEnvironmentSafety>(); }

        private void OnEnable()
        {
            EnsureInitialized();
            ExpandRendererBounds();
        }

        private void OnDisable()
        {
            StopMotion();
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].localBounds = rendererBounds[i];
        }

        /// <summary>Explicit references take priority over rig discovery. Call after sizing the model.</summary>
        public void ConfigureRig(Transform head, Transform jaw = null)
        {
            StopMotion();
            headBone = head;
            jawBone = jaw;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].localBounds = rendererBounds[i];
            initialized = false;
            EnsureInitialized();
        }

        public void SetMouthOffset(Vector3 offset) => headPointLocalOffset = offset;

        public void BeginOrbit(Transform player)
        {
            EnsureInitialized();
            if (player == null) { StopMotion(); return; }
            RestoreBonePose();
            BuildSpine();
            orbitTarget = player;
            chargeTarget = null;
            orbitCentre = safety != null ? safety.SafeOrbitCentre(player.position, CentreFootprint, MaxCentreShift) : player.position;
            centreVelocity = Vector3.zero;
            motionTime = 0f;
            chargeElapsed = 0f;
            ChargeProgress = 0f;
            hitPulse = 0f;
            turnSide = 0f;
            laneHeight = laneBoost = 0f;
            nextReverse = Random.Range(reverseEverySeconds.x, reverseEverySeconds.y);
            irregularPhase = Random.Range(0f, 6.28f);
            avoidCooldown = 0f;
            // The encounter may still move the model (spawn point, whole-body clearance) before the first frame.
            needsSeed = true;
            state = MotionState.Orbit;
        }

        public void BeginCharge(Transform playerHead)
        {
            EnsureInitialized();
            if (playerHead == null) { StopMotion(); return; }
            if (needsSeed) SeedTrail();
            chargeStartPosition = headPosition;
            chargeInitialDirection = moveDirection;
            chargeTarget = playerHead;
            lastChargeTargetPosition = playerHead.position;
            chargeApproachDirection = SafeDirection(lastChargeTargetPosition - chargeStartPosition, moveDirection);
            chargeElapsed = 0f;
            ChargeProgress = 0f;
            state = MotionState.Charge;
        }

        /// <summary>A brief local swim ripple; scoring, damage, audio and VFX belong to the controller.</summary>
        public void PlayHitReaction(float strength = 1f) => hitPulse = Mathf.Max(hitPulse, Mathf.Clamp01(strength));
        /// <summary>1 right after a hit, fading to 0 (PlayHitReaction).</summary>
        public float HitPulse => hitPulse;

        public void StopMotion(bool restorePose = true)
        {
            state = MotionState.Idle;
            orbitTarget = null;
            chargeTarget = null;
            ChargeProgress = 0f;
            hitPulse = 0f;
            if (restorePose && initialized) RestoreBonePose();
        }

        public void ResetMotion(bool restoreTransform = true)
        {
            StopMotion();
            if (!initialized || !restoreTransform) return;
            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            transform.localScale = originalLocalScale;
        }

        private float CentreFootprint => orbitRadius + radialAmplitude + 4f;
        // Small enough that the player always stays well inside the loop.
        private float MaxCentreShift => orbitRadius * .35f;
        private float Scale => Mathf.Abs(transform.lossyScale.x);

        private void LateUpdate()
        {
            if (state == MotionState.Idle || state == MotionState.Arrived) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (needsSeed) SeedTrail();
            motionTime += dt;
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 3.5f);

            if (state == MotionState.Orbit)
            {
                if (orbitTarget == null) { StopMotion(); return; }
                UpdateOrbit(dt);
            }
            else UpdateCharge(dt);
        }

        private void UpdateOrbit(float dt)
        {
            Vector3 player = orbitTarget.position;
            Vector3 centreGoal = safety != null ? safety.SafeOrbitCentre(player, CentreFootprint, MaxCentreShift) : player;
            orbitCentre = Vector3.SmoothDamp(orbitCentre, centreGoal, ref centreVelocity, 1.2f, Mathf.Infinity, dt);

            float baseSpeed = Mathf.Max(1f, Mathf.Abs(angularSpeed) * Mathf.Deg2Rad * orbitRadius);
            float speed = baseSpeed * (1f + speedVariation * Mathf.Sin(motionTime * 1.9f + irregularPhase * 1.7f));
            float turnRadius = Mathf.Max(1f, orbitRadius * turnRadiusFraction);
            float maxTurnDegrees = speed / turnRadius * Mathf.Rad2Deg;

            Vector3 flat = Flat(heading, Flat(moveDirection, Vector3.forward));
            Vector3 rel = headPosition - orbitCentre; rel.y = 0f;
            float distance = rel.magnitude;
            Vector3 radial = distance > .01f ? rel / distance : Vector3.Cross(Vector3.up, flat);

            if (motionTime >= nextReverse) RequestReverse(flat, radial, turnRadius, speed);
            if (motionTime >= laneRelaxAt) laneBoost = Mathf.MoveTowards(laneBoost, 0f, turnRadius * .6f * dt);

            // Aim at a point a little ahead: a pursuit curve that settles onto the (breathing) circle. Chasing a point
            // lookAhead radians ahead on radius R/cos(lookAhead) makes the steady path exactly radius R.
            float targetRadius = orbitRadius + radialAmplitude * Mathf.Sin(motionTime * radialFrequency * Mathf.PI * 2f) + laneBoost;
            float carrotAngle = Mathf.Atan2(radial.z, radial.x) + dirSign * lookAhead;
            Vector3 carrot = orbitCentre + new Vector3(Mathf.Cos(carrotAngle), 0f, Mathf.Sin(carrotAngle)) * (targetRadius / Mathf.Cos(lookAhead));
            Vector3 desiredFlat = Flat(carrot - headPosition, flat);

            // Irregular rises and dives around the player's depth, plus the lane chosen at the last U-turn.
            float vp = motionTime * verticalFrequency * Mathf.PI * 2f + irregularPhase;
            float targetY = player.y + heightOffset + laneHeight
                + verticalAmplitude * (Mathf.Sin(vp) + .45f * Mathf.Sin(vp * 1.93f + .8f) + .25f * Mathf.Sin(vp * 3.17f + 2.4f));
            float minHere = float.NegativeInfinity, maxHere = float.PositiveInfinity;
            if (safety != null)
            {
                // Look along the current heading for reefs: rise over them early, or turn away if there is no room.
                float clearance = safety.BodyBelowSpine + safety.seabedClearance;
                maxHere = safety.CeilingAt(headPosition) - safety.BodyAboveSpine;
                minHere = safety.FloorBelow(headPosition, safety.BodyHalfWidth) + clearance;
                float minAhead = minHere, look = Mathf.Max(6f, speed * 1.5f);
                for (int i = 1; i <= 3; i++)
                    minAhead = Mathf.Max(minAhead, safety.FloorBelow(headPosition + flat * (look * i / 3f), safety.BodyHalfWidth) + clearance);
                if (minAhead + .5f <= maxHere - .3f) targetY = Mathf.Clamp(targetY, minAhead + .5f, maxHere - .3f);
                else
                {
                    targetY = maxHere - .3f;
                    if (turnSide == 0f && motionTime >= avoidCooldown) RequestReverse(flat, radial, turnRadius, speed);
                }
            }

            // Yaw: limited turn rate, so every change of direction is an arc of at least the turning radius.
            float flatAngle = Vector3.SignedAngle(flat, desiredFlat, Vector3.up);
            if (turnSide == 0f && Mathf.Abs(flatAngle) > 100f) BeginUTurn(flat, radial, flatAngle);
            float yawStep = maxTurnDegrees * dt, yaw;
            if (turnSide != 0f)
            {
                turnElapsed += dt;
                // Committed loop away from the player until roughly lined up with the new direction.
                if (Mathf.Abs(flatAngle) < 35f || turnElapsed * maxTurnDegrees > 300f) { turnSide = 0f; yaw = Mathf.Clamp(flatAngle, -yawStep, yawStep); }
                else yaw = turnSide * yawStep;
            }
            else yaw = Mathf.Clamp(flatAngle, -yawStep, yawStep);

            float horizontalLook = Mathf.Max(4f, speed * 1.2f);
            float targetPitch = Mathf.Clamp(Mathf.Atan2(targetY - headPosition.y, horizontalLook) * Mathf.Rad2Deg, -maximumPitchDegrees, maximumPitchDegrees);
            float pitch = Mathf.MoveTowards(Mathf.Asin(Mathf.Clamp(heading.y, -1f, 1f)) * Mathf.Rad2Deg, targetPitch, maxTurnDegrees * .6f * dt);
            flat = Quaternion.AngleAxis(yaw, Vector3.up) * flat;
            heading = (flat * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;

            float wave = swimWaveDegrees * (1f + 1.5f * hitPulse) * Mathf.Sin(motionTime * swimWaveFrequency * Mathf.PI * 2f);
            moveDirection = Quaternion.AngleAxis(wave, Vector3.up) * heading;
            headPosition += moveDirection * (speed * dt);
            if (minHere <= maxHere) headPosition.y = Mathf.Clamp(headPosition.y, minHere, maxHere);
            else if (headPosition.y > maxHere) headPosition.y = maxHere;

            Vector3 velocity = moveDirection * speed;
            currentAngularSpeed = (rel.x * velocity.z - rel.z * velocity.x) / Mathf.Max(1f, distance * distance) * Mathf.Rad2Deg;

            RecordTrail();
            PoseFromTrail();
            if (jawBone != null)
            {
                float open = .5f - .5f * Mathf.Cos(motionTime * swimJawFrequency * Mathf.PI * 2f);
                jawBone.localRotation = jawRestRotation * Quaternion.AngleAxis(Mathf.Sign(jawOpenDegrees) * swimJawDegrees * open, SafeDirection(jawLocalAxis, Vector3.right));
                if (upperJawBone != null)
                    upperJawBone.localRotation = upperJawRestRotation * Quaternion.AngleAxis(-Mathf.Sign(jawOpenDegrees) * swimJawDegrees * upperJawMotionShare * open,SafeDirection(jawLocalAxis,Vector3.right));
            }
            if (safety != null)
            {
                // Whole-skin clearance: shift the serpent and its path together, so it keeps swimming.
                bool fits = safety.SolveLift(out float lift);
                if (lift != 0f) ShiftPath(lift);
                if (!fits && turnSide == 0f && motionTime >= avoidCooldown) RequestReverse(flat, radial, turnRadius, speed);
            }
        }

        private void RequestReverse(Vector3 flat, Vector3 radial, float turnRadius, float speed)
        {
            dirSign = -dirSign;
            nextReverse = motionTime + Random.Range(reverseEverySeconds.x, reverseEverySeconds.y);
            avoidCooldown = motionTime + 4f;
            bool goDown = laneHeight > 0f || (laneHeight == 0f && Random.value < .5f);
            laneHeight = (goDown ? -1f : 1f) * Random.Range(uTurnHeightShift.x, uTurnHeightShift.y);
            // The loop puts the returning head two turning radii outside the body; stay in that outer lane until
            // the tail has come round, then drift back to the orbit.
            laneBoost = 2f * turnRadius;
            laneRelaxAt = motionTime + bodyLength / Mathf.Max(1f, speed);
            BeginUTurn(flat, radial, 180f);
        }

        private void BeginUTurn(Vector3 flat, Vector3 radial, float fallbackAngle)
        {
            // Rotate through the outward radial: the loop bulges away from the player, never through them.
            float side = Vector3.Cross(flat, radial).y;
            turnSide = Mathf.Abs(side) > .05f ? Mathf.Sign(side) : Mathf.Sign(fallbackAngle);
            turnElapsed = 0f;
        }

        private void UpdateCharge(float dt)
        {
            if (chargeTarget != null) lastChargeTargetPosition = chargeTarget.position;
            chargeElapsed += dt;
            ChargeProgress = Mathf.Clamp01(chargeElapsed / chargeDuration);
            Vector3 approach = SafeDirection(lastChargeTargetPosition - chargeStartPosition, chargeApproachDirection);
            Vector3 end = lastChargeTargetPosition - approach * chargeStopDistance;
            // Increasing speed makes the short finale read as a lunge; a Hermite curve leaves the orbit tangentially,
            // the body follows that curve, and the mouth still ends at an exact anchor in front of the camera.
            float u = ChargeProgress * ChargeProgress;
            float reach = Vector3.Distance(chargeStartPosition, end);
            Vector3 t0 = chargeInitialDirection * (reach * .8f), t1 = approach * reach;
            float u2 = u * u, u3 = u2 * u;
            Vector3 position = (2f * u3 - 3f * u2 + 1f) * chargeStartPosition + (u3 - 2f * u2 + u) * t0
                + (-2f * u3 + 3f * u2) * end + (u3 - u2) * t1;
            Vector3 tangent = (6f * u2 - 6f * u) * chargeStartPosition + (3f * u2 - 4f * u + 1f) * t0
                + (-6f * u2 + 6f * u) * end + (3f * u2 - 2f * u) * t1;
            headPosition = position;
            heading = moveDirection = SafeDirection(tangent, heading);
            RecordTrail();
            PoseFromTrail();
            if (headBone != null)
            {
                Vector3 mouthAnchor = HeadWorldPosition;
                Vector3 right = SafeDirection(Vector3.Cross(Vector3.up, moveDirection), transform.right);
                headBone.rotation = Quaternion.AngleAxis(-chargeHeadLiftDegrees * Mathf.SmoothStep(0f,1f,ChargeProgress), right) * headBone.rotation;
                transform.position += mouthAnchor - HeadWorldPosition;
            }
            if (jawBone != null)
                jawBone.localRotation = jawRestRotation * Quaternion.AngleAxis(jawOpenDegrees * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ChargeProgress * 2.5f)), SafeDirection(jawLocalAxis, Vector3.right));
            if (upperJawBone != null)
                upperJawBone.localRotation = upperJawRestRotation * Quaternion.AngleAxis(-jawOpenDegrees * upperJawMotionShare * Mathf.SmoothStep(0f,1f,Mathf.Clamp01(ChargeProgress*2.5f)),SafeDirection(jawLocalAxis,Vector3.right));
            if (safety != null)
            {
                float lift;
                safety.SolveLift(out lift);
                if (lift != 0f) ShiftPath(lift);
            }
            if (ChargeProgress >= 1f) state = MotionState.Arrived;
        }

        // ---- Path following -------------------------------------------------------------------------------------

        private void SeedTrail()
        {
            needsSeed = false;
            int n = restJoints.Length;
            if (n < 2)
            {
                headPosition = HeadWorldPosition;
                heading = moveDirection = transform.TransformDirection(modelForwardAxis).normalized;
                return;
            }
            // The current (rest, straight) body is the path the head has just swum.
            int capacity = Mathf.CeilToInt((bodyLength + 24f) / Mathf.Max(.05f, trailSpacing)) + n + 8;
            if (trail.Length != capacity) trail = new Vector3[capacity];
            trailHead = -1; trailCount = 0;
            Vector3 tip = transform.TransformPoint(restJoints[n - 1]);
            Vector3 back = SafeDirection(tip - transform.TransformPoint(restJoints[n - 2]), -transform.TransformDirection(modelForwardAxis));
            PushTrail(tip + back * 20f);
            for (int k = n - 1; k >= 0; k--) PushTrail(transform.TransformPoint(restJoints[k]));
            headPosition = transform.TransformPoint(restJoints[0]);
            heading = moveDirection = SafeDirection(headPosition - transform.TransformPoint(restJoints[1]), transform.TransformDirection(modelForwardAxis));
            if (orbitTarget != null)
            {
                Vector3 rel = headPosition - orbitCentre;
                float side = rel.x * heading.z - rel.z * heading.x;
                dirSign = Mathf.Abs(side) > .01f ? Mathf.Sign(side) : (angularSpeed < 0f ? -1f : 1f);
            }
        }

        private void PushTrail(Vector3 p)
        {
            if (trail.Length == 0) return;
            trailHead = (trailHead + 1) % trail.Length;
            trail[trailHead] = p;
            if (trailCount < trail.Length) trailCount++;
        }

        private Vector3 TrailPoint(int i) => trail[(trailHead - i + trail.Length * 2) % trail.Length];

        private void RecordTrail()
        {
            if (trailCount == 0 || (headPosition - TrailPoint(0)).sqrMagnitude >= trailSpacing * trailSpacing) PushTrail(headPosition);
        }

        private void ShiftPath(float lift)
        {
            headPosition.y += lift;
            for (int i = 0; i < trail.Length; i++) trail[i].y += lift;
            if (joints.Length > 0) for (int i = 0; i < joints.Length; i++) joints[i].y += lift;
        }

        private void PoseFromTrail()
        {
            int n = restJoints.Length;
            if (n < 2 || spine.Length == 0)
            {
                // No recognised spine: move the model rigidly, head first.
                Quaternion frame = Quaternion.LookRotation(SafeDirection(modelForwardAxis, Vector3.forward), SafeUp(modelForwardAxis, modelUpAxis));
                transform.rotation = Quaternion.LookRotation(moveDirection, SafeUp(moveDirection, Vector3.up)) * Quaternion.Inverse(frame);
                transform.position += headPosition - HeadWorldPosition;
                return;
            }

            // Walk back along the recorded path: each joint sits on it at the bone's own length behind the previous.
            joints[0] = headPosition;
            Vector3 a = headPosition;
            int index = 0;
            for (int k = 1; k < n; k++)
            {
                Vector3 c = joints[k - 1];
                float length = segmentLengths[k - 1], length2 = length * length;
                while (index < trailCount && (TrailPoint(index) - c).sqrMagnitude < length2) { a = TrailPoint(index); index++; }
                if (index < trailCount) joints[k] = SphereExit(c, length, a, TrailPoint(index));
                else joints[k] = c + SafeDirection(k >= 2 ? joints[k - 1] - joints[k - 2] : -moveDirection, -moveDirection) * length;
                a = joints[k];
            }

            if (rootJoint > 0 && rootJoint < n - 1)
            {
                Vector3 along = joints[rootJoint - 1] - joints[rootJoint + 1];
                Quaternion rotation = Quaternion.LookRotation(SafeDirection(along, moveDirection), SafeUp(along, Vector3.up)) * rootFrameToRoot;
                transform.SetPositionAndRotation(joints[rootJoint] - rotation * (restJoints[rootJoint] * Scale), rotation);
            }
            for (int i = 0; i < spine.Length; i++)
            {
                SpineBone b = spine[i];
                Vector3 d = SafeDirection(joints[b.target] - joints[b.joint], moveDirection);
                b.bone.SetPositionAndRotation(joints[b.joint], Quaternion.LookRotation(d, SafeUp(d, Vector3.up)) * b.frameToBone);
            }
        }

        private static Vector3 SphereExit(Vector3 centre, float radius, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a, f = a - centre;
            float A = d.sqrMagnitude;
            if (A < 1e-8f) return b;
            float B = 2f * Vector3.Dot(f, d), C = f.sqrMagnitude - radius * radius;
            float t = (-B + Mathf.Sqrt(Mathf.Max(0f, B * B - 4f * A * C))) / (2f * A);
            return a + d * Mathf.Clamp01(t);
        }

        /// <summary>Spine from the rest pose: head bone down to the rig root, then the tail chain from that root.</summary>
        private void BuildSpine()
        {
            spine = new SpineBone[0]; restJoints = new Vector3[0]; segmentLengths = new float[0]; joints = new Vector3[0];
            bodyLength = 0f;
            if (headBone == null) return;
            var skinBones = new HashSet<Transform>();
            foreach (var r in renderers) if (r != null) foreach (var b in r.bones) if (b != null) skinBones.Add(b);

            var headward = new List<Transform>();
            for (Transform t = headBone; t != null && t != transform && skinBones.Contains(t); t = t.parent) headward.Add(t);
            if (headward.Count < 2) return;
            Transform chainRoot = headward[headward.Count - 1];

            // Longest skinned chain hanging off the same parent is the tail.
            var tailChain = new List<Transform>();
            if (chainRoot.parent != null)
                foreach (Transform sibling in chainRoot.parent)
                {
                    if (sibling == chainRoot || !skinBones.Contains(sibling)) continue;
                    var chain = new List<Transform>();
                    for (Transform t = sibling; t != null;)
                    {
                        chain.Add(t);
                        Transform next = null;
                        foreach (Transform child in t) if (skinBones.Contains(child)) { next = child; break; }
                        t = next;
                    }
                    if (chain.Count > tailChain.Count) tailChain = chain;
                }

            var points = new List<Vector3> { transform.InverseTransformPoint(HeadWorldPosition) };
            for (int i = 0; i < headward.Count; i++) points.Add(transform.InverseTransformPoint(headward[i].position));
            rootJoint = points.Count - 1;
            var tailJoints = new int[tailChain.Count];
            for (int i = 0; i < tailChain.Count; i++)
            {
                Vector3 p = transform.InverseTransformPoint(tailChain[i].position);
                if ((p - points[points.Count - 1]).sqrMagnitude > 1e-6f) points.Add(p);
                tailJoints[i] = points.Count - 1;
            }
            {
                // The last tail bone has no child joint: extend it by its predecessor's length.
                Vector3 last = points[points.Count - 1], before = points[points.Count - 2];
                points.Add(last + (last - before));
            }
            restJoints = points.ToArray();
            joints = new Vector3[restJoints.Length];
            segmentLengths = new float[restJoints.Length - 1];
            float scale = Scale;
            for (int k = 0; k < segmentLengths.Length; k++)
            {
                segmentLengths[k] = Mathf.Max(.01f, Vector3.Distance(restJoints[k], restJoints[k + 1]) * scale);
                bodyLength += segmentLengths[k];
            }

            var list = new List<SpineBone>();
            Quaternion toRoot = Quaternion.Inverse(transform.rotation);
            // Parent first: from the rig root up to the head, then down the tail.
            for (int i = headward.Count - 1; i >= 0; i--) list.Add(MakeSpineBone(headward[i], i + 1, i, toRoot));
            for (int i = 0; i < tailChain.Count; i++) list.Add(MakeSpineBone(tailChain[i], tailJoints[i], tailJoints[i] + 1, toRoot));
            spine = list.ToArray();

            if (rootJoint > 0 && rootJoint < restJoints.Length - 1)
            {
                Vector3 along = restJoints[rootJoint - 1] - restJoints[rootJoint + 1];
                rootFrameToRoot = Quaternion.Inverse(Quaternion.LookRotation(SafeDirection(along, modelForwardAxis), SafeUp(along, modelUpAxis)));
            }
        }

        private SpineBone MakeSpineBone(Transform bone, int joint, int target, Quaternion toRoot)
        {
            Vector3 d = SafeDirection(restJoints[target] - restJoints[joint], modelForwardAxis);
            Quaternion frame = Quaternion.LookRotation(d, SafeUp(d, modelUpAxis));
            return new SpineBone { bone = bone, joint = joint, target = target, frameToBone = Quaternion.Inverse(frame) * (toRoot * bone.rotation) };
        }

        // ---- Setup ----------------------------------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (initialized) return;
            modelForwardAxis = SafeDirection(modelForwardAxis, Vector3.forward);
            modelUpAxis = SafeUp(modelForwardAxis, modelUpAxis);
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            originalLocalScale = transform.localScale;
            renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            rendererBounds = new Bounds[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) rendererBounds[i] = renderers[i].localBounds;

            Transform[] all = GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                if (!byName.ContainsKey(all[i].name)) byName.Add(all[i].name, all[i]);
                // Only semantically named jaw bones are inferred. Generic Bone.033/.034 require explicit verification.
                if (jawBone == null && (all[i].name.ToLowerInvariant() == "jaw" || all[i].name.ToLowerInvariant() == "lowerjaw")) jawBone = all[i];
            }
            bool importedNaga = byName.ContainsKey("Naga_Rig") && byName.ContainsKey("Bone.001") && byName.ContainsKey("Bone.032");
            if (importedNaga && upperJawBone == null) byName.TryGetValue("Bone.033", out upperJawBone);
            if (headBone == null && importedNaga) byName.TryGetValue("Bone.032", out headBone);
            if (headBone == null)
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name.ToLowerInvariant() == "head") { headBone = all[i]; break; }
            if (jawBone != null) jawRestRotation = jawBone.localRotation;
            if (upperJawBone != null) upperJawRestRotation = upperJawBone.localRotation;

            // Every skinned bone's rest pose, so stopping always returns to the bind-like straight body.
            var bones = new HashSet<Transform>();
            foreach (var r in renderers) foreach (var b in r.bones) if (b != null && b.IsChildOf(transform)) bones.Add(b);
            restPoses = new RestPose[bones.Count];
            int n = 0;
            foreach (var b in bones) restPoses[n++] = new RestPose { bone = b, position = b.localPosition, rotation = b.localRotation, scale = b.localScale };
            initialized = true;
            ExpandRendererBounds();
        }

        private void RestoreBonePose()
        {
            for (int i = 0; i < restPoses.Length; i++)
            {
                if (restPoses[i].bone == null) continue;
                restPoses[i].bone.localPosition = restPoses[i].position;
                restPoses[i].bone.localRotation = restPoses[i].rotation;
                restPoses[i].bone.localScale = restPoses[i].scale;
            }
            if (jawBone != null) jawBone.localRotation = jawRestRotation;
            if (upperJawBone != null) upperJawBone.localRotation = upperJawRestRotation;
        }

        private void ExpandRendererBounds()
        {
            // Once per enable, no CPU mesh deformation or per-frame bounds allocations. A curled body stays within
            // half its length of the rig root, which the widest original extent already covers in every direction.
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Bounds bounds = rendererBounds[i];
                float extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
                bounds.extents = Vector3.Max(bounds.extents, Vector3.one * extent);
                renderers[i].localBounds = bounds;
            }
        }

        private static Vector3 Flat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.000001f ? value.normalized : fallback;
        }

        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback) => value.sqrMagnitude > 0.000001f ? value.normalized : fallback.normalized;

        private static Vector3 SafeUp(Vector3 forward, Vector3 up)
        {
            forward = SafeDirection(forward, Vector3.forward);
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < 0.000001f) up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            if (up.sqrMagnitude < 0.000001f) up = Vector3.ProjectOnPlane(Vector3.right, forward);
            return up.normalized;
        }

        private void OnValidate()
        {
            orbitRadius = Mathf.Max(2f, orbitRadius);
            chargeDuration = Mathf.Max(0.1f, chargeDuration);
            trailSpacing = Mathf.Max(.05f, trailSpacing);
        }
    }
}

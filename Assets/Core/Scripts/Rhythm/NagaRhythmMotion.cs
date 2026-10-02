using System.Collections.Generic;
using UnityEngine;

namespace AKI.Rhythm
{
    /// <summary>
    /// Lightweight, head-led swimming for the Naga. The encounter controller owns all timing and hits.
    /// Bones use their captured local pose; do not animate the same bones with an Animator concurrently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NagaRhythmMotion : MonoBehaviour
    {
        private enum MotionState { Idle, Orbit, Charge, Arrived }

        [Header("Head and model axes")]
        [SerializeField] private Transform headBone;
        [SerializeField] private Transform jawBone;
        [Tooltip("Offset in head-bone coordinates, e.g. the centre of the mouth. With no head bone, uses root coordinates.")]
        [SerializeField] private Vector3 headPointLocalOffset;
        [SerializeField] private Vector3 modelForwardAxis = Vector3.forward;
        [SerializeField] private Vector3 modelUpAxis = Vector3.up;

        [Header("Orbit around the moving player")]
        [Min(2f)] [SerializeField] private float orbitRadius = 16f;
        [Tooltip("Degrees per second. A negative value reverses the swimming direction.")]
        [SerializeField] private float angularSpeed = 27f;
        [SerializeField] private float heightOffset = 0.2f;
        [Min(0f)] [SerializeField] private float verticalAmplitude = 1.5f;
        [Min(0f)] [SerializeField] private float verticalFrequency = 0.22f;
        [Min(0f)] [SerializeField] private float radialAmplitude = 1.1f;
        [Min(0f)] [SerializeField] private float radialFrequency = 0.15f;
        [Min(0f)] [SerializeField] private float secondaryAmplitude = 0.45f;
        [Min(0f)] [SerializeField] private float secondaryFrequency = 0.37f;
        [Min(0.01f)] [SerializeField] private float orbitEntryDuration = 1.25f;
        [Header("Smooth irregular changes of direction")]
        [SerializeField] private Vector2 reverseEverySeconds = new Vector2(7f,13f);
        [Min(.3f)] [SerializeField] private float reverseSmoothSeconds = 2.2f;
        [Min(5f)] [SerializeField] private float headingDegreesPerSecond = 65f;
        [Range(0f,45f)] [SerializeField] private float maximumPitchDegrees = 20f;

        [Header("Body deformation")]
        [Tooltip("Optional explicit spine transforms. Empty auto-discovers the imported Naga rig or skinned body bones.")]
        [SerializeField] private Transform[] bodyBones = new Transform[0];
        [Range(0f, 1.5f)] [SerializeField] private float orbitCurveStrength = 1f;
        [Range(0f, 15f)] [SerializeField] private float maximumSegmentBend = 8f;
        [Range(0f, 12f)] [SerializeField] private float horizontalWaveDegrees = 3.5f;
        [Range(0f, 8f)] [SerializeField] private float verticalWaveDegrees = 1.1f;
        [Min(0f)] [SerializeField] private float waveFrequency = 0.65f;
        [Min(0.1f)] [SerializeField] private float waveLength = 10f;

        [Header("Slow swimming jaw")]
        [SerializeField] private bool swimmingJawMotion = true;
        [Range(0f,15f)] [SerializeField] private float swimJawDegrees = 5f;
        [Min(.02f)] [SerializeField] private float swimJawFrequency = .18f;

        [Header("Final charge")]
        [Min(0.1f)] [SerializeField] private float chargeDuration = 1.05f;
        [Tooltip("Mouth stops this far in front of the target head, rather than putting the root pivot inside the player.")]
        [Min(0f)] [SerializeField] private float chargeStopDistance = 0.2f;
        [SerializeField] private Vector3 jawLocalAxis = Vector3.right;
        [Range(-90f, 90f)] [SerializeField] private float jawOpenDegrees = 38f;
        [Tooltip("Raise the head relative to the neck during the final bite; the mouth still reaches the camera anchor.")]
        [Range(0f,25f)] [SerializeField] private float chargeHeadLiftDegrees = 9f;

        private MotionState state;
        private Transform orbitTarget;
        private Transform chargeTarget;
        private BonePose[] poses = new BonePose[0];
        private SkinnedMeshRenderer[] renderers = new SkinnedMeshRenderer[0];
        private Bounds[] rendererBounds = new Bounds[0];
        private Quaternion jawRestRotation;
        private Vector3 headForwardInBone;
        private Vector3 headUpInBone;
        private Quaternion headRestRotation;
        private Vector3 headLiftAxis;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private Vector3 orbitEntryPosition;
        private Vector3 chargeStartPosition;
        private Vector3 lastChargeTargetPosition;
        private Vector3 chargeApproachDirection;
        private Vector3 chargeInitialDirection;
        private Vector3 lastDirection = Vector3.forward;
        private float orbitAngle;
        private float motionTime;
        private float orbitEntryTime;
        private float chargeElapsed;
        private float hitPulse;
        private bool initialized;
        private NagaEnvironmentSafety safety;
        private float currentAngularSpeed, angularVelocity, nextReverse, directionSign, irregularPhase;
        private float avoidCooldown;
        private Vector3 orbitCentre, centreVelocity;

        private struct BonePose
        {
            public Transform bone;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Vector3 yawAxis;
            public Vector3 pitchAxis;
            public Vector3 segmentInRoot;
            public Vector3 distanceFromHeadInRoot;
            public float forwardSign;
        }

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

        private void Awake() { EnsureInitialized(); safety=GetComponent<NagaEnvironmentSafety>(); }

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
        public void ConfigureRig(Transform head, Transform jaw = null, Transform[] body = null)
        {
            StopMotion();
            headBone = head;
            jawBone = jaw;
            if (body != null) bodyBones = body;
            // Restore the original culling bounds before recapturing them.
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
            orbitTarget = player;
            chargeTarget = null;
            orbitEntryPosition = HeadWorldPosition;
            orbitCentre=safety!=null?safety.SafeOrbitCentre(player.position,orbitRadius+radialAmplitude+8f):player.position;
            centreVelocity=Vector3.zero;
            Vector3 relative = orbitEntryPosition - orbitCentre;
            orbitAngle = Mathf.Atan2(relative.z, relative.x);
            orbitEntryTime = 0f;
            motionTime = 0f;
            chargeElapsed = 0f;
            ChargeProgress = 0f;
            hitPulse = 0f;
            currentAngularSpeed=angularSpeed; directionSign=Mathf.Sign(angularSpeed);
            nextReverse=Random.Range(reverseEverySeconds.x,reverseEverySeconds.y);
            irregularPhase=Random.Range(0f,6.28f);avoidCooldown=0f;
            state = MotionState.Orbit;
        }

        public void BeginCharge(Transform playerHead)
        {
            EnsureInitialized();
            if (playerHead == null) { StopMotion(); return; }
            chargeStartPosition = HeadWorldPosition;
            chargeTarget = playerHead;
            lastChargeTargetPosition = playerHead.position;
            chargeApproachDirection = SafeDirection(lastChargeTargetPosition - chargeStartPosition, lastDirection);
            chargeInitialDirection = lastDirection;
            chargeElapsed = 0f;
            ChargeProgress = 0f;
            state = MotionState.Charge;
        }

        /// <summary>A brief local swim ripple; scoring, damage, audio and VFX belong to the controller.</summary>
        public void PlayHitReaction(float strength = 1f) => hitPulse = Mathf.Max(hitPulse, Mathf.Clamp01(strength));

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

        private void LateUpdate()
        {
            if (state == MotionState.Idle) return;
            float dt = Time.deltaTime;
            motionTime += dt;
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 3.5f);
            if (headBone != null) headBone.localRotation = headRestRotation;

            if (state == MotionState.Orbit)
            {
                if (orbitTarget == null) { StopMotion(); return; }
                Vector3 centreGoal=safety!=null?safety.SafeOrbitCentre(orbitTarget.position,orbitRadius+radialAmplitude+8f):orbitTarget.position;
                orbitCentre=Vector3.SmoothDamp(orbitCentre,centreGoal,ref centreVelocity,1.2f,Mathf.Infinity,dt);
                if(motionTime>=nextReverse) {directionSign=-directionSign;nextReverse=motionTime+Random.Range(reverseEverySeconds.x,reverseEverySeconds.y);}
                currentAngularSpeed=Mathf.SmoothDamp(currentAngularSpeed,Mathf.Abs(angularSpeed)*directionSign,ref angularVelocity,reverseSmoothSeconds,Mathf.Infinity,dt);
                float step=currentAngularSpeed*Mathf.Deg2Rad*dt;
                orbitAngle += step;
                orbitEntryTime += dt;
                float radius = Mathf.Max(2f, orbitRadius + radialAmplitude * Mathf.Sin(motionTime * radialFrequency * Mathf.PI * 2f));
                Vector3 radial = new Vector3(Mathf.Cos(orbitAngle), 0f, Mathf.Sin(orbitAngle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                float secondaryPhase = motionTime * secondaryFrequency * Mathf.PI * 2f;
                Vector3 desired = orbitCentre + radial * radius + tangent * (secondaryAmplitude * Mathf.Sin(secondaryPhase));
                float vp=motionTime*verticalFrequency*Mathf.PI*2f+irregularPhase;
                desired.y += heightOffset+verticalAmplitude*(Mathf.Sin(vp)+.45f*Mathf.Sin(vp*1.93f+.8f)+.25f*Mathf.Sin(vp*3.17f+2.4f));
                float entry = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(orbitEntryTime / orbitEntryDuration));
                Vector3 anchor = Vector3.Lerp(orbitEntryPosition, desired, entry);
                float radiansPerSecond = currentAngularSpeed * Mathf.Deg2Rad;
                Vector3 velocity = tangent * (radius * radiansPerSecond);
                velocity += radial * (radialAmplitude * radialFrequency * Mathf.PI * 2f * Mathf.Cos(motionTime * radialFrequency * Mathf.PI * 2f));
                velocity += tangent * (secondaryAmplitude * secondaryFrequency * Mathf.PI * 2f * Mathf.Cos(secondaryPhase));
                velocity -= radial * (secondaryAmplitude * Mathf.Sin(secondaryPhase) * radiansPerSecond);
                velocity.y = verticalAmplitude*verticalFrequency*Mathf.PI*2f*(Mathf.Cos(vp)+.45f*1.93f*Mathf.Cos(vp*1.93f+.8f)+.25f*3.17f*Mathf.Cos(vp*3.17f+2.4f));
                if (entry < 1f) velocity = Vector3.Lerp(desired - orbitEntryPosition, velocity, entry);
                ApplyBodyPose(-Mathf.Clamp(currentAngularSpeed/Mathf.Max(1f,Mathf.Abs(angularSpeed)),-1f,1f)*orbitCurveStrength/radius*entry,1f);
                Vector3 forward=SafeDirection(velocity,lastDirection);
                forward.y=Mathf.Clamp(forward.y,-Mathf.Sin(maximumPitchDegrees*Mathf.Deg2Rad),Mathf.Sin(maximumPitchDegrees*Mathf.Deg2Rad));
                forward=Vector3.RotateTowards(lastDirection,forward.normalized,headingDegreesPerSecond*Mathf.Deg2Rad*dt,0f).normalized;
                PlaceHead(anchor,forward);
                if (jawBone != null)
                {
                    float opening = swimmingJawMotion ? swimJawDegrees * (.5f - .5f * Mathf.Cos(motionTime * swimJawFrequency * 2f * Mathf.PI + irregularPhase)) : 0f;
                    jawBone.localRotation = jawRestRotation * Quaternion.AngleAxis(opening * Mathf.Sign(jawOpenDegrees), SafeDirection(jawLocalAxis, Vector3.right));
                }
                if(safety!=null&&!safety.Constrain())
                {
                    orbitAngle-=step;
                    if(motionTime>=avoidCooldown){directionSign=-directionSign;avoidCooldown=motionTime+4f;nextReverse=motionTime+Random.Range(reverseEverySeconds.x,reverseEverySeconds.y);}
                    if(safety.HasSafePose)
                    {
                        lastDirection=(headBone!=null?headBone:transform).TransformDirection(headForwardInBone).normalized;
                        Vector3 offset=HeadWorldPosition-orbitCentre;orbitAngle=Mathf.Atan2(offset.z,offset.x);
                    }
                }
            }
            else
            {
                if (chargeTarget != null) lastChargeTargetPosition = chargeTarget.position;
                if (state == MotionState.Charge)
                {
                    chargeElapsed += dt;
                    ChargeProgress = Mathf.Clamp01(chargeElapsed / chargeDuration);
                }
                Vector3 approach = SafeDirection(lastChargeTargetPosition - chargeStartPosition, chargeApproachDirection);
                Vector3 end = lastChargeTargetPosition - approach * chargeStopDistance;
                // Increasing speed makes the short finale read as a lunge, while still ending at an exact safe anchor.
                float travel = ChargeProgress * ChargeProgress;
                Vector3 anchor = Vector3.Lerp(chargeStartPosition, end, travel);
                float straightening = 1f - Mathf.SmoothStep(0f, 1f, ChargeProgress);
                // Keep the long tail curved and the torso nearly level during the lunge. Fully straightening
                // a 60m serpent towards an upward target would drive its tail into a reef or through the surface.
                ApplyBodyPose(-Mathf.Sign(currentAngularSpeed)*orbitCurveStrength/Mathf.Max(2f,orbitRadius)*Mathf.Lerp(.75f,1f,straightening),straightening*.7f);
                float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(chargeElapsed / Mathf.Min(.18f, chargeDuration * .3f)));
                Vector3 levelApproach=SafeDirection(Vector3.ProjectOnPlane(approach,Vector3.up),lastDirection);
                PlaceHead(anchor, SafeDirection(Vector3.Slerp(chargeInitialDirection,levelApproach,turn),levelApproach));
                if (headBone != null)
                {
                    float lift = chargeHeadLiftDegrees * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ChargeProgress * 2f));
                    headBone.localRotation = headRestRotation * Quaternion.AngleAxis(-lift, headLiftAxis);
                    // Raising the skull must not change where the mouth arrives.
                    transform.position += anchor - HeadWorldPosition;
                }
                if (jawBone != null)
                    jawBone.localRotation = jawRestRotation * Quaternion.AngleAxis(jawOpenDegrees * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ChargeProgress * 2.5f)), SafeDirection(jawLocalAxis, Vector3.right));
                if(safety!=null)safety.Constrain();
                if (ChargeProgress >= 1f) state = MotionState.Arrived;
            }
        }

        private void PlaceHead(Vector3 position, Vector3 forward)
        {
            Transform head = headBone != null ? headBone : transform;
            Vector3 localForward = transform.InverseTransformDirection(head.TransformDirection(headForwardInBone));
            Vector3 localUp = transform.InverseTransformDirection(head.TransformDirection(headUpInBone));
            Quaternion localHeadFrame = Quaternion.LookRotation(SafeDirection(localForward, Vector3.forward), SafeUp(localForward, localUp));
            transform.rotation = Quaternion.LookRotation(forward, SafeUp(forward, Vector3.up)) * Quaternion.Inverse(localHeadFrame);
            // Head is usually 20+ model units away from the imported pivot. Account for deformation AND root scale.
            transform.position += position - HeadWorldPosition;
            lastDirection = forward;
        }

        private void ApplyBodyPose(float curvature, float waveWeight)
        {
            float phase = motionTime * waveFrequency * Mathf.PI * 2f;
            for (int i = 0; i < poses.Length; i++)
            {
                BonePose pose = poses[i];
                if (pose.bone == null) continue;
                float segmentLength = transform.TransformVector(pose.segmentInRoot).magnitude;
                float distance = transform.TransformVector(pose.distanceFromHeadInRoot).magnitude;
                float wavePhase = phase - distance / Mathf.Max(0.1f, waveLength) * Mathf.PI * 2f;
                float taper = Mathf.Clamp01(distance / 3f);
                float bend = Mathf.Clamp(curvature * segmentLength * Mathf.Rad2Deg * pose.forwardSign, -maximumSegmentBend, maximumSegmentBend);
                float yaw = Mathf.Sin(wavePhase) * horizontalWaveDegrees * waveWeight * taper;
                float pitch = Mathf.Sin(wavePhase * 0.83f + 1.4f) * verticalWaveDegrees * waveWeight * taper;
                yaw += hitPulse * Mathf.Sin(wavePhase * 1.7f) * 1.8f * taper;
                pose.bone.localPosition = pose.position;
                pose.bone.localScale = pose.scale;
                pose.bone.localRotation = pose.rotation * Quaternion.AngleAxis(bend + yaw, pose.yawAxis) * Quaternion.AngleAxis(pitch, pose.pitchAxis);
            }
        }

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
            bool importedNaga = byName.ContainsKey("Naga_Rig") && byName.ContainsKey("Bone.001") && byName.ContainsKey("Bone.002")
                && byName.ContainsKey("Bone.031") && byName.ContainsKey("Bone.032") && byName.ContainsKey("Bone.037");
            if (headBone == null && importedNaga) byName.TryGetValue("Bone.032", out headBone);
            if (headBone == null)
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name.ToLowerInvariant() == "head") { headBone = all[i]; break; }
            Transform head = headBone != null ? headBone : transform;
            headForwardInBone = head.InverseTransformDirection(transform.TransformDirection(modelForwardAxis));
            headUpInBone = head.InverseTransformDirection(transform.TransformDirection(modelUpAxis));
            headRestRotation = head.localRotation;
            headLiftAxis = head.InverseTransformDirection(transform.TransformDirection(Vector3.Cross(modelUpAxis, modelForwardAxis))).normalized;
            if (jawBone != null) jawRestRotation = jawBone.localRotation;

            var selected = new List<Transform>();
            if (bodyBones != null && bodyBones.Length > 0)
            {
                for (int i = 0; i < bodyBones.Length; i++) AddBodyBone(selected, bodyBones[i]);
            }
            else if (importedNaga)
            {
                AddChain(selected, byName["Bone.001"]);
                AddChain(selected, byName["Bone.002"]);
            }
            else
            {
                for (int i = 0; i < renderers.Length; i++)
                    foreach (Transform bone in renderers[i].bones)
                        if (bone != null && bone.childCount > 0) AddBodyBone(selected, bone);
            }

            poses = new BonePose[selected.Count];
            Vector3 restHead = transform.InverseTransformPoint(head.position);
            Vector3 up = transform.TransformDirection(modelUpAxis);
            Vector3 right = transform.TransformDirection(Vector3.Cross(modelUpAxis, modelForwardAxis).normalized);
            for (int i = 0; i < selected.Count; i++)
            {
                Transform bone = selected[i];
                Vector3 local = transform.InverseTransformPoint(bone.position);
                Vector3 segment = Vector3.zero;
                if (bone.childCount > 0) segment = transform.InverseTransformPoint(bone.GetChild(0).position) - local;
                poses[i] = new BonePose
                {
                    bone = bone, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale,
                    yawAxis = bone.InverseTransformDirection(up).normalized,
                    pitchAxis = bone.InverseTransformDirection(right).normalized,
                    segmentInRoot = segment, distanceFromHeadInRoot = restHead - local,
                    forwardSign = Mathf.Sign(Vector3.Dot(segment, modelForwardAxis))
                };
            }
            lastDirection = transform.TransformDirection(modelForwardAxis);
            initialized = true;
            ExpandRendererBounds();
        }

        private void AddChain(List<Transform> selected, Transform bone)
        {
            while (bone != null && bone != headBone && bone != jawBone)
            {
                if (bone.childCount == 0) break;
                AddBodyBone(selected, bone);
                // Stop at branches to avoid bending unverified facial bones.
                if (bone.childCount != 1) break;
                bone = bone.GetChild(0);
            }
        }

        private void AddBodyBone(List<Transform> selected, Transform bone)
        {
            if (bone == null || bone == transform || bone == headBone || bone == jawBone || !bone.IsChildOf(transform)) return;
            if (headBone != null && bone.IsChildOf(headBone)) return;
            if (jawBone != null && bone.IsChildOf(jawBone)) return;
            if (!selected.Contains(bone)) selected.Add(bone);
        }

        private void RestoreBonePose()
        {
            for (int i = 0; i < poses.Length; i++)
            {
                if (poses[i].bone == null) continue;
                poses[i].bone.localPosition = poses[i].position;
                poses[i].bone.localRotation = poses[i].rotation;
                poses[i].bone.localScale = poses[i].scale;
            }
            if (jawBone != null) jawBone.localRotation = jawRestRotation;
            if (headBone != null) headBone.localRotation = headRestRotation;
        }

        private void ExpandRendererBounds()
        {
            // Once per enable, no CPU mesh deformation or per-frame bounds allocations.
            // Bending the 54-unit straight bind pose into an arc can exceed its narrow original bounds.
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Bounds bounds = rendererBounds[i];
                float extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
                bounds.extents = Vector3.Max(bounds.extents, Vector3.one * extent);
                renderers[i].localBounds = bounds;
            }
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
            orbitEntryDuration = Mathf.Max(0.01f, orbitEntryDuration);
            chargeDuration = Mathf.Max(0.1f, chargeDuration);
            waveLength = Mathf.Max(0.1f, waveLength);
        }
    }
}

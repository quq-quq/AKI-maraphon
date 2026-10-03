using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AKI.Menu
{
    /// <summary>
    /// The grandfather's dive (Fishman_Cutscene model): sits in the boat on the first frame of Sit_Stand until
    /// <see cref="Play"/>, then stands up (Sit_Stand), blends into the run with a short acceleration and dives
    /// (Run_To_Dive). The same sequence as the editor preview (Tools > Fishman > Preview Full Cutscene), driven by a
    /// playable graph instead of an animator controller. The clips carry the motion baked into the pose, so the body
    /// travels with the animation while this object stays put.
    /// </summary>
    public class FishmanCutscene : MonoBehaviour
    {
        [Tooltip("The grandfather's Humanoid animator (the Actor object).")]
        public Animator actor;
        public AnimationClip sitStand;
        public AnimationClip runToDive;
        [Tooltip("Overlap of standing up and starting to run (s).")]
        [Range(0.1f, 0.6f)] public float crossfadeSeconds = 0.25f;
        [Tooltip("The run starts from standing still and reaches full speed over this time (s).")]
        [Range(0f, 1f)] public float runAccelerationSeconds = 0.4f;

        [Header("Seated breathing (additive, stops when standing up)")]
        public bool seatedBreathing = true;
        [Range(4f,30f)] public float breathsPerMinute = 12f;
        [Tooltip("Small back bend in degrees; no root, boat or leg movement.")]
        [Range(0f,5f)] public float breathingSpineDegrees = 1.4f;
        [Range(0f,5f)] public float breathingChestDegrees = 2.2f;
        [Range(0f,.03f)] public float breathingChestExpansion = .012f;
        [Min(.05f)] public float breathingBlendOutSeconds = .25f;
        [Tooltip("Actor-only seated height offset in metres; smoothly removed when standing up.")]
        [Range(-.2f,.1f)] public float seatedHeightOffset = -.055f;
        [Tooltip("Keep the pelvis on the seat shown in the edited scene, despite Humanoid root normalization.")]
        public bool alignSeatedHips;
        public Vector3 seatedHipsLocalPosition;
        [Min(.1f)] public float seatedAlignmentBlendOutSeconds = 1f;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable sitPlayable, divePlayable;
        AnimationScriptPlayable breathingPlayable;
        SeatedBreathingJob breathingJob;
        float time;
        float diveTime;
        bool playing;
        Transform head;
        SkinnedMeshRenderer[] skins;
        Transform breathingSpine, breathingChest;
        Quaternion seatedSpineRotation, seatedChestRotation;
        Vector3 spineBreathingAxis, chestBreathingAxis;
        float breathingWeight = 1f;
        Vector3 actorBaseLocalPosition;
        Vector3 seatedActorCorrection;

        // Pose the bones inside the animation stream so the skinned mesh receives the final animated pose.
        struct SeatedBreathingJob : IAnimationJob
        {
            public TransformStreamHandle spine, chest;
            public Vector3 spineAxis, chestAxis;
            public float spineBend, chestBend, chestExpansion;
            public bool hasSpine, hasChest;
            public void ProcessRootMotion(AnimationStream stream) { }
            public void ProcessAnimation(AnimationStream stream)
            {
                if (hasSpine && spine.IsValid(stream))
                    spine.SetLocalRotation(stream, Quaternion.AngleAxis(spineBend,spineAxis) * spine.GetLocalRotation(stream));
                if (hasChest && chest.IsValid(stream))
                {
                    chest.SetLocalRotation(stream, Quaternion.AngleAxis(chestBend,chestAxis) * chest.GetLocalRotation(stream));
                    chest.SetLocalScale(stream, chest.GetLocalScale(stream) * (1f + chestExpansion));
                }
            }
        }

        public bool IsPlaying => playing;

        /// <summary>The whole sequence has played (the grandfather is at the end of the dive).</summary>
        public bool IsFinished => playing && time >= Duration;

        /// <summary>0..1 through Run_To_Dive (0 while still sitting / standing up).</summary>
        public float DiveProgress => runToDive != null && runToDive.length > 0f ? Mathf.Clamp01(diveTime / runToDive.length) : 0f;

        /// <summary>The grandfather's head bone.</summary>
        public Transform Head => head;

        float Crossfade => sitStand != null && runToDive != null ? Mathf.Clamp(crossfadeSeconds, 0f, Mathf.Min(0.6f, sitStand.length, runToDive.length)) : 0f;
        float RunAcceleration => Crossfade > 1e-5f ? runAccelerationSeconds : 0f;
        float TransitionStart => (sitStand != null ? sitStand.length : 0f) - Crossfade;
        public float Duration => (sitStand != null ? sitStand.length : 0f) + (runToDive != null ? runToDive.length : 0f) - Crossfade + RunAcceleration * 0.5f;

        void Awake()
        {
            if (actor == null) actor = GetComponentInChildren<Animator>();
            if (actor == null || sitStand == null || runToDive == null)
            {
                Debug.LogError("FishmanCutscene: needs the actor's Animator and the Sit_Stand / Run_To_Dive clips.", this);
                enabled = false;
                return;
            }

            actor.runtimeAnimatorController = null;
            actorBaseLocalPosition = actor.transform.localPosition;
            actor.applyRootMotion = false;
            actor.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            head = actor.isHuman ? actor.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) head = actor.transform;
            skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer skin in skins) skin.updateWhenOffscreen = true;

            graph = PlayableGraph.Create("Fishman Cutscene");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            sitPlayable = AnimationClipPlayable.Create(graph, sitStand);
            divePlayable = AnimationClipPlayable.Create(graph, runToDive);
            sitPlayable.SetApplyFootIK(false);
            divePlayable.SetApplyFootIK(false);
            sitPlayable.SetSpeed(0);
            divePlayable.SetSpeed(0);
            graph.Connect(sitPlayable, 0, mixer, 0);
            graph.Connect(divePlayable, 0, mixer, 1);
            AnimationPlayableOutput.Create(graph, "Fishman", actor).SetSourcePlayable(mixer);
            graph.Play();
            Sample(0f);   // seated
            if (alignSeatedHips && actor.isHuman)
            {
                Vector3 delta = transform.TransformPoint(seatedHipsLocalPosition) - actor.GetBoneTransform(HumanBodyBones.Hips).position;
                seatedActorCorrection = actor.transform.parent.InverseTransformVector(delta);
            }
            if (actor.isHuman)
            {
                breathingSpine = actor.GetBoneTransform(HumanBodyBones.Spine);
                breathingChest = actor.GetBoneTransform(HumanBodyBones.Chest);
                if (breathingSpine != null)
                {
                    seatedSpineRotation = breathingSpine.localRotation;
                    spineBreathingAxis = breathingSpine.parent.InverseTransformDirection(actor.transform.right).normalized;
                }
                if (breathingChest != null)
                {
                    seatedChestRotation = breathingChest.localRotation;
                    chestBreathingAxis = breathingChest.parent.InverseTransformDirection(actor.transform.right).normalized;
                }
            }
            breathingJob = new SeatedBreathingJob
            {
                hasSpine = breathingSpine != null, hasChest = breathingChest != null,
                spineAxis = spineBreathingAxis, chestAxis = chestBreathingAxis
            };
            if (breathingSpine != null) breathingJob.spine = actor.BindStreamTransform(breathingSpine);
            if (breathingChest != null) breathingJob.chest = actor.BindStreamTransform(breathingChest);
            breathingPlayable = AnimationScriptPlayable.Create(graph,breathingJob,1);
            graph.Connect(mixer,0,breathingPlayable,0);
            breathingPlayable.SetInputWeight(0,1f);
            ((AnimationPlayableOutput)graph.GetOutput(0)).SetSourcePlayable(breathingPlayable);
            ApplySeatedHeight(1f);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        /// <summary>Stands up, runs and dives.</summary>
        public void Play()
        {
            if (!enabled) return;
            playing = true;
            time = 0f;
        }

        public void SetActorVisible(bool visible)
        {
            if (skins == null) return;
            foreach (SkinnedMeshRenderer skin in skins) skin.enabled = visible;
        }

        /// <summary>Where the head is sitting in the boat and at the very end of the dive (world, at the current pose of this object).</summary>
        public void MeasureDive(out Vector3 headSeated, out Vector3 headAtEnd)
        {
            Sample(0f);
            ApplySeatedHeight(1f);
            headSeated = head.position;
            Sample(Duration);
            ApplySeatedHeight(0f);
            headAtEnd = head.position;
            Sample(playing ? time : 0f);
            ApplySeatedHeight(SeatWeight);
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            if (playing) time = Mathf.Min(Duration, time + Time.deltaTime);
        }

        float SeatWeight => playing ? 1f - Mathf.SmoothStep(0f,1f,time / Mathf.Max(.1f,seatedAlignmentBlendOutSeconds)) : 1f;

        void LateUpdate()
        {
            if (!graph.IsValid()) return;
            ApplySeatedHeight(SeatWeight);
            breathingWeight = Mathf.MoveTowards(breathingWeight, seatedBreathing && !playing ? 1f : 0f,
                Time.deltaTime / Mathf.Max(.05f, breathingBlendOutSeconds));
            float inhale = .5f - .5f * Mathf.Cos(Time.time * Mathf.Max(0f, breathsPerMinute) / 60f * 2f * Mathf.PI);
            float bend = inhale * breathingWeight;
            breathingJob.spineBend = bend * breathingSpineDegrees;
            breathingJob.chestBend = bend * breathingChestDegrees;
            breathingJob.chestExpansion = bend * breathingChestExpansion;
            breathingPlayable.SetJobData(breathingJob);
            Sample(playing ? time : 0f);
            ApplySeatedHeight(SeatWeight);
        }

        // Seated first frame -> Sit_Stand -> SmoothStep blend into the run (no pose pop) with the run's time
        // ramped up from standing still -> Run_To_Dive.
        void Sample(float seconds)
        {
            if (!graph.IsValid()) return;
            float runWeight = Crossfade > 1e-5f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TransitionStart, TransitionStart + Crossfade, seconds))
                : seconds >= TransitionStart ? 1f : 0f;
            mixer.SetInputWeight(0, 1f - runWeight);
            mixer.SetInputWeight(1, runWeight);
            sitPlayable.SetTime(Mathf.Clamp(seconds, 0f, sitStand.length));

            float elapsed = Mathf.Max(0f, seconds - TransitionStart);
            float runTime = elapsed;
            if (RunAcceleration > 1e-5f)
            {
                // integral of a SmoothStep speed ramp (0 -> clip speed), so the baked trajectory isn't sped up twice
                float u = Mathf.Clamp01(elapsed / RunAcceleration);
                runTime = elapsed < RunAcceleration
                    ? RunAcceleration * (u * u * u - 0.5f * u * u * u * u)
                    : elapsed - RunAcceleration * 0.5f;
            }
            diveTime = Mathf.Clamp(runTime, 0f, runToDive.length);
            divePlayable.SetTime(diveTime);
            graph.Evaluate(0f);
        }

        void ApplySeatedHeight(float weight)
        {
            if (actor != null && actor.transform != transform)
                actor.transform.localPosition = actorBaseLocalPosition + (seatedActorCorrection + Vector3.up * seatedHeightOffset) * weight;
        }
    }
}

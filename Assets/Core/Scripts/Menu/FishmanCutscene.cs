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

        [Header("Seated idle (additive, fades out when standing)")]
        public bool seatedBreathing = true;
        [Range(4f, 30f)] public float breathsPerMinute = 12f;
        [Range(0f, 3f)] public float breathingSpineDegrees = .55f;
        [Range(0f, 3f)] public float breathingChestDegrees = .8f;
        [Min(.05f)] public float breathingBlendOutSeconds = .25f;
        [Tooltip("Actor-only seated height correction in metres. Fades out before the run; never moves the boat.")]
        public float seatedHeightOffset = -.055f;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable sitPlayable, divePlayable;
        float time;
        float diveTime;
        bool playing;
        Transform head;
        SkinnedMeshRenderer[] skins;
        Transform breathingSpine, breathingChest;
        Quaternion seatedSpineRotation, seatedChestRotation;
        Vector3 spineBreathingAxis, chestBreathingAxis, actorBasePosition;
        float seatedWeight = 1f;

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
            actorBasePosition = actor.transform.localPosition;
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
            actor.transform.localPosition = actorBasePosition + Vector3.up * seatedHeightOffset;
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
            actor.transform.localPosition = actorBasePosition + Vector3.up * seatedHeightOffset;
            headSeated = head.position;
            Sample(Duration);
            actor.transform.localPosition = actorBasePosition;
            headAtEnd = head.position;
            Sample(playing ? time : 0f);
            actor.transform.localPosition = actorBasePosition + Vector3.up * (seatedHeightOffset * seatedWeight);
        }

        void Update()
        {
            if (!playing) return;
            time = Mathf.Min(Duration, time + Time.deltaTime);
            Sample(time);
        }

        void LateUpdate()
        {
            if (!graph.IsValid()) return;
            seatedWeight = Mathf.MoveTowards(seatedWeight, playing ? 0f : 1f,
                Time.deltaTime / Mathf.Max(.05f, breathingBlendOutSeconds));
            actor.transform.localPosition = actorBasePosition + Vector3.up * (seatedHeightOffset * seatedWeight);
            if (!playing)
            {
                if (breathingSpine != null) breathingSpine.localRotation = seatedSpineRotation;
                if (breathingChest != null) breathingChest.localRotation = seatedChestRotation;
            }
            if (!seatedBreathing || seatedWeight <= 0f) return;
            float inhale = .5f - .5f * Mathf.Cos(Time.time * breathsPerMinute / 60f * 2f * Mathf.PI);
            float bend = inhale * seatedWeight;
            if (breathingSpine != null)
                breathingSpine.localRotation = Quaternion.AngleAxis(bend * breathingSpineDegrees, spineBreathingAxis) * breathingSpine.localRotation;
            if (breathingChest != null)
                breathingChest.localRotation = Quaternion.AngleAxis(bend * breathingChestDegrees, chestBreathingAxis) * breathingChest.localRotation;
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
    }
}

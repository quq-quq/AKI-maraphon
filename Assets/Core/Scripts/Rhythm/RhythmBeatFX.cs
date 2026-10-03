using AKI.Water;
using UnityEngine;

namespace AKI.Rhythm
{
    /// <summary>
    /// Lets the player feel the music's beat: on every beat of the <see cref="RhythmConductor"/> the camera's field of
    /// view gives a short kick and a soft glow pulse runs along the top edge of the screen, from the centre out to the sides (drawn by the water lens pass,
    /// <see cref="WaterLensFeature"/>). Lives on the player camera; <see cref="RhythmGameFlow"/> adds one when the
    /// camera has none, so add it to the camera yourself to keep tuned settings.
    /// The kick sits on top of whatever else sets the field of view (swimming speed, aiming): it is added after them
    /// in LateUpdate and taken off again before the next frame, so their smoothing never sees it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(1000)]
    public sealed class RhythmBeatFX : MonoBehaviour
    {
        static readonly int EdgeId = Shader.PropertyToID("_RhythmBeatEdge");
        static readonly int EdgeWidthId = Shader.PropertyToID("_RhythmBeatWidth");
        static readonly int SpreadId = Shader.PropertyToID("_RhythmBeatSpread");

        [Tooltip("Music clock. Empty = the active RhythmConductor.")]
        public RhythmConductor conductor;

        [Header("Field of view kick")]
        [Tooltip("Degrees the view widens on a beat (negative = it narrows). 0 = off.")]
        [Range(-10f, 10f)] public float fovKick = 0.25f;
        [Tooltip("Seconds the kick takes to rise.")]
        [Min(0f)] public float fovAttackSeconds = 0.08f;
        [Tooltip("Seconds the kick takes to settle back.")]
        [Min(0.01f)] public float fovReleaseSeconds = 0.3f;

        [Header("Pulse along the top edge, from the centre out to the sides")]
        [ColorUsage(false, true)] public Color edgeColour = new Color(0.55f, 0.9f, 1f, 1f);
        [Tooltip("Brightness of the pulse right at the top edge on a beat. 0 = off.")]
        [Range(0f, 2f)] public float edgeIntensity = 0.06f;
        [Tooltip("How far the pulse reaches down from the top edge, share of the screen height.")]
        [Range(0.02f, 0.5f)] public float edgeWidth = 0.07f;
        [Tooltip("While it fades the pulse reaches this much further down (share of Edge Width).")]
        [Range(0f, 2f)] public float edgeTravel = 0.2f;
        [Tooltip("Length of the soft trail behind the pulse's front, share of the half screen width (it spreads from " +
                 "the centre to each side edge over the whole pulse).")]
        [Range(0.05f, 1f)] public float edgeTrail = 0.45f;
        [Tooltip("Seconds the glow takes to rise.")]
        [Min(0f)] public float edgeAttackSeconds = 0.07f;
        [Tooltip("Seconds the glow takes to fade.")]
        [Min(0.01f)] public float edgeReleaseSeconds = 0.32f;

        Camera cam;
        float appliedFov;                  // what was added to the field of view this frame, to take it off again
        float sinceBeat = float.MaxValue;  // seconds since the last beat
        int lastBeat = -1;
        long lastLoop;
        int lastSession;
        float fovLevel, edgeLevel;         // where each envelope is now: a beat that comes early rises from there
        float fovFrom, edgeFrom;           // ... and where it was when the beat came

        void Awake() => cam = GetComponent<Camera>();

        void OnDisable()
        {
            RemoveKick();
            SetEdge(0f, 0f);
            sinceBeat = float.MaxValue;
            lastBeat = -1;
            fovLevel = edgeLevel = fovFrom = edgeFrom = 0f;
        }

        // Runs before every LateUpdate: whoever sets the field of view this frame starts from its own value.
        void Update() => RemoveKick();

        void LateUpdate()
        {
            RhythmConductor clock = conductor != null ? conductor : RhythmConductor.Active;
            bool playing = clock != null && clock.isActiveAndEnabled && clock.IsPlaying;
            int beat = playing ? clock.CurrentBeat : -1;
            long loop = playing ? clock.LoopIndex : 0;
            int session = playing ? clock.Session : 0;
            // a new beat: another index, or the same one again in the next loop of the track or in a new track
            if (beat >= 0 && (beat != lastBeat || loop != lastLoop || session != lastSession))
            {
                sinceBeat = 0f;
                fovFrom = fovLevel;
                edgeFrom = edgeLevel;
            }
            else if (sinceBeat < float.MaxValue) sinceBeat += Time.unscaledDeltaTime;
            lastBeat = beat;
            lastLoop = loop;
            lastSession = session;

            fovLevel = Envelope(sinceBeat, fovAttackSeconds, fovReleaseSeconds, fovFrom);
            appliedFov = fovKick * fovLevel;
            if (cam != null) cam.fieldOfView += appliedFov;

            edgeLevel = Envelope(sinceBeat, edgeAttackSeconds, edgeReleaseSeconds, edgeFrom);
            float travel = Mathf.Clamp01(sinceBeat / Mathf.Max(0.01f, edgeAttackSeconds + edgeReleaseSeconds));
            SetEdge(edgeIntensity * edgeLevel, edgeWidth * (1f + edgeTravel * travel));
            float front = 1f - (1f - travel) * (1f - travel);   // quick out of the centre, easing in at the sides
            Shader.SetGlobalVector(SpreadId, new Vector4(front, edgeTrail, 0f, 0f));
        }

        // from -> 1 over the attack, back to 0 over the release.
        static float Envelope(float since, float attack, float release, float from)
        {
            if (since >= attack + release) return 0f;
            if (since < attack) return Mathf.SmoothStep(from, 1f, since / attack);
            return 1f - Mathf.SmoothStep(0f, 1f, (since - attack) / release);
        }

        void RemoveKick()
        {
            if (appliedFov != 0f && cam != null) cam.fieldOfView -= appliedFov;
            appliedFov = 0f;
        }

        void SetEdge(float strength, float width)
        {
            Shader.SetGlobalVector(EdgeId, new Vector4(edgeColour.r, edgeColour.g, edgeColour.b, strength));
            Shader.SetGlobalFloat(EdgeWidthId, width);
            WaterLensFeature.RhythmBeatActive = strength > 0.001f;
        }
    }
}

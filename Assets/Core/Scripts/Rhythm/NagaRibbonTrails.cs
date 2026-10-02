using UnityEngine;

namespace AKI.Rhythm
{
    /// <summary>
    /// Glowing ribbons wound around the Naga. A few trails circle its head; every joint of the body follows the path
    /// the head drew (<see cref="NagaRhythmMotion"/>), so what they leave behind is a spiral the body swims through.
    /// They swell and brighten on the music's beats and flare when the Naga is hit.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after NagaRhythmMotion has posed the bones this frame
    [RequireComponent(typeof(NagaRhythmMotion))]
    public class NagaRibbonTrails : MonoBehaviour
    {
        const float MaxSpeed = 60f;   // m/s: anything faster is a teleport, not a trail

        [Tooltip("AKI/UnderwaterParticle material, additive: fades with distance and loses colour in the water.")]
        public Material material;
        [Min(1)] public int ribbons = 3;
        [Tooltip("How far from the spine the ribbons circle (m).")]
        [Min(0f)] public float radius = 1.6f;
        [Tooltip("Turns around the body per second: the tighter, the denser the spiral.")]
        public float turnsPerSecond = 0.45f;
        [Tooltip("Seconds a ribbon lingers: long enough to reach along the whole body.")]
        [Min(0.1f)] public float trailSeconds = 4f;
        [Tooltip("Width at the head (m); it thins out towards the tail.")]
        [Min(0f)] public float width = 0.14f;
        public Gradient colour = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(0.45f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.65f, 0.45f, 1f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) }
        };
        [Tooltip("Glow of the ribbons (HDR multiplier on the colour).")]
        [Min(0f)] public float glow = 2.5f;

        [Header("Rhythm")]
        [Tooltip("Extra width and glow on a beat of the music.")]
        [Min(0f)] public float beatSwell = 0.8f;
        [Tooltip("Extra width and glow when the Naga is hit.")]
        [Min(0f)] public float hitFlare = 2f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        NagaRhythmMotion motion;
        Material instance;
        Color baseColour;
        TrailRenderer[] trails;
        Vector3 lastHead;
        Vector3 direction = Vector3.forward;
        float angle;
        bool started;

        void Start()
        {
            motion = GetComponent<NagaRhythmMotion>();
            if (material == null) { enabled = false; return; }
            instance = new Material(material) { name = material.name + " (Naga)" };
            baseColour = instance.GetColor(BaseColorId);

            var widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.55f), new Keyframe(1f, 0f));
            trails = new TrailRenderer[ribbons];
            for (int i = 0; i < ribbons; i++)
            {
                var go = new GameObject("Ribbon " + i);
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TrailRenderer>();
                t.sharedMaterial = instance;
                t.time = trailSeconds;
                t.minVertexDistance = 0.3f;
                t.widthCurve = widthCurve;
                t.widthMultiplier = width;
                t.colorGradient = colour;
                t.textureMode = LineTextureMode.Stretch;
                t.alignment = LineAlignment.View;
                t.numCapVertices = 2;
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.receiveShadows = false;
                t.emitting = false;
                trails[i] = t;
            }
        }

        void OnDestroy()
        {
            if (instance != null) Destroy(instance);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (trails == null || dt <= 0f) return;

            Vector3 head = motion.HeadBone != null ? motion.HeadBone.position : transform.position;
            Vector3 moved = head - lastHead;
            lastHead = head;
            // the first frame and a teleport are not motion: start the ribbons afresh where the head is now
            bool jumped = !started || moved.magnitude > MaxSpeed * dt;
            started = true;
            if (!jumped && moved.sqrMagnitude > 1e-6f)
                direction = Vector3.Slerp(direction, moved.normalized, 1f - Mathf.Exp(-dt * 8f)).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, direction);
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(direction, right);
            angle += dt * turnsPerSecond * Mathf.PI * 2f;

            RhythmConductor clock = RhythmConductor.Active;
            float beat = clock != null && clock.IsPlaying ? Mathf.Clamp01(clock.BeatPulse) : 0f;
            float boost = 1f + beatSwell * beat + hitFlare * motion.HitPulse;
            instance.SetColor(BaseColorId, baseColour * (glow * boost));

            for (int i = 0; i < trails.Length; i++)
            {
                float a = angle + i * Mathf.PI * 2f / trails.Length;
                float r = radius * (1f + 0.15f * Mathf.Sin(Time.time * 1.3f + i * 2.1f));
                var t = trails[i];
                t.transform.position = head + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * r;
                t.widthMultiplier = width * boost;
                if (jumped) t.Clear();
                t.emitting = !jumped;
            }
        }
    }
}

using AKI.Rhythm;
using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// Shows the <see cref="WaterCurrent"/>: specks of sediment drifting with the water in front of the camera. They only
    /// live inside the camera's view (a speck that leaves it is reborn somewhere in it), so none are spent behind the
    /// player. They travel <see cref="flowSpeed"/> times faster than the water: the swimmer drifts with the current too,
    /// so specks moving at the water's own speed would look still next to them. Every speck moves with the water at its
    /// own spot (plus a little wobble), so eddies and slower deep water show too. They are stretched along their motion
    /// relative to the camera, like a short exposure, and are only there under water.
    /// </summary>
    public class WaterCurrentView : MonoBehaviour
    {
        const float MaxCameraSpeed = 15f;   // m/s: anything faster is a teleport
        const float MaxScreenSize = 0.012f; // share of the screen a speck may cover between beats

        [Tooltip("AKI/UnderwaterParticle material, additive (M_BubbleFizz). Its colour and fade settings are used.")]
        public Material material;
        [Tooltip("Speck sprite (white + alpha). Empty = a built-in one: a crisp core in a faint halo.")]
        public Texture2D texture;

        [Header("Where")]
        [Tooltip("Specks live in the camera's view from this distance (m)...")]
        [Min(0.05f)] public float nearDistance = 0.2f;
        [Tooltip("...up to this one (m); they fade out towards it.")]
        [Min(1f)] public float viewDistance = 10f;
        [Tooltip("The view is widened by this share, so turning the head doesn't show empty edges.")]
        [Range(0f, 0.6f)] public float margin = 0.2f;
        [Tooltip("How quickly they fade with distance (1/m); lower than the bubbles', so the flow reads further out.")]
        [Min(0f)] public float fadeDensity = 0.06f;

        [Header("Specks")]
        [Min(0)] public int count = 1100;
        [Tooltip("Size range (m). Most are small, a few big.")]
        public Vector2 size = new Vector2(0.006f, 0.05f);
        [Tooltip("How strongly small specks outnumber big ones (1 = evenly spread).")]
        [Min(1f)] public float smallBias = 2.2f;
        [Tooltip("Colours they are picked between: bright plankton and duller sediment.")]
        public Color colorA = new Color(0.8f, 0.95f, 1f, 0.75f);
        public Color colorB = new Color(0.62f, 0.72f, 0.62f, 0.55f);
        [Tooltip("Seconds a speck lives (it fades in and out, so nothing pops).")]
        public Vector2 lifetime = new Vector2(5f, 10f);
        [Tooltip("Seconds a new speck takes to fade in.")]
        [Min(0.05f)] public float fadeIn = 0.5f;
        [Tooltip("Specks catching the light: how much their brightness flickers (0 = steady).")]
        [Range(0f, 1f)] public float twinkle = 0.3f;

        [Header("Motion")]
        [Tooltip("Speed of the specks as a multiple of the water's. The swimmer drifts with the water, so at 1 they'd seem to stand still beside them; at 2 they pass by at the current's own speed.")]
        [Min(0f)] public float flowSpeed = 2f;
        [Tooltip("Random drift on top of the current (m/s), so specks don't move in lockstep.")]
        [Min(0f)] public float wobble = 0.05f;
        [Tooltip("Stretch along the motion relative to the camera (seconds of travel): swimming makes them streak.")]
        [Min(0f)] public float stretch = 0.05f;

        [Header("Rhythm")]
        [Tooltip("Music clock. Empty = the active RhythmConductor.")]
        [SerializeField] RhythmConductor rhythm;
        [Tooltip("Extra glow on the music's recorded rhythmic pulses (0 = no beat pulse).")]
        [Min(0f)] public float beatEmission = 6f;
        [Tooltip("Specks swell by this share on a beat, so the flash reads as light blooming from each mote rather than a one-pixel blink (0 = size stays).")]
        [Min(0f)] public float beatSizeBoost = 1.5f;
        [ColorUsage(false, true)] public Color beatGlowColour = new Color(.45f, .85f, 1f, 1f);
        [Tooltip("Maximum vertical excursion on strong recorded music transients (m). This is visual motion only, so it cannot accumulate drift.")]
        [Min(0f)] public float accentAmplitude = 0.1f;
        [Tooltip("Vertical oscillations per second during an accent.")]
        [Min(0f)] public float accentFrequency = 8f;
        [Tooltip("Seconds for the particles to respond to a beat or accent.")]
        [Min(0.001f)] public float rhythmAttackTime = 0.025f;
        [Tooltip("Seconds for the rhythm glow and motion to settle when the music stops.")]
        [Min(0.001f)] public float rhythmReleaseTime = 0.15f;

        static readonly int RhythmId = Shader.PropertyToID("_CurrentRhythm");
        static readonly int GlowId = Shader.PropertyToID("_CurrentGlowColour");

        Transform root;
        Material instance;
        Texture2D builtIn;
        ParticleSystem system;
        ParticleSystemRenderer speckRenderer;
        ParticleSystem.Particle[] buffer;
        Camera cam;
        Vector3 lastCamPos;
        Vector3 camVelocity;
        bool wasUnder;
        float rhythmBeat;
        float rhythmAccent;
        float rhythmPhase;

        void Start()
        {
            if (material == null)
            {
                Debug.LogWarning("WaterCurrentView: no material, the current won't be shown.", this);
                enabled = false;
                return;
            }
            root = new GameObject("Water Current View").transform;
            instance = new Material(material) { name = material.name + " (current)" };
            instance.SetFloat("_FadeDensity", fadeDensity);
            instance.SetVector(RhythmId, Vector4.zero);
            instance.SetColor(GlowId, beatGlowColour);
            if (texture == null) texture = builtIn = BuildSpeckTexture(64);
            instance.SetTexture("_BaseMap", texture);
            system = Build();
            buffer = new ParticleSystem.Particle[count];
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (instance != null) Destroy(instance);
            if (builtIn != null) Destroy(builtIn);
        }

        void LateUpdate()
        {
            UpdateRhythm(Time.unscaledDeltaTime);
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null || system == null) return;
            // Unity hot reload cannot retain ParticleSystem.Particle[]; also support live count edits safely.
            if (buffer == null || buffer.Length < count) buffer = new ParticleSystem.Particle[count];

            float dt = Time.deltaTime;
            Vector3 camPos = cam.transform.position;
            Vector3 moved = camPos - lastCamPos;
            lastCamPos = camPos;
            // a teleport (respawn, cutscene) is not motion: no streaks, refill the view
            bool jumped = dt <= 0f || moved.magnitude > MaxCameraSpeed * dt;
            camVelocity = jumped ? Vector3.zero : Vector3.Lerp(camVelocity, moved / dt, 1f - Mathf.Exp(-12f * dt));

            // particles live in the root's space, which moves with the camera: their velocity is relative to it,
            // so the stretched billboards streak the way the eye sees them
            root.position = camPos;

            bool under = WaterSurface.IsPointUnderwater(camPos);
            if (!under)
            {
                if (wasUnder) system.Clear();
                wasUnder = false;
                return;
            }
            if (!wasUnder || jumped) Fill();
            wasUnder = true;
            Steer(camPos, dt);
        }

        void UpdateRhythm(float dt)
        {
            if (instance == null) return;

            RhythmConductor clock = rhythm != null ? rhythm : RhythmConductor.Active;
            bool playing = clock != null && clock.isActiveAndEnabled && clock.IsPlaying;
            float beat = playing ? Mathf.Clamp01(clock.BeatPulse) : 0f;
            float accent = playing ? Mathf.Clamp01(clock.AccentPulse) : 0f;
            rhythmBeat = FollowPulse(rhythmBeat, beat, dt);
            rhythmAccent = FollowPulse(rhythmAccent, accent, dt);
            rhythmPhase = Mathf.Repeat(rhythmPhase + dt * Mathf.Max(0f, accentFrequency) * (2f * Mathf.PI), 2f * Mathf.PI);

            // Offset only the rendered vertices: the particle simulation, current sampling and stretched
            // billboards still use the original drift. The bounded offset returns to zero as music fades.
            instance.SetVector(RhythmId, new Vector4(rhythmBeat * Mathf.Max(0f, beatEmission),
                rhythmAccent * Mathf.Max(0f, accentAmplitude), rhythmPhase, 0f));
            instance.SetColor(GlowId, beatGlowColour);
        }

        float FollowPulse(float current, float target, float dt)
        {
            float response = target > current ? rhythmAttackTime : rhythmReleaseTime;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, response)));
        }

        void Fill()
        {
            system.Clear();
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            system.Emit(emit, count);
            int n = system.GetParticles(buffer);
            for (int i = 0; i < n; i++)
            {
                Respawn(ref buffer[i]);
                // already part way through their life, so they don't all fade at once later
                buffer[i].remainingLifetime = buffer[i].startLifetime * Random.Range(0.1f, 1f);
            }
            system.SetParticles(buffer, n);
        }

        // Each speck drifts with the water where it is; one that leaves the view or reaches the end of its life is
        // reborn somewhere else in the view.
        void Steer(Vector3 camPos, float dt)
        {
            int n = system.GetParticles(buffer);
            if (n == 0) return;

            Transform ct = cam.transform;
            Quaternion toCam = Quaternion.Inverse(ct.rotation);
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (1f + margin);
            float tanX = tanY * cam.aspect;
            float far = viewDistance;
            float t = Time.time;
            float swell = 1f + Mathf.Max(0f, beatSizeBoost) * rhythmBeat;
            if (speckRenderer != null) speckRenderer.maxParticleSize = MaxScreenSize * swell;

            for (int i = 0; i < n; i++)
            {
                ParticleSystem.Particle p = buffer[i];
                Vector3 world = camPos + p.position;
                Vector3 local = toCam * p.position;
                if (p.remainingLifetime < 0.1f || local.z < nearDistance * 0.5f || local.z > far * 1.05f
                    || Mathf.Abs(local.x) > local.z * tanX + 0.1f || Mathf.Abs(local.y) > local.z * tanY + 0.1f
                    || !WaterSurface.IsPointUnderwater(world))
                {
                    Respawn(ref p);
                    buffer[i] = p;
                    continue;
                }

                uint seed = p.randomSeed;
                float phase = (seed % 1000u) * 0.0063f;
                Vector3 drift = new Vector3(Mathf.Sin(t * 0.7f + phase * 6.1f),
                                            0.6f * Mathf.Sin(t * 0.9f + phase * 3.7f),
                                            Mathf.Cos(t * 0.8f + phase * 5.3f)) * wobble;
                p.velocity = WaterCurrent.At(world) * flowSpeed + drift - camVelocity;

                // fade in when born, out at the end of life, near the lens and towards the far end of the view
                float age = p.startLifetime - p.remainingLifetime;
                float alpha = Mathf.Clamp01(age / fadeIn) * Mathf.Clamp01(p.remainingLifetime / 0.8f);
                alpha *= Mathf.Clamp01((local.z - nearDistance) / 0.35f);
                alpha *= Mathf.Clamp01((far - local.z) / (far * 0.3f));
                if (twinkle > 0f) alpha *= 1f - twinkle * (0.5f + 0.5f * Mathf.Sin(t * (1.5f + (seed % 7u) * 0.4f) + phase * 40f));
                Color c = Color.Lerp(colorA, colorB, (seed % 97u) / 96f);
                c.a *= alpha;
                p.startColor = c;
                p.startSize = BaseSize(seed) * swell;
                buffer[i] = p;
            }
            system.SetParticles(buffer, n);
        }

        // A random spot in the (widened) view, spread evenly through its volume, under water.
        void Respawn(ref ParticleSystem.Particle p)
        {
            Transform ct = cam.transform;
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (1f + margin);
            float tanX = tanY * cam.aspect;
            float n3 = nearDistance * nearDistance * nearDistance;
            float f3 = viewDistance * viewDistance * viewDistance;
            Vector3 offset = Vector3.zero;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                // the view widens with distance: as many specks per cubic metre near and far
                float z = Mathf.Pow(Mathf.Lerp(n3, f3, Random.value), 1f / 3f);
                offset = ct.rotation * new Vector3(Random.Range(-1f, 1f) * z * tanX, Random.Range(-1f, 1f) * z * tanY, z);
                if (WaterSurface.IsPointUnderwater(root.position + offset)) break;
            }

            p.position = offset;
            p.velocity = Vector3.zero;
            p.startLifetime = Random.Range(lifetime.x, lifetime.y);
            p.remainingLifetime = p.startLifetime;
            p.rotation = Random.Range(0f, 360f);
            p.randomSeed = (uint)Random.Range(1, int.MaxValue);
            p.startSize = BaseSize(p.randomSeed);
            Color c = colorA;
            c.a = 0f;
            p.startColor = c;
        }

        // The speck's own size, from its seed: the beat swell can scale it every frame without storing it.
        float BaseSize(uint seed)
        {
            float u = ((seed * 2654435761u) >> 8) / 16777216f;
            return Mathf.Lerp(size.x, size.y, Mathf.Pow(u, smallBias));
        }

        ParticleSystem Build()
        {
            var go = new GameObject("Specks");
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(1, count);
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = root;   // the root follows the camera without turning with it
            main.startLifetime = lifetime.y;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = false;   // placed and reborn by hand (Steer / Respawn)
            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = instance;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = stretch;
            r.lengthScale = 1f;
            r.minParticleSize = 0.0007f;   // far specks stay a pixel or so instead of flickering in and out
            r.maxParticleSize = MaxScreenSize;    // a speck right at the lens stays a speck
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            speckRenderer = r;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            ps.Play();
            return ps;
        }

        // A crisp round core with a faint halo: reads as a lit mote at any size instead of a blurry blob.
        static Texture2D BuildSpeckTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Speck (built-in)", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.42f, r));
                float halo = Mathf.Exp(-r * r * 9f) * 0.35f;
                float a = Mathf.Clamp01(core + halo) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, r)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}

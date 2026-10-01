using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// Shows the <see cref="WaterCurrent"/>: specks of sediment and thin light streaks drifting with the water around
    /// the camera, stretched along their motion so the flow direction reads at a glance. Every particle moves with the
    /// water at its own spot (plus a little wobble), so eddies and slower deep water show too. Only under water.
    /// </summary>
    public class WaterCurrentView : MonoBehaviour
    {
        [Tooltip("Soft round dot on AKI/UnderwaterParticle, additive (M_BubbleFizz).")]
        public Material material;
        [Tooltip("Half size of the box around the camera they live in (m).")]
        public Vector3 extent = new Vector3(7f, 4f, 7f);
        [Tooltip("How quickly they fade with distance (1/m); lower than the bubbles', so the flow reads further out.")]
        [Min(0f)] public float fadeDensity = 0.06f;

        [Header("Specks")]
        [Min(0)] public int specks = 500;
        public Vector2 speckSize = new Vector2(0.035f, 0.07f);
        [Tooltip("Stretch along the motion (seconds of travel).")]
        [Min(0f)] public float speckStretch = 0.06f;
        public Color speckColor = new Color(0.75f, 0.9f, 1f, 0.6f);

        [Header("Streaks")]
        [Min(0)] public int streaks = 18;
        public Vector2 streakSize = new Vector2(0.015f, 0.025f);
        [Min(0f)] public float streakStretch = 0.9f;
        public Color streakColor = new Color(0.85f, 0.95f, 1f, 0.4f);

        [Tooltip("Random drift on top of the current (m/s), so specks don't move in lockstep.")]
        [Min(0f)] public float wobble = 0.05f;

        Transform root;
        Material instance;
        ParticleSystem speckSystem;
        ParticleSystem streakSystem;
        ParticleSystem.Particle[] buffer;
        Camera cam;
        bool filled;

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
            speckSystem = Build("Specks", specks, new Vector2(4f, 8f), speckSize, speckColor, speckStretch, 1f);
            streakSystem = Build("Streaks", streaks, new Vector2(2.5f, 4.5f), streakSize, streakColor, streakStretch, 0.8f);
            buffer = new ParticleSystem.Particle[Mathf.Max(specks, streaks)];
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (instance != null) Destroy(instance);
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null || root == null) return;

            Vector3 centre = cam.transform.position;
            root.position = centre;
            bool under = WaterSurface.IsPointUnderwater(centre);
            SetEmitting(speckSystem, under);
            SetEmitting(streakSystem, under);
            if (under && !filled)
            {
                // fill the space right away instead of waiting for the emitters
                speckSystem.Emit(specks);
                streakSystem.Emit(streaks / 2);
                filled = true;
            }
            if (!under) filled = false;

            Steer(speckSystem, centre);
            Steer(streakSystem, centre);
        }

        // Each particle drifts with the water where it is; those left behind by the moving camera make room for new ones.
        void Steer(ParticleSystem ps, Vector3 centre)
        {
            int n = ps.GetParticles(buffer);
            if (n == 0) return;
            float t = Time.time;
            Vector3 reach = extent * 1.25f;
            for (int i = 0; i < n; i++)
            {
                ParticleSystem.Particle p = buffer[i];
                Vector3 pos = p.position;
                Vector3 off = pos - centre;
                if (Mathf.Abs(off.x) > reach.x || Mathf.Abs(off.y) > reach.y || Mathf.Abs(off.z) > reach.z
                    || !WaterSurface.IsPointUnderwater(pos))
                {
                    p.remainingLifetime = 0f;
                    buffer[i] = p;
                    continue;
                }

                float phase = (p.randomSeed % 1000u) * 0.0063f;
                Vector3 drift = new Vector3(Mathf.Sin(t * 0.7f + phase * 6.1f),
                                            0.6f * Mathf.Sin(t * 0.9f + phase * 3.7f),
                                            Mathf.Cos(t * 0.8f + phase * 5.3f)) * wobble;
                p.velocity = WaterCurrent.At(pos) + drift;
                buffer[i] = p;
            }
            ps.SetParticles(buffer, n);
        }

        static void SetEmitting(ParticleSystem ps, bool on)
        {
            var emission = ps.emission;
            if (emission.enabled != on) emission.enabled = on;
        }

        ParticleSystem Build(string name, int count, Vector2 life, Vector2 size, Color color, float stretch, float boxShare)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = Mathf.Max(1, count);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(color * new Color(1f, 1f, 1f, 0.5f), color);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = count / ((life.x + life.y) * 0.5f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = extent * (2f * boxShare);

            // fade in and out, so nothing pops
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = instance;
            r.renderMode = ParticleSystemRenderMode.Stretch;   // long along the flow: shows where it goes
            r.velocityScale = stretch;
            r.lengthScale = 1f;
            r.maxParticleSize = 0.02f;                          // a speck right at the lens stays a speck
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }
    }
}

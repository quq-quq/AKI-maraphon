using AKI.Water;
using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// A light wake behind a swimming fish, so its motion reads in the murky water: a faint soft ribbon from the tail
    /// and a few specks of stirred-up water swirling off it. Both get stronger the faster the fish swims
    /// (<see cref="FishAI.Velocity"/>) and die away once it stops (hit by a harpoon). Built at runtime from the material.
    /// </summary>
    [RequireComponent(typeof(FishAI))]
    public class FishTrail : MonoBehaviour
    {
        [Tooltip("AKI/UnderwaterParticle material, additive (M_BubbleFizz). Its fade settings are used.")]
        public Material material;
        [Tooltip("Where the wake starts. Empty = the back end of the fish's mesh.")]
        public Transform tail;
        [Tooltip("Fish speed (m/s) at which the wake is at full strength.")]
        [Min(0.1f)] public float fullSpeed = 2f;

        [Header("Ribbon")]
        [Tooltip("Seconds the ribbon stays behind the fish.")]
        [Min(0.05f)] public float ribbonSeconds = 0.7f;
        [Tooltip("Width right at the tail (m); it narrows to nothing.")]
        [Min(0f)] public float ribbonWidth = 0.22f;
        [ColorUsage(true, true)] public Color ribbonColor = new Color(0.7f, 0.9f, 1f, 0.14f);

        [Header("Stirred-up specks")]
        [Tooltip("Specks per metre swum at full speed.")]
        [Min(0f)] public float specksPerMetre = 14f;
        public Vector2 speckSize = new Vector2(0.008f, 0.025f);
        public Vector2 speckLifetime = new Vector2(0.6f, 1.6f);
        public Color speckColor = new Color(0.8f, 0.95f, 1f, 0.55f);

        FishAI ai;
        TrailRenderer ribbon;
        ParticleSystem specks;
        Material ribbonMaterial;
        Material speckMaterial;
        Texture2D ribbonTexture;
        float speckRate;
        float strength;

        void Awake()
        {
            ai = GetComponent<FishAI>();
            if (material == null)
            {
                Debug.LogWarning("FishTrail: no material, the fish leaves no wake.", this);
                enabled = false;
                return;
            }
            if (tail == null) tail = MakeTailPoint();

            ribbonTexture = BuildRibbonTexture();
            ribbonMaterial = new Material(material) { name = material.name + " (wake)" };
            ribbonMaterial.SetTexture("_BaseMap", ribbonTexture);
            speckMaterial = new Material(material) { name = material.name + " (wake specks)" };

            ribbon = BuildRibbon();
            specks = BuildSpecks();
        }

        void OnDestroy()
        {
            if (ribbonMaterial != null) Destroy(ribbonMaterial);
            if (speckMaterial != null) Destroy(speckMaterial);
            if (ribbonTexture != null) Destroy(ribbonTexture);
        }

        void LateUpdate()
        {
            float target = ai.IsStopped || !ai.IsUnderwater ? 0f : Mathf.Clamp01(ai.Velocity.magnitude / fullSpeed);
            // fades in and out instead of switching: a stopped fish's wake thins away
            strength = Mathf.MoveTowards(strength, target, Time.deltaTime * 2f);

            ribbon.emitting = strength > 0.02f;
            Color head = ribbonColor;
            head.a *= strength;
            Color end = head;
            end.a = 0f;
            ribbon.startColor = head;
            ribbon.endColor = end;
            ribbon.widthMultiplier = ribbonWidth * Mathf.Lerp(0.6f, 1f, strength);

            var emission = specks.emission;
            emission.rateOverDistanceMultiplier = speckRate * strength;
            WaterCurrent.Drift(specks, tail.position);   // the stirred water drifts away with the current
        }

        // The rearmost point of the fish's mesh, a little above its centre line.
        Transform MakeTailPoint()
        {
            float back = 0f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                          : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    back = Mathf.Min(back, transform.InverseTransformPoint(r.transform.TransformPoint(corner)).z);
                }
            }
            var point = new GameObject("Wake").transform;
            point.SetParent(transform, false);
            point.localPosition = new Vector3(0f, 0f, back * 0.75f);
            return point;
        }

        TrailRenderer BuildRibbon()
        {
            var go = new GameObject("Wake Ribbon");
            go.transform.SetParent(tail, false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ribbonMaterial;
            trail.time = ribbonSeconds;
            trail.minVertexDistance = 0.08f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = ribbonWidth;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            trail.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            trail.emitting = false;
            return trail;
        }

        ParticleSystem BuildSpecks()
        {
            var go = new GameObject("Wake Specks");
            go.transform.SetParent(tail, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // left behind in the water
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(speckLifetime.x, speckLifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(speckSize.x, speckSize.y);
            main.startColor = new ParticleSystem.MinMaxGradient(speckColor * new Color(1f, 1f, 1f, 0.4f), speckColor);
            main.gravityModifier = -0.01f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = specksPerMetre;
            speckRate = specksPerMetre;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            // swirled about by the tail, quickly losing the push
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 2f;
            noise.scrollSpeed = 0.6f;
            noise.damping = true;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = 3f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = speckMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            r.maxParticleSize = 0.012f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            ps.Play();
            return ps;
        }

        // Across the ribbon (v): soft gaussian edges; along it (u): a soft start right at the tail.
        static Texture2D BuildRibbonTexture()
        {
            const int w = 32, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Wake (built-in)", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float v = (y + 0.5f) / h * 2f - 1f;
                float across = Mathf.Exp(-v * v * 4f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, Mathf.Abs(v))));
                float along = Mathf.SmoothStep(0f, 1f, u / 0.05f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(across * along * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}

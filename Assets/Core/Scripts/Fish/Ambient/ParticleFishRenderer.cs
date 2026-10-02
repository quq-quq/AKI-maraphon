using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Fish
{
    /// <summary>
    /// Draws every small fish of one kind (one mesh + material) as the particles of one <see cref="ParticleSystem"/> in
    /// mesh mode. The particle system only draws: it emits nothing and moves nothing by itself, <see cref="AmbientFish"/>
    /// writes each fish's position, rotation and size into it every frame.
    /// </summary>
    public class ParticleFishRenderer
    {
        readonly ParticleSystem system;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[0];
        readonly Quaternion meshToFish;   // turns the mesh so its head is +Z and its back +Y
        readonly Material shiny;          // the fish's material with the glow and the glints

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        /// <summary>Length of the mesh from tail to head (m), to size the fish.</summary>
        public float MeshLength { get; }

        /// <param name="headAxis">Axis of the mesh the head points along.</param>
        /// <param name="backAxis">Axis of the mesh the back (dorsal fin) points along.</param>
        /// <param name="upsideDown">The fish come out belly up: turn them over.</param>
        /// <param name="glow">How much the fish shine by themselves in their own colours (0 = not at all).</param>
        /// <param name="glint">Strength of the sun's glints on the scales (0 = matt).</param>
        /// <param name="glintSmoothness">0..1: small and sharp glints near 1.</param>
        public ParticleFishRenderer(Transform parent, string name, Mesh mesh, Material material, Vector3 headAxis, Vector3 backAxis, bool upsideDown,
            float glow, float glint, float glintSmoothness)
        {
            shiny = Shine(material, glow, glint, glintSmoothness);
            Vector3 head = headAxis.normalized;
            Vector3 back = Vector3.ProjectOnPlane(backAxis, head).normalized;
            if (back.sqrMagnitude < 0.5f) back = Mathf.Abs(head.z) < 0.9f ? Vector3.forward : Vector3.up;   // back along the head: any axis across it
            if (upsideDown) back = -back;
            meshToFish = Quaternion.Inverse(Quaternion.LookRotation(head, back));
            MeshLength = Mathf.Max(0.01f, Extent(mesh.bounds, head));

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startRotation3D = true;
            main.maxParticles = 0;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.sharedMaterial = shiny;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.enableGPUInstancing = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
            system.Play();
        }

        // A copy of the fish's (URP Simple Lit) material: its own colours glow a little, so it can be told apart in the
        // deep blue, and a bright specular highlight makes the scales flash in the sunlight as the fish turns and wags.
        static Material Shine(Material source, float glow, float glint, float smoothness)
        {
            var m = new Material(source) { name = source.name + " (shiny)" };
            if (glow > 0f)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty(BaseMapId) && m.HasProperty(EmissionMapId)) m.SetTexture(EmissionMapId, m.GetTexture(BaseMapId));
                m.SetColor(EmissionColorId, Color.white * glow);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (glint > 0f)
            {
                m.EnableKeyword("_SPECULAR_COLOR");
                m.DisableKeyword("_SPECGLOSSMAP");
                m.SetColor(SpecColorId, new Color(glint, glint, glint, smoothness));
                m.SetFloat(SmoothnessId, smoothness);
            }
            return m;
        }

        static float Extent(Bounds b, Vector3 axis)
        {
            return Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))));
        }

        /// <summary>
        /// Shows <paramref name="count"/> fish: position, the way they face (head along +Z, back along +Y) and
        /// length (m) of each.
        /// </summary>
        public void Write(Vector3[] positions, Quaternion[] rotations, float[] lengths, int count)
        {
            if (particles.Length < count)
            {
                particles = new ParticleSystem.Particle[count];
                ParticleSystem.MainModule main = system.main;
                main.maxParticles = count;
            }
            for (int i = 0; i < count; i++)
            {
                ref ParticleSystem.Particle p = ref particles[i];
                p.position = positions[i];
                p.rotation3D = (rotations[i] * meshToFish).eulerAngles;
                p.startSize = lengths[i] / MeshLength;
                p.startColor = Color.white;
                p.startLifetime = 100000f;
                p.remainingLifetime = 100000f;
                p.velocity = Vector3.zero;
                p.angularVelocity3D = Vector3.zero;
            }
            system.SetParticles(particles, count);
        }
    }
}

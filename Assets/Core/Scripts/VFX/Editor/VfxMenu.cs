using System;
using System.IO;
using AKI.Water;
using AKI.Weapons;
using AKI.Weapons.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AKI.VFX.Editor
{
    /// <summary>
    /// Builds the underwater particle effects: harpoon bubbles and tuna blood (textures, materials, prefabs),
    /// and a test bench in the open scene to look at them (the speargun itself comes from <see cref="SpeargunMenu"/>).
    /// Textures and materials are only created when missing (so tweaks survive); prefabs are rebuilt every time.
    /// </summary>
    public static class VfxMenu
    {
        const string TextureDir = "Assets/Core/Visual/Textures/VFX";
        const string MaterialDir = "Assets/Core/Visual/Materials/VFX";
        const string PrefabDir = "Assets/Core/Prefabs/VFX";
        public const string HarpoonPrefabPath = PrefabDir + "/VFX_HarpoonBubbles.prefab";
        const string BloodPrefabPath = PrefabDir + "/VFX_TunaBlood.prefab";
        const string ShaderName = "AKI/UnderwaterParticle";

        // water absorbs red first (same idea as the water material's Absorption, a bit weaker)
        static readonly Vector4 Absorption = new Vector4(0.25f, 0.07f, 0.03f, 0f);

        [MenuItem("AKI/VFX/Create Underwater Effects")]
        public static void CreateEffects()
        {
            Texture2D bubbleTex = EnsureTexture("T_Bubble", BubblePixel);
            Texture2D dotTex = EnsureTexture("T_SoftDot", SoftDotPixel);
            Texture2D cloudTex = EnsureTexture("T_BloodCloud", CloudPixel);

            Material bubble = EnsureMaterial("M_Bubble", bubbleTex, additive: 0.8f, fade: 0.12f, soft: false);
            Material fizz = EnsureMaterial("M_BubbleFizz", dotTex, additive: 1f, fade: 0.15f, soft: false);
            Material cloud = EnsureMaterial("M_BloodCloud", cloudTex, additive: 0f, fade: 0.08f, soft: true);
            Material speck = EnsureMaterial("M_BloodSpeck", dotTex, additive: 0f, fade: 0.1f, soft: false);

            BuildHarpoonPrefab(bubble, fizz);
            BuildBloodPrefab(cloud, speck);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AKI/VFX/Add Harpoon Demo To Scene")]
        public static void AddDemo()
        {
            var bloodFx = AssetDatabase.LoadAssetAtPath<GameObject>(BloodPrefabPath);
            if (bloodFx == null)
            {
                CreateEffects();
                bloodFx = AssetDatabase.LoadAssetAtPath<GameObject>(BloodPrefabPath);
            }
            GameObject gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpeargunMenu.PrefabPath);
            if (gunPrefab == null) gunPrefab = SpeargunMenu.CreateSpeargun();
            if (gunPrefab == null) return;

            // in front of the main camera, a couple of metres under the water
            Camera cam = Camera.main;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            Vector3 origin = cam != null ? cam.transform.position + forward.normalized * 1.5f : Vector3.zero;
            var water = Object.FindFirstObjectByType<WaterSurface>();
            if (water != null) origin.y = Mathf.Min(origin.y, water.WaterLevel - 2.5f);

            var demo = new GameObject("VFX Demo (speargun + tuna)");
            Undo.RegisterCreatedObjectUndo(demo, "Add Harpoon VFX Demo");
            demo.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
            var bench = demo.AddComponent<VfxDemo>();

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab, demo.transform);

            // tuna stand-in: a 1.2 m capsule along its forward axis; the collider sits on the unscaled root,
            // so a harpoon stuck in it isn't skewed by the visual's scale
            var tuna = new GameObject("Tuna").transform;
            tuna.SetParent(demo.transform, false);
            var body = tuna.gameObject.AddComponent<CapsuleCollider>();
            body.direction = 2;
            body.radius = 0.175f;
            body.height = 1.2f;
            AddVisual(PrimitiveType.Capsule, tuna, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.35f, 0.6f, 0.35f));
            PrefabUtility.InstantiatePrefab(bloodFx, tuna);

            bench.gun = gun.GetComponent<Speargun>();
            bench.tuna = tuna;
            Selection.activeGameObject = demo;
        }

        static void AddVisual(PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
        }

        // ------------------------------------------------------------------ harpoon

        static void BuildHarpoonPrefab(Material bubble, Material fizz)
        {
            var root = new GameObject("VFX_HarpoonBubbles");
            var fx = root.AddComponent<HarpoonBubbles>();

            // Shot: a cloud of bubbles thrown forward out of the muzzle ...
            ParticleSystem shot = NewSystem(root.transform, "ShotBubbles", bubble, 250);
            {
                var main = shot.main;
                main.duration = 0.3f;
                main.startLifetime = Range(1.2f, 3f);
                main.startSpeed = Range(0.5f, 5f);
                main.startSize = Range(0.012f, 0.07f);
                main.gravityModifier = Range(-0.08f, -0.16f);   // rising
                Bursts(shot, new ParticleSystem.Burst(0f, 70, 110), new ParticleSystem.Burst(0.05f, 20, 35));
                Cone(shot, 30f, 0.04f);
                Drag(shot, 3f);
                Wobble(shot, 0.25f, 1.6f);
                PopSize(shot);
                FadeOverLife(shot, 0.05f, 0.85f);
            }

            // ... and a white flash of fine fizz, stretched by its speed
            ParticleSystem flash = NewSystem(root.transform, "ShotFizz", fizz, 400);
            {
                var main = flash.main;
                main.duration = 0.2f;
                main.startLifetime = Range(0.25f, 1.1f);
                main.startSpeed = Range(1f, 9f);
                main.startSize = Range(0.006f, 0.025f);
                main.startColor = new Color(0.88f, 0.96f, 1f, 0.7f);
                main.gravityModifier = -0.05f;
                Bursts(flash, new ParticleSystem.Burst(0f, 180, 240));
                Cone(flash, 18f, 0.03f);
                Drag(flash, 6f);
                Wobble(flash, 0.15f, 3f);
                Stretch(flash, 0.03f);
                FadeOverLife(flash, 0.02f, 0.4f, 0.8f);
            }

            // Flight: the bubble streak behind the harpoon (emitted by distance, so it follows the path) ...
            ParticleSystem trail = NewSystem(root.transform, "TrailBubbles", bubble, 1500, looping: true);
            {
                var main = trail.main;
                main.startLifetime = Range(1.5f, 3.5f);
                main.startSpeed = Range(0f, 0.25f);
                main.startSize = Range(0.008f, 0.035f);
                main.gravityModifier = Range(-0.06f, -0.14f);
                var emission = trail.emission;
                emission.rateOverDistance = 35f;
                emission.enabled = false;   // HarpoonBubbles switches it on in flight
                Sphere(trail, 0.025f);
                InheritVelocity(trail, 0.08f);   // dragged along a little behind the harpoon
                Drag(trail, 3f);
                Wobble(trail, 0.2f, 2f);
                PopSize(trail);
                FadeOverLife(trail, 0.05f, 0.85f);
            }

            // ... with a dense line of tiny fizz in it: the bright stripe that quickly dissolves
            ParticleSystem streak = NewSystem(root.transform, "TrailFizz", fizz, 3000, looping: true);
            {
                var main = streak.main;
                main.startLifetime = Range(0.35f, 1.2f);
                main.startSpeed = Range(0f, 0.15f);
                main.startSize = Range(0.006f, 0.02f);
                main.startColor = new Color(0.85f, 0.95f, 1f, 0.55f);
                main.gravityModifier = -0.03f;
                var emission = streak.emission;
                emission.rateOverDistance = 140f;
                emission.enabled = false;
                Sphere(streak, 0.012f);
                InheritVelocity(streak, 0.15f);
                Drag(streak, 4f);
                Wobble(streak, 0.08f, 3f);
                FadeOverLife(streak, 0.03f, 0.3f, 0.7f);
            }

            fx.fireBurst = new[] { shot, flash };
            fx.trail = new[] { trail, streak };
            SavePrefab(root, HarpoonPrefabPath);
        }

        // ------------------------------------------------------------------ blood

        static void BuildBloodPrefab(Material cloud, Material speck)
        {
            var root = new GameObject("VFX_TunaBlood");
            var fx = root.AddComponent<TunaBlood>();

            Color bright = new Color(0.45f, 0.02f, 0.03f, 0.85f);
            Color dark = new Color(0.26f, 0.01f, 0.02f, 0.9f);

            // Hit: a puff of blood out of the wound that spreads and slowly dissolves (sinks a little: blood is heavier)
            ParticleSystem puff = NewSystem(root.transform, "HitCloud", cloud, 100);
            {
                var main = puff.main;
                main.duration = 0.5f;
                main.startLifetime = Range(3.5f, 7f);
                main.startSpeed = Range(0.2f, 1.2f);
                main.startSize = Range(0.15f, 0.35f);
                main.startColor = new ParticleSystem.MinMaxGradient(bright, dark);
                main.gravityModifier = Range(0.002f, 0.01f);
                Bursts(puff, new ParticleSystem.Burst(0f, 10, 16), new ParticleSystem.Burst(0.12f, 4, 8));
                Cone(puff, 50f, 0.04f);
                Drag(puff, 1.5f);
                Wobble(puff, 0.12f, 0.6f, 0.2f);
                Spin(puff, 0.4f);
                SizeOverLife(puff, 3f, 0f, 0.35f, 0.3f, 0.8f, 1f, 1f);
                FadeOverLife(puff, 0.03f, 0.5f, 0.7f);
                Sorted(puff);
            }

            // dark specks spurting out
            ParticleSystem specks = NewSystem(root.transform, "HitSpecks", speck, 120);
            {
                var main = specks.main;
                main.duration = 0.3f;
                main.startLifetime = Range(0.6f, 1.6f);
                main.startSpeed = Range(1f, 3.5f);
                main.startSize = Range(0.015f, 0.045f);
                main.startColor = new Color(0.3f, 0.01f, 0.015f, 0.9f);
                main.gravityModifier = 0.02f;
                Bursts(specks, new ParticleSystem.Burst(0f, 30, 45));
                Cone(specks, 35f, 0.02f);
                Drag(specks, 4f);
                Stretch(specks, 0.06f);
                FadeOverLife(specks, 0.02f, 0.6f);
            }

            // Bleeding: blood keeps coming out of the wound while the fish swims (TunaBlood fades it out)
            ParticleSystem bleed = NewSystem(root.transform, "BleedTrail", cloud, 400, looping: true);
            {
                var main = bleed.main;
                main.startLifetime = Range(3f, 6f);
                main.startSpeed = Range(0.02f, 0.2f);
                main.startSize = Range(0.08f, 0.18f);
                main.startColor = new ParticleSystem.MinMaxGradient(bright * new Color(1f, 1f, 1f, 0.65f), dark * new Color(1f, 1f, 1f, 0.6f));
                main.gravityModifier = 0.003f;
                var emission = bleed.emission;
                emission.rateOverTime = 3f;
                emission.rateOverDistance = 5f;
                emission.enabled = false;
                Sphere(bleed, 0.03f);
                Drag(bleed, 1f);
                Wobble(bleed, 0.1f, 0.5f, 0.2f);
                Spin(bleed, 0.3f);
                SizeOverLife(bleed, 3.5f, 0f, 0.3f, 0.4f, 0.8f, 1f, 1f);
                FadeOverLife(bleed, 0.08f, 0.5f, 0.75f);
                Sorted(bleed);
            }

            fx.hitBurst = new[] { puff, specks };
            fx.bleedTrail = bleed;
            SavePrefab(root, BloodPrefabPath);
        }

        // ------------------------------------------------------------------ particle system helpers

        static ParticleSystem NewSystem(Transform parent, string name, Material material, int maxParticles, bool looping = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = looping;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // bubbles / blood stay in the water, not on the object
            main.scalingMode = ParticleSystemScalingMode.Shape;           // a scaled harpoon / fish model doesn't resize the particles
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.startRotation = Range(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        static ParticleSystem.MinMaxCurve Range(float min, float max) => new ParticleSystem.MinMaxCurve(min, max);

        static void Bursts(ParticleSystem ps, params ParticleSystem.Burst[] bursts)
        {
            ps.emission.SetBursts(bursts);
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;   // opens along +Z: the harpoon's / spurt's forward
            shape.angle = angle;
            shape.radius = radius;
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        // water drag: the particles quickly lose the speed they were thrown with
        static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.dampen = 0f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        static void Wobble(ParticleSystem ps, float strength, float frequency, float scroll = 0.5f)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = scroll;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        static void InheritVelocity(ParticleSystem ps, float amount)
        {
            var inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = amount;
        }

        static void Spin(ParticleSystem ps, float radiansPerSecond)
        {
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = Range(-radiansPerSecond, radiansPerSecond);
        }

        static void Stretch(ParticleSystem ps, float velocityScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = 1f;
        }

        static void Sorted(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().sortMode = ParticleSystemSortMode.Distance;
        }

        // a bubble swells a little after it is born and pops at the end
        static void PopSize(ParticleSystem ps)
        {
            SizeOverLife(ps, 1f, 0f, 0.6f, 0.08f, 1f, 0.92f, 1.08f, 1f, 0f);
        }

        static void SizeOverLife(ParticleSystem ps, float multiplier, params float[] timeValue)
        {
            var keys = new Keyframe[timeValue.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(multiplier, curve);
        }

        // alpha: 0 -> 1 by fadeIn, 1 -> holdAlpha by holdUntil, -> 0 at the end of life
        static void FadeOverLife(ParticleSystem ps, float fadeIn, float holdUntil, float holdAlpha = 1f)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn),
                    new GradientAlphaKey(holdAlpha, holdUntil), new GradientAlphaKey(0f, 1f)
                });

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("Prefab created: " + path);
            return saved;
        }

        // ------------------------------------------------------------------ materials

        static Material EnsureMaterial(string name, Texture texture, float additive, float fade, bool soft)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Directory.CreateDirectory(MaterialDir);
            material = new Material(Shader.Find(ShaderName)) { name = name };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Additive", additive);
            material.SetFloat("_FadeDensity", fade);
            material.SetVector("_Absorption", Absorption);
            // soft particles read the depth texture: only where it matters (big clouds cutting into the fish)
            material.SetFloat("_Soft", soft ? 1f : 0f);
            if (soft) material.EnableKeyword("_SOFT_PARTICLES"); else material.DisableKeyword("_SOFT_PARTICLES");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ------------------------------------------------------------------ textures (u, v in -1..1, white + alpha)

        static Texture2D EnsureTexture(string name, Func<float, float, float> alpha, int size = 128)
        {
            string path = TextureDir + "/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(u, v)));
            }
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TextureDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // thin film with a bright rim and a window highlight in the top left
        static float BubblePixel(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float rim = Mathf.Exp(-Sq((r - 0.84f) / 0.08f));
            float film = 0.1f + 0.15f * r * r;
            float highlight = Mathf.Exp(-Sq(Dist(u, v, -0.32f, 0.36f) / 0.13f));
            float glint = 0.35f * Mathf.Exp(-Sq(Dist(u, v, 0.38f, -0.4f) / 0.09f));
            float a = Mathf.Max(film, rim) + highlight + glint;
            return a * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1f, r)));
        }

        static float SoftDotPixel(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
        }

        // wispy cloud: fractal noise with a ragged round edge
        static float CloudPixel(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float n = 0f, amplitude = 0.5f, frequency = 2.2f;
            for (int i = 0; i < 4; i++)
            {
                n += amplitude * Mathf.PerlinNoise(u * frequency + 13.1f * i + 5f, v * frequency - 7.7f * i + 30f);
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }
            float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, r + (n - 0.47f) * 0.6f));
            return edge * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(n * 1.1f));
        }

        static float Sq(float x) => x * x;

        static float Dist(float u, float v, float cu, float cv) => Mathf.Sqrt(Sq(u - cu) + Sq(v - cv));
    }
}

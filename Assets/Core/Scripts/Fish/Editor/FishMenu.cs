using System.IO;
using AKI.Player;
using AKI.Weapons;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AKI.Fish.Editor
{
    /// <summary>
    /// Builds the tuna prefabs and puts a <see cref="FishSpawner"/> into the open scene, aimed at the player's camera.
    ///   Tuna:          the animated fish model (Models/Fish) with the AC_Fish animator: Swim, and Struggle when hit.
    ///   Tuna_Greybox:  capsule-and-cubes stand-in.
    /// The collider sits on the unscaled root (a harpoon stuck into a stretched parent would be skewed), the meshes are
    /// children without colliders. No blood on them: that effect hooks into <see cref="FishCollision.onHit"/>.
    /// Materials and the animator are only created when missing (so tweaks survive); prefabs are rebuilt every time.
    /// </summary>
    public static class FishMenu
    {
        const string PrefabDir = "Assets/Core/Prefabs/Fish";
        public const string TunaPrefabPath = PrefabDir + "/Tuna.prefab";
        public const string GreyboxPrefabPath = PrefabDir + "/Tuna_Greybox.prefab";
        const string MaterialDir = "Assets/Core/Visual/Materials/Fish";
        const string ModelPath = "Assets/Core/Visual/Models/Fish/Fish.fbx";
        const string ModelPrefabPath = "Assets/Core/Visual/Models/Fish/Prefabs/Fish.prefab";
        const string ControllerPath = "Assets/Core/Animations/AC_Fish.controller";

        [MenuItem("AKI/Fish/Create Tuna")]
        public static void CreateTunaMenu() => CreateTuna();

        [MenuItem("AKI/Fish/Create Greybox Tuna")]
        public static void CreateGreyboxMenu() => CreateGreybox();

        [MenuItem("AKI/Fish/Add Fish Spawner To Scene")]
        public static void AddSpawner()
        {
            // the animated tuna; the greybox one only if the model is missing
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TunaPrefabPath);
            FishAI fish = prefab != null ? prefab.GetComponent<FishAI>() : CreateTuna();
            if (fish == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxPrefabPath);
                fish = prefab != null ? prefab.GetComponent<FishAI>() : CreateGreybox();
            }
            if (fish == null) return;

            var spawner = Object.FindFirstObjectByType<FishSpawner>();
            if (spawner == null)
            {
                var go = new GameObject("Fish Spawner");
                Undo.RegisterCreatedObjectUndo(go, "Add Fish Spawner");
                spawner = go.AddComponent<FishSpawner>();
            }
            else
            {
                Undo.RecordObject(spawner, "Set Up Fish Spawner");
            }

            spawner.fishPrefab = fish;
            spawner.player = FindPlayerCamera();
            if (spawner.player == null) Debug.LogWarning("No FirstPersonSwimController in the scene: the fish will circle the main camera.");
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            Selection.activeGameObject = spawner.gameObject;
        }

        // ------------------------------------------------------------------ animated tuna

        public static FishAI CreateTuna()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (model == null)
            {
                Debug.LogError($"Fish model prefab not found at {ModelPrefabPath}");
                return null;
            }
            AnimatorController controller = EnsureController();
            if (controller == null) return null;

            var root = new GameObject("Tuna");

            // the model, kept linked to its prefab so changes to it come through; head along +Z like the fish root
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = "Model";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Animator animator = instance.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // body collider fitted to the mesh (fins stick out of it, the harpoon goes through those)
            Bounds bounds = MeshBounds(root.transform);
            CapsuleCollider collider = AddBody(root, bounds.center, Mathf.Min(bounds.extents.x, bounds.extents.y) * 0.7f, bounds.size.z * 0.92f);

            FishAI ai = AddFishComponents(root, collider);
            ai.wiggleDegrees = 0f;                 // the swim animation wags the tail now
            ai.bodyHalfHeight = bounds.extents.y;  // up to the tip of the dorsal fin
            root.AddComponent<FishAnimation>().animator = animator;

            return Save(root, TunaPrefabPath);
        }

        // Swim loops; the Hit trigger switches to the Struggle loop (FishAnimation slows it down to a stop).
        static AnimatorController EnsureController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null) return controller;

            AnimationClip swim = null, struggle = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                if (clip.name == "Fish_Swim") swim = clip;
                else if (clip.name == "Fish_Struggle") struggle = clip;
            }
            if (swim == null || struggle == null)
            {
                Debug.LogError($"Fish_Swim / Fish_Struggle clips not found in {ModelPath}");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(FishAnimation.HitTrigger, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState swimState = machine.AddState("Swim");
            swimState.motion = swim;
            AnimatorState struggleState = machine.AddState("Struggle");
            struggleState.motion = struggle;
            machine.defaultState = swimState;

            AnimatorStateTransition hit = swimState.AddTransition(struggleState);
            hit.AddCondition(AnimatorConditionMode.If, 0f, FishAnimation.HitTrigger);
            hit.hasExitTime = false;
            hit.duration = 0.1f;
            return controller;
        }

        static Bounds MeshBounds(Transform root)
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(0.4f, 0.4f, 1.3f));   // fallback: a ~1.3 m tuna
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;   // root sits at the origin unrotated: world = root space
                if (any) bounds.Encapsulate(b);
                else bounds = b;
                any = true;
            }
            return bounds;
        }

        // ------------------------------------------------------------------ greybox tuna

        public static FishAI CreateGreybox()
        {
            Material skin = EnsureMaterial("M_Greybox_Tuna", new Color(0.24f, 0.31f, 0.42f));
            Material fin = EnsureMaterial("M_Greybox_TunaFin", new Color(0.11f, 0.13f, 0.18f));

            var root = new GameObject("Tuna_Greybox");
            Transform t = root.transform;

            // ~1.25 m tuna along +Z
            CapsuleCollider collider = AddBody(root, Vector3.zero, 0.2f, 1.3f);

            // body: taller than wide, like a tuna
            AddVisual(PrimitiveType.Capsule, t, "Body", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.3f, 0.62f, 0.38f), skin);
            // crescent tail, swept back
            AddVisual(PrimitiveType.Cube, t, "Tail Upper", new Vector3(0f, 0.12f, -0.7f), Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.025f, 0.28f, 0.1f), fin);
            AddVisual(PrimitiveType.Cube, t, "Tail Lower", new Vector3(0f, -0.12f, -0.7f), Quaternion.Euler(40f, 0f, 0f), new Vector3(0.025f, 0.28f, 0.1f), fin);
            AddVisual(PrimitiveType.Cube, t, "Dorsal Fin", new Vector3(0f, 0.2f, 0.05f), Quaternion.Euler(-30f, 0f, 0f), new Vector3(0.02f, 0.18f, 0.14f), fin);
            AddVisual(PrimitiveType.Cube, t, "Fin Right", new Vector3(0.15f, -0.03f, 0.18f), Quaternion.Euler(0f, 25f, -15f), new Vector3(0.18f, 0.02f, 0.08f), fin);
            AddVisual(PrimitiveType.Cube, t, "Fin Left", new Vector3(-0.15f, -0.03f, 0.18f), Quaternion.Euler(0f, -25f, 15f), new Vector3(0.18f, 0.02f, 0.08f), fin);
            AddVisual(PrimitiveType.Sphere, t, "Eye Right", new Vector3(0.115f, 0.05f, 0.47f), Quaternion.identity, Vector3.one * 0.05f, fin);
            AddVisual(PrimitiveType.Sphere, t, "Eye Left", new Vector3(-0.115f, 0.05f, 0.47f), Quaternion.identity, Vector3.one * 0.05f, fin);

            AddFishComponents(root, collider);
            return Save(root, GreyboxPrefabPath);
        }

        // ------------------------------------------------------------------ shared

        static CapsuleCollider AddBody(GameObject root, Vector3 center, float radius, float length)
        {
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.center = center;
            collider.radius = radius;
            collider.height = length;
            var rigidbody = root.AddComponent<Rigidbody>();   // moved by script: kinematic
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            return collider;
        }

        static FishAI AddFishComponents(GameObject root, Collider body)
        {
            root.AddComponent<Catchable>().dissolveShader = Shader.Find("AKI/DissolveLit");
            FishAI ai = root.AddComponent<FishAI>();
            root.AddComponent<FishCollision>().body = body;
            return ai;
        }

        static FishAI Save(GameObject root, string path)
        {
            Directory.CreateDirectory(PrefabDir);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"Fish saved to {path}");
            return saved != null ? saved.GetComponent<FishAI>() : null;
        }

        static Transform FindPlayerCamera()
        {
            var swimmer = Object.FindFirstObjectByType<FirstPersonSwimController>();
            if (swimmer == null) return null;
            Camera cam = swimmer.playerCamera != null ? swimmer.playerCamera : swimmer.GetComponentInChildren<Camera>();
            return cam != null ? cam.transform : swimmer.transform;
        }

        static void AddVisual(PrimitiveType type, Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Directory.CreateDirectory(MaterialDir);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}

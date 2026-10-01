using System.IO;
using AKI.Player;
using AKI.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AKI.Fish.Editor
{
    /// <summary>
    /// Builds the greybox tuna prefab and puts a <see cref="FishSpawner"/> into the open scene, aimed at the player's camera.
    /// The collider sits on the unscaled root (a harpoon stuck into a stretched parent would be skewed), the meshes are
    /// children without colliders. No blood on it: that effect hooks into <see cref="FishCollision.onHit"/>.
    /// Materials are only created when missing (so tweaks survive); the prefab is rebuilt every time.
    /// </summary>
    public static class FishMenu
    {
        const string PrefabDir = "Assets/Core/Prefabs/Fish";
        public const string TunaPrefabPath = PrefabDir + "/Tuna_Greybox.prefab";
        const string MaterialDir = "Assets/Core/Visual/Materials/Fish";

        [MenuItem("AKI/Fish/Create Greybox Tuna")]
        public static void CreateTunaMenu() => CreateTuna();

        [MenuItem("AKI/Fish/Add Fish Spawner To Scene")]
        public static void AddSpawner()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TunaPrefabPath);
            FishAI fish = prefab != null ? prefab.GetComponent<FishAI>() : CreateTuna();
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

        public static FishAI CreateTuna()
        {
            Material skin = EnsureMaterial("M_Greybox_Tuna", new Color(0.24f, 0.31f, 0.42f));
            Material fin = EnsureMaterial("M_Greybox_TunaFin", new Color(0.11f, 0.13f, 0.18f));

            var root = new GameObject("Tuna_Greybox");
            Transform t = root.transform;

            // ~1.25 m tuna along +Z
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.radius = 0.2f;
            collider.height = 1.3f;
            var rigidbody = root.AddComponent<Rigidbody>();   // moved by script: kinematic
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

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

            root.AddComponent<Catchable>().dissolveShader = Shader.Find("AKI/DissolveLit");
            root.AddComponent<FishAI>();
            root.AddComponent<FishCollision>().body = collider;

            Directory.CreateDirectory(PrefabDir);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TunaPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"Greybox tuna saved to {TunaPrefabPath}");
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

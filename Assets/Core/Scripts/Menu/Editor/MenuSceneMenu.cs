using System.IO;
using System.Linq;
using AKI.Water;
using AKI.Water.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AKI.Menu.Editor
{
    /// <summary>
    /// Builds the main menu test scene: the ocean over the demo seabed, the grandfather in his boat
    /// (Fishman_Cutscene with <see cref="FishmanCutscene"/> and <see cref="BoatBob"/>) over deep water, the menu /
    /// cutscene camera, the first-person player (inactive until the hand-over) and the <see cref="MainMenuController"/>.
    /// </summary>
    public static class MenuSceneMenu
    {
        const string ScenePath = "Assets/Core/Scenes/TestScenes/MenuTest.unity";
        const string CutscenePrefabPath = "Assets/Core/Visual/Models/Fishman/Prefabs/Fishman_Cutscene.prefab";
        const string CutsceneModelPath = "Assets/Core/Visual/Models/Fishman/Fishman_Cutscene.fbx";
        const string PlayerPrefabPath = "Assets/Core/Prefabs/Player.prefab";
        const string SeabedMeshPath = "Assets/Core/Visual/Models/DemoSeabed.asset";
        const string SandMaterialPath = "Assets/Core/Visual/Materials/Water/M_DemoSand.mat";

        // the boat floats over deep water (the demo seabed is ~11 m down at the sea side)
        static readonly Vector3 BoatPosition = new Vector3(0f, 0f, -50f);

        [MenuItem("AKI/Menu/Create Menu Test Scene")]
        public static void CreateScene()
        {
            var cutscenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CutscenePrefabPath);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Object[] modelAssets = AssetDatabase.LoadAllAssetsAtPath(CutsceneModelPath);
            AnimationClip sit = modelAssets.OfType<AnimationClip>().FirstOrDefault(c => c.name == "Sit_Stand");
            AnimationClip dive = modelAssets.OfType<AnimationClip>().FirstOrDefault(c => c.name == "Run_To_Dive");
            if (cutscenePrefab == null || playerPrefab == null || sit == null || dive == null)
            {
                Debug.LogError($"Menu scene: missing {CutscenePrefabPath}, {PlayerPrefabPath} or the Sit_Stand / Run_To_Dive clips in {CutsceneModelPath}");
                return;
            }
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Menu test scene", $"{ScenePath} already exists. Rebuild it?", "Rebuild", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Sun (as in the water demo)
            var sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            RenderSettings.sun = sun;

            // Menu / cutscene camera: also goes under the water, so it gets the lens effects too.
            // A starting shot from the side of the boat; it is meant to be placed by hand (the menu never moves it).
            Camera cam = Camera.main;
            cam.name = "Cutscene Camera";
            Vector3 camPosition = BoatPosition + new Vector3(6f, 2.5f, -5f);
            cam.transform.SetPositionAndRotation(camPosition, Quaternion.LookRotation(BoatPosition + Vector3.up - camPosition));
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            cam.gameObject.AddComponent<WaterCameraEffects>();
            WaterMenu.InstallLensFeature();

            // Ocean and seabed
            GameObject water = WaterMenu.CreateWater();
            water.name = "Water";
            var seabed = new GameObject("Seabed");
            seabed.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(SeabedMeshPath);
            seabed.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SandMaterialPath);
            seabed.AddComponent<MeshCollider>();

            // The grandfather in his boat
            var cutsceneObject = (GameObject)PrefabUtility.InstantiatePrefab(cutscenePrefab);
            cutsceneObject.transform.SetPositionAndRotation(BoatPosition, Quaternion.identity);
            var cutscene = cutsceneObject.AddComponent<FishmanCutscene>();
            cutscene.actor = cutsceneObject.GetComponentInChildren<Animator>();
            cutscene.sitStand = sit;
            cutscene.runToDive = dive;
            var bob = cutsceneObject.AddComponent<BoatBob>();
            Transform boat = cutsceneObject.transform.Find("Boat");
            if (boat != null) bob.hull = boat.GetComponent<Renderer>();
            // no sea inside the hull (the lid is built at start from the hull mesh: Read/Write on its import)
            var mask = cutsceneObject.AddComponent<BoatWaterMask>();
            mask.hull = boat != null ? boat.GetComponent<MeshFilter>() : null;
            mask.maskShader = Shader.Find("AKI/WaterMask");

            // The first-person player: takes over under the water
            var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            playerObject.transform.SetPositionAndRotation(BoatPosition + Vector3.down * 3f, Quaternion.identity);
            playerObject.SetActive(false);

            // The flow
            var menu = new GameObject("Main Menu").AddComponent<MainMenuController>();
            menu.cutscene = cutscene;
            menu.cutsceneCamera = cam;
            menu.player = playerObject;
            menu.playerCamera = playerObject.GetComponentInChildren<Camera>(true);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = menu.gameObject;
            Debug.Log($"Menu test scene saved to {ScenePath}");
        }
    }
}

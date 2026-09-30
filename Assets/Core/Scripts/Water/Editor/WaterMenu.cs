using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AKI.Water.Editor
{
    /// <summary>Menu helpers: create the water materials / water object, and a small demo scene to look at it.</summary>
    public static class WaterMenu
    {
        const string MaterialDir = "Assets/Core/Visual/Materials/Water";
        const string PcMaterialPath = MaterialDir + "/M_Water_PC.mat";
        const string MobileMaterialPath = MaterialDir + "/M_Water_Mobile.mat";
        const string PrefabPath = "Assets/Core/Prefabs/Water.prefab";
        const string DemoScenePath = "Assets/Core/Scenes/TestScenes/WaterTest.unity";
        const string HeightComputePath = "Assets/Core/Visual/Shaders/Water/WaterHeight.compute";
        const string PlayerPrefabPath = "Assets/Core/Prefabs/Player.prefab";

        [MenuItem("GameObject/AKI/Ocean Water", false, 10)]
        public static GameObject CreateWater()
        {
            EnsureMaterials(out Material pc, out Material mobile);

            var go = new GameObject("Ocean Water");
            Undo.RegisterCreatedObjectUndo(go, "Create Ocean Water");
            var surface = go.AddComponent<WaterSurface>();
            surface.material = pc;
            surface.fallbackMaterial = mobile;
            surface.underwaterShader = Shader.Find("AKI/WaterUnderwater");
            surface.heightCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(HeightComputePath);
            surface.Refresh();
            Selection.activeGameObject = go;
            return go;
        }

        [MenuItem("AKI/Water/Create Materials + Prefab")]
        public static void CreateMaterialsAndPrefab()
        {
            EnsureMaterials(out Material pc, out Material mobile);

            var go = new GameObject("Water");
            var surface = go.AddComponent<WaterSurface>();
            surface.material = pc;
            surface.fallbackMaterial = mobile;
            surface.underwaterShader = Shader.Find("AKI/WaterUnderwater");
            surface.heightCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(HeightComputePath);
            surface.enabled = false;   // OnDisable removes the runtime-only mesh / underwater child before saving
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("Water materials and prefab created: " + PrefabPath);
        }

        /// <summary>Adds the WaterLensFeature to every URP renderer in the project (once).</summary>
        [MenuItem("AKI/Water/Install Lens Effect")]
        public static void InstallLensFeature()
        {
            Shader shader = Shader.Find("Hidden/AKI/WaterLens");
            FieldInfo mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))   // never touch package assets
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null || data.rendererFeatures.Exists(f => f is WaterLensFeature)) continue;

                var feature = ScriptableObject.CreateInstance<WaterLensFeature>();
                feature.name = "AKI Water Lens";
                feature.shader = shader;
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);

                // keep URP's feature map in sync, the same way the renderer inspector does
                if (mapField != null && mapField.GetValue(data) is List<long> map &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                    map.Add(localId);

                data.SetDirty();
                EditorUtility.SetDirty(data);
                Debug.Log("Water lens effect added to " + path);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AKI/Water/Reset Materials To Shader Defaults")]
        public static void ResetMaterialsToDefaults()
        {
            EnsureMaterials(out Material pc, out Material mobile);
            foreach (Material m in new[] { pc, mobile })
            {
                var fresh = new Material(m.shader);
                m.CopyPropertiesFromMaterial(fresh);   // also copies the toggle keywords, so they are re-applied below
                Object.DestroyImmediate(fresh);
                if (m == mobile) ConfigureMobile(m); else ConfigurePc(m);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        public static void EnsureMaterials(out Material pc, out Material mobile)
        {
            Directory.CreateDirectory(MaterialDir);
            Shader shader = Shader.Find("AKI/Water");

            pc = AssetDatabase.LoadAssetAtPath<Material>(PcMaterialPath);
            if (pc == null)
            {
                pc = new Material(shader) { name = "M_Water_PC" };
                ConfigurePc(pc);
                AssetDatabase.CreateAsset(pc, PcMaterialPath);
            }

            mobile = AssetDatabase.LoadAssetAtPath<Material>(MobileMaterialPath);
            if (mobile == null)
            {
                mobile = new Material(shader) { name = "M_Water_Mobile" };
                ConfigureMobile(mobile);
                AssetDatabase.CreateAsset(mobile, MobileMaterialPath);
            }
        }

        static void SetToggle(Material m, string property, string keyword, bool on)
        {
            m.SetFloat(property, on ? 1f : 0f);
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        // PC: everything on (needs Depth + Opaque texture in the URP asset).
        static void ConfigurePc(Material m)
        {
            SetToggle(m, "_SceneTextures", "_SCENE_TEXTURES", true);
            SetToggle(m, "_WaveDetail", "_WAVE_DETAIL", true);
            SetToggle(m, "_FoamOn", "_FOAM", true);
            SetToggle(m, "_CausticsOn", "_CAUSTICS", true);
            SetToggle(m, "_GodRaysOn", "_GODRAYS", true);
        }

        // Mobile: analytic colour only, no scene textures, no shafts / caustics / foam.
        static void ConfigureMobile(Material m)
        {
            SetToggle(m, "_SceneTextures", "_SCENE_TEXTURES", false);
            SetToggle(m, "_WaveDetail", "_WAVE_DETAIL", false);
            SetToggle(m, "_FoamOn", "_FOAM", false);
            SetToggle(m, "_CausticsOn", "_CAUSTICS", false);
            SetToggle(m, "_GodRaysOn", "_GODRAYS", false);
        }

        // ------------------------------------------------------------------ demo scene

        [MenuItem("AKI/Water/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            EnsureMaterials(out Material pc, out Material mobile);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Sun
            var sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            RenderSettings.sun = sun;

            // Camera
            var cam = Camera.main;
            cam.transform.position = new Vector3(0f, 7f, -32f);
            cam.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            cam.farClipPlane = 1500f;
            cam.gameObject.AddComponent<WaterListenerAudio>();   // muffles sound + exposes IsUnderwater for the player
            cam.gameObject.AddComponent<WaterCameraEffects>();   // wet lens + waterline
            CreatePlayer(cam);
            InstallLensFeature();

            // Sea bed / beach
            var seabed = new GameObject("Seabed");
            seabed.AddComponent<MeshFilter>().sharedMesh = BuildSeabedMesh();
            var sandMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_DemoSand" };
            sandMat.SetColor("_BaseColor", new Color(0.78f, 0.68f, 0.50f));
            sandMat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(sandMat, MaterialDir + "/M_DemoSand.mat");
            seabed.AddComponent<MeshRenderer>().sharedMaterial = sandMat;
            seabed.AddComponent<MeshCollider>();

            // Rocks + a floating crate to see shore foam and light shafts being interrupted
            var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_DemoRock" };
            rockMat.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.30f));
            rockMat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(rockMat, MaterialDir + "/M_DemoRock.mat");
            PlaceRock("Rock A", new Vector3(-9f, -1.2f, 6f), new Vector3(5f, 4.5f, 4.5f), rockMat);
            PlaceRock("Rock B", new Vector3(11f, -2.5f, 2f), new Vector3(6f, 7f, 5f), rockMat);
            PlaceRock("Rock C", new Vector3(2f, -3f, 12f), new Vector3(3f, 3f, 3f), rockMat);
            PlaceRock("Rock D", new Vector3(-16f, -4f, -2f), new Vector3(7f, 9f, 6f), rockMat);

            // Water
            var water = new GameObject("Water");
            var surface = water.AddComponent<WaterSurface>();
            surface.material = pc;
            surface.fallbackMaterial = mobile;
            surface.underwaterShader = Shader.Find("AKI/WaterUnderwater");
            surface.heightCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(HeightComputePath);
            surface.Refresh();

            Directory.CreateDirectory(Path.GetDirectoryName(DemoScenePath));
            EditorSceneManager.SaveScene(scene, DemoScenePath);
            AssetDatabase.SaveAssets();
        }

        // First-person swimmer standing on the beach, looking at the sea. The main camera becomes its eyes.
        static void CreatePlayer(Camera cam)
        {
            var player = new GameObject("Player");
            player.transform.SetPositionAndRotation(new Vector3(0f, 3f, 30f), Quaternion.Euler(0f, 180f, 0f));

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.65f, 0f);

            cam.transform.SetParent(pivot, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.08f;
            cam.fieldOfView = 72f;

            var swimmer = player.AddComponent<AKI.Player.FirstPersonSwimController>();
            swimmer.cameraPivot = pivot;
            swimmer.playerCamera = cam;
            swimmer.inputActions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");

            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, PlayerPrefabPath, InteractionMode.AutomatedAction);
        }

        static void PlaceRock(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Mesh BuildSeabedMesh()
        {
            const int quads = 200;
            const float size = 240f;
            int verts = quads + 1;
            var pos = new Vector3[verts * verts];
            var uv = new Vector2[verts * verts];

            for (int z = 0; z < verts; z++)
            for (int x = 0; x < verts; x++)
            {
                float wx = (x / (float)quads - 0.5f) * size;
                float wz = (z / (float)quads - 0.5f) * size;

                // deep at the camera side, rising to a beach at +z
                float slope = Mathf.Lerp(-11f, 2.2f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-30f, 42f, wz)));
                float dunes = (Mathf.PerlinNoise(wx * 0.05f + 20f, wz * 0.05f) - 0.5f) * 3.0f
                            + (Mathf.PerlinNoise(wx * 0.3f, wz * 0.3f) - 0.5f) * 0.3f
                            + Mathf.Sin(wx * 0.8f + wz * 0.25f) * 0.05f;
                pos[z * verts + x] = new Vector3(wx, slope + dunes, wz);
                uv[z * verts + x] = new Vector2(wx, wz) * 0.1f;
            }

            var tris = new int[quads * quads * 6];
            int i = 0;
            for (int z = 0; z < quads; z++)
            for (int x = 0; x < quads; x++)
            {
                int a = z * verts + x, b = a + 1, c = a + verts, d = c + 1;
                tris[i++] = a; tris[i++] = c; tris[i++] = b;
                tris[i++] = b; tris[i++] = c; tris[i++] = d;
            }

            var mesh = new Mesh { name = "DemoSeabed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = pos;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            const string meshPath = "Assets/Core/Visual/Models/DemoSeabed.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(meshPath));
            AssetDatabase.CreateAsset(mesh, meshPath);
            return mesh;
        }
    }
}

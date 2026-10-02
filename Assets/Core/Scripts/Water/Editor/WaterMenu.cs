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
        const string OceanComputePath = "Assets/Core/Visual/Shaders/Water/OceanFFT.compute";
        const string PlayerPrefabPath = "Assets/Core/Prefabs/Player.prefab";
        const string CurrentMaterialPath = "Assets/Core/Visual/Materials/VFX/M_BubbleFizz.mat";

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
            AddOcean(surface);
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
            AddOcean(surface);
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
            SetToggle(m, "_FFTWaves", "_FFT_WAVES", true);
            m.SetFloat("_CrestFoamThreshold", 0.75f);   // FFT foam: share of breaking crests
            m.SetFloat("_SpecularIntensity", 0.25f);
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
            AddOcean(surface);
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
            AKI.Weapons.Editor.SpeargunMenu.AttachToPlayer(player);

            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, PlayerPrefabPath, InteractionMode.AutomatedAction);
        }

        // FFT open ocean: real wind-wave spectrum, grid that reaches the horizon
        static void AddOcean(WaterSurface surface)
        {
            surface.size = 6000f;
            surface.expandingGrid = true;
            surface.detailRadius = 60f;
            var ocean = surface.GetComponent<OceanFFT>();
            if (ocean == null) ocean = surface.gameObject.AddComponent<OceanFFT>();
            ocean.fftCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(OceanComputePath);
            AddCurrent(surface.gameObject);
        }

        /// <summary>The turning wind, the sea's current and the drifting specks that show it (once).</summary>
        [MenuItem("AKI/Water/Add Current To Selected Water")]
        static void AddCurrentToSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
                if (go.GetComponent<WaterSurface>() != null) AddCurrent(go);
        }

        public static void AddCurrent(GameObject water)
        {
            if (water.GetComponent<Wind>() == null) Undo.AddComponent<Wind>(water);
            if (water.GetComponent<WaterCurrent>() == null) Undo.AddComponent<WaterCurrent>(water);
            var view = water.GetComponent<WaterCurrentView>();
            if (view == null) view = Undo.AddComponent<WaterCurrentView>(water);
            if (view.material == null) view.material = AssetDatabase.LoadAssetAtPath<Material>(CurrentMaterialPath);
        }

        static void PlaceRock(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            // a sphere collider stays round (radius of the largest axis) and would stick out of a stretched rock
            Object.DestroyImmediate(go.GetComponent<SphereCollider>());
            go.AddComponent<MeshCollider>();
        }

        [MenuItem("AKI/Water/Rebuild Demo Seabed")]
        static void RebuildSeabed() => BuildSeabedMesh();

        static Mesh BuildSeabedMesh()
        {
            const int quads = 200;
            const float size = 240f;
            const float shelfStart = 80f;     // distance from the centre where the island starts sinking into the sea
            const float edgeDepth = -14f;     // height of the plate's rim, well under the waves
            const float skirtDepth = -60f;    // walls hanging from the rim, so nobody can look under the plate
            int verts = quads + 1;
            int skirtVerts = quads * 4 * 4;
            var pos = new Vector3[verts * verts + skirtVerts];
            var uv = new Vector2[pos.Length];

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
                // the beach must not run off the edge of the plate: seen from the sea outside, an open edge above
                // the water shows the culled underside (sky with floating slivers of sand)
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shelfStart, size * 0.5f, Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz))));
                float h = Mathf.Lerp(slope + dunes, edgeDepth + dunes * 0.3f, rim);
                pos[z * verts + x] = new Vector3(wx, h, wz);
                uv[z * verts + x] = new Vector2(wx, wz) * 0.1f;
            }

            var tris = new int[quads * quads * 6 + quads * 4 * 6];
            int i = 0;
            for (int z = 0; z < quads; z++)
            for (int x = 0; x < quads; x++)
            {
                int a = z * verts + x, b = a + 1, c = a + verts, d = c + 1;
                tris[i++] = a; tris[i++] = c; tris[i++] = b;
                tris[i++] = b; tris[i++] = c; tris[i++] = d;
            }

            // skirt: walk the rim counter-clockwise (seen from above) and hang an outward-facing wall from it.
            // Own vertices, so the wall's hard normals do not bend the shading of the sea bed.
            int s = verts * verts;
            for (int side = 0; side < 4; side++)
            for (int k = 0; k < quads; k++)
            {
                int i0 = RimIndex(side, k, quads), i1 = RimIndex(side, k + 1, quads);
                Vector3 p0 = pos[i0], p1 = pos[i1];
                pos[s] = p0; pos[s + 1] = new Vector3(p0.x, skirtDepth, p0.z);
                pos[s + 2] = p1; pos[s + 3] = new Vector3(p1.x, skirtDepth, p1.z);
                float u0 = (side * quads + k) * size / quads * 0.1f, u1 = u0 + size / quads * 0.1f;
                uv[s] = new Vector2(u0, p0.y * 0.1f); uv[s + 1] = new Vector2(u0, skirtDepth * 0.1f);
                uv[s + 2] = new Vector2(u1, p1.y * 0.1f); uv[s + 3] = new Vector2(u1, skirtDepth * 0.1f);
                tris[i++] = s; tris[i++] = s + 2; tris[i++] = s + 1;
                tris[i++] = s + 2; tris[i++] = s + 3; tris[i++] = s + 1;
                s += 4;
            }

            const string meshPath = "Assets/Core/Visual/Models/DemoSeabed.asset";
            // update an existing asset in place, so the scenes that use it keep their reference
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "DemoSeabed" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = pos;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (isNew)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(meshPath));
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
                // mesh colliders cook their data once: hand them the new shape
                foreach (var col in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                    if (col.sharedMesh == mesh) { col.sharedMesh = null; col.sharedMesh = mesh; }
            }
            return mesh;
        }

        // grid index of the k-th rim vertex on a side, walking the rim counter-clockwise seen from above
        static int RimIndex(int side, int k, int quads)
        {
            int verts = quads + 1;
            switch (side)
            {
                case 0: return k;                                  // -z edge, x rising
                case 1: return k * verts + quads;                  // +x edge, z rising
                case 2: return quads * verts + (quads - k);        // +z edge, x falling
                default: return (quads - k) * verts;               // -x edge, z falling
            }
        }
    }
}

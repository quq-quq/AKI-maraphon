using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

namespace OptimizedSeagulls.Editor
{
    public static class SeagullSceneBuilder
    {
        const string Root = "Assets/Core/Visual/Models/Seagull/";
        public static string Build()
        {
            if (GameObject.Find("Seagulls_Sky") != null)
                throw new InvalidOperationException("Seagulls_Sky already exists; preserve existing routes.");
            foreach (string folder in new[] { "Animations", "Paths" })
                if (!AssetDatabase.IsValidFolder(Root + folder)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), folder);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Root + "Models/Seagull.fbx").OfType<AnimationClip>().ToArray();
            var flap = clips.Single(c => c.name == "Seagull_Flap");
            var idle = clips.Single(c => c.name == "Seagull_Idle");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "Animations/SeagullFlight.controller");
            controller.AddParameter("Flapping", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var glideState = machine.AddState("Glide", new Vector3(220, 0)); glideState.motion = idle;
            var flapState = machine.AddState("Flap", new Vector3(480, 0)); flapState.motion = flap;
            machine.defaultState = glideState;
            var toFlap = glideState.AddTransition(flapState); toFlap.hasExitTime = false; toFlap.hasFixedDuration = true;
            toFlap.duration = .3f; toFlap.AddCondition(AnimatorConditionMode.If, 0, "Flapping");
            var toGlide = flapState.AddTransition(glideState); toGlide.hasExitTime = false; toGlide.hasFixedDuration = true;
            toGlide.duration = .4f; toGlide.AddCondition(AnimatorConditionMode.IfNot, 0, "Flapping");
            var bird = new GameObject("Seagull");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/Seagull.fbx"));
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Visual"; visual.transform.SetParent(bird.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Seagull_BaseMap.mat");
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.updateWhenOffscreen = false;
                renderer.quality = SkinQuality.Bone1;
                // Include all wing-beat extents for reliable animation culling.
                renderer.localBounds = new Bounds(Vector3.zero, new Vector3(2.2f, 2.2f, 2.2f));
            }
            bird.AddComponent<SeagullFlight>().wingAnimator = animator;
            var birdPrefab = PrefabUtility.SaveAsPrefabAsset(bird, Root + "Prefabs/Seagull.prefab");
            UnityEngine.Object.DestroyImmediate(bird);
            var sky = new GameObject("Seagulls_Sky"); Undo.RegisterCreatedObjectUndo(sky, "Create five seagull flight routes");
            for (int i = 0; i < 5; i++)
            {
                var spline = ScriptableObject.CreateInstance<BakedFlightSpline>();
                spline.controlPoints = new Vector3[12];
                float rx = 86 + i * 15, rz = 72 + i * 11, cx = (i - 2) * 13, cz = (i % 2 == 0 ? -18 : 22);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI * 2 / 12 * (i % 2 == 0 ? 1 : -1);
                    float wobble = 1 + .12f * Mathf.Sin(a * 3 + i);
                    spline.controlPoints[k] = new Vector3(cx + Mathf.Cos(a) * rx * wobble,
                        22 + i * 4 + Mathf.Sin(a * 2 + i) * 3, cz + Mathf.Sin(a) * rz * wobble);
                }
                spline.Bake(); AssetDatabase.CreateAsset(spline, Root + "Paths/SkyRoute_" + (i + 1).ToString("D2") + ".asset");
                var routeObject = new GameObject("FlightRoute_" + (i + 1).ToString("D2")); routeObject.transform.SetParent(sky.transform, false);
                var route = routeObject.AddComponent<FlightSplineRoute>(); route.spline = spline;
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(birdPrefab); actor.name = "Seagull_" + (i + 1).ToString("D2");
                actor.transform.SetParent(routeObject.transform, false); actor.transform.localScale = Vector3.one * 1.15f;
                var flight = actor.GetComponent<SeagullFlight>(); flight.route = route;
                flight.speed = 9 + i * .8f; flight.startingProgress = .09f + i * .177f;
                flight.flapSeconds = 2.5f + i * .15f; flight.glideSeconds = 8 + i * 1.1f; flight.animationPhase = i * 2.4f;
                flight.ResetFlight(); PrefabUtility.RecordPrefabInstancePropertyModifications(flight);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
            }
            PrefabUtility.SaveAsPrefabAsset(sky, Root + "Prefabs/Seagulls_Sky.prefab");
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(sky.scene); EditorSceneManager.SaveScene(sky.scene);
            Selection.activeGameObject = sky;
            return "Created Seagull prefab/controller, five baked 512-sample closed splines and five flying birds in " + sky.scene.path;
        }
    }
}

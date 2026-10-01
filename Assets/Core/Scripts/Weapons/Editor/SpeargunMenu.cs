using System.IO;
using AKI.Player;
using AKI.VFX;
using AKI.VFX.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AKI.Weapons.Editor
{
    /// <summary>
    /// Builds the speargun from its model (gun prefab, flying arrow prefab, animator) and puts it in the player's hands.
    /// The model only animates the gun (the ejector kicks back), the arrow stays in place: the shot arrow is a
    /// separate projectile copy, launched from the tip of the loaded one.
    /// The animator is only created when missing (so tweaks survive); prefabs are rebuilt every time.
    /// </summary>
    public static class SpeargunMenu
    {
        public const string PrefabPath = "Assets/Core/Prefabs/Speargun.prefab";
        const string ArrowPrefabPath = "Assets/Core/Prefabs/HarpoonArrow.prefab";
        const string ModelPath = "Assets/Core/Visual/Models/Harpoon/harpoon_with_anim.fbx";
        const string ControllerPath = "Assets/Core/Animations/AC_Speargun.controller";
        const string PlayerPrefabPath = "Assets/Core/Prefabs/Player.prefab";

        [MenuItem("AKI/Weapons/Create Speargun")]
        public static void CreateSpeargunMenu() => CreateSpeargun();

        /// <summary>Builds Speargun.prefab (and the arrow, the animator and the bubbles if missing). Returns the prefab.</summary>
        public static GameObject CreateSpeargun()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogWarning("Speargun model not found: " + ModelPath);
                return null;
            }

            var bubblesFx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxMenu.HarpoonPrefabPath);
            if (bubblesFx == null)
            {
                VfxMenu.CreateEffects();
                bubblesFx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxMenu.HarpoonPrefabPath);
            }

            var root = new GameObject("Speargun");
            var gun = root.AddComponent<Speargun>();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            Transform arrow = FindDeep(instance.transform, "harpoon_arrow");
            Transform body = FindDeep(instance.transform, "harpoon");
            if (arrow == null || MeshOf(arrow) == null)
            {
                Object.DestroyImmediate(root);
                Debug.LogWarning("No 'harpoon_arrow' mesh in " + ModelPath);
                return null;
            }

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            Pose tip = ArrowTip(arrow, body);
            muzzle.SetPositionAndRotation(tip.position, tip.rotation);

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = EnsureController();
            animator.applyRootMotion = false;

            gun.animator = animator;
            gun.loadedArrow = arrow.gameObject;
            gun.muzzle = muzzle;
            gun.projectilePrefab = BuildArrowPrefab(arrow, muzzle, bubblesFx);
            GameObject saved = SavePrefab(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return saved;
        }

        [MenuItem("AKI/Weapons/Give Speargun To Player Prefab")]
        public static void GiveToPlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && CreateSpeargun() == null) return;

            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (AttachToPlayer(player))
                {
                    PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
                    Debug.Log("Speargun given to " + PlayerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        /// <summary>
        /// Puts the speargun in front of the player's camera and adds <see cref="PlayerSpeargun"/> (once).
        /// Returns false if the player has no camera or the gun can't be built.
        /// </summary>
        public static bool AttachToPlayer(GameObject player)
        {
            var swimmer = player.GetComponent<FirstPersonSwimController>();
            Camera cam = swimmer != null && swimmer.playerCamera != null ? swimmer.playerCamera : player.GetComponentInChildren<Camera>();
            if (swimmer == null || cam == null)
            {
                Debug.LogWarning("Speargun: " + player.name + " needs a FirstPersonSwimController with a camera.");
                return false;
            }

            var holder = player.GetComponent<PlayerSpeargun>();
            if (holder == null) holder = player.AddComponent<PlayerSpeargun>();
            if (holder.gun != null) return true;

            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (gunPrefab == null) gunPrefab = CreateSpeargun();
            if (gunPrefab == null) return false;

            var gunGo = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab, cam.transform);
            gunGo.transform.SetLocalPositionAndRotation(holder.holdPosition, Quaternion.Euler(holder.holdRotation));
            holder.gun = gunGo.GetComponent<Speargun>();
            holder.gun.owner = player.transform;
            return true;
        }

        // Flying copy of the arrow: origin at the tip, +Z = flight direction, bubbles at the tip.
        static HarpoonProjectile BuildArrowPrefab(Transform arrow, Transform muzzle, GameObject bubblesFx)
        {
            var root = new GameObject("HarpoonArrow");
            root.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);

            GameObject mesh = Object.Instantiate(arrow.gameObject, arrow.position, arrow.rotation);
            mesh.name = "Arrow";
            mesh.transform.SetParent(root.transform, true);

            var projectile = root.AddComponent<HarpoonProjectile>();
            if (bubblesFx != null)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(bubblesFx, root.transform);
                projectile.bubbles = fx.GetComponent<HarpoonBubbles>();
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return SavePrefab(root, ArrowPrefabPath).GetComponent<HarpoonProjectile>();
        }

        // Idle loops; the Shoot trigger plays the shot once (from any state, so fast shots restart it) and returns to idle.
        static AnimatorController EnsureController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null) return controller;

            AnimationClip idle = null, shoot = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                if (clip.name == "Harpoon_Idle") idle = clip;
                else if (clip.name == "Harpoon_Shoot") shoot = clip;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = machine.AddState("Idle");
            idleState.motion = idle;
            AnimatorState shootState = machine.AddState("Shoot");
            shootState.motion = shoot;
            machine.defaultState = idleState;

            AnimatorStateTransition fire = machine.AddAnyStateTransition(shootState);
            fire.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            fire.hasExitTime = false;
            fire.duration = 0f;
            fire.canTransitionToSelf = true;

            AnimatorStateTransition back = shootState.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.duration = 0.05f;
            return controller;
        }

        // The end of the arrow's long axis that points away from the gun body, facing out of the gun.
        static Pose ArrowTip(Transform arrow, Transform body)
        {
            Bounds b = MeshOf(arrow).bounds;
            Vector3 extents = b.extents;
            int axis = extents.x >= extents.y && extents.x >= extents.z ? 0 : extents.y >= extents.z ? 1 : 2;
            Vector3 half = Vector3.zero;
            half[axis] = extents[axis];
            Vector3 endA = arrow.TransformPoint(b.center + half);
            Vector3 endB = arrow.TransformPoint(b.center - half);

            Mesh bodyMesh = body != null ? MeshOf(body) : null;
            Vector3 bodyCentre = bodyMesh != null ? body.TransformPoint(bodyMesh.bounds.center) : arrow.parent.position;
            bool aIsTip = (endA - bodyCentre).sqrMagnitude >= (endB - bodyCentre).sqrMagnitude;
            Vector3 tip = aIsTip ? endA : endB;
            Vector3 tail = aIsTip ? endB : endA;
            return new Pose(tip, Quaternion.LookRotation(tip - tail, Vector3.up));
        }

        // Meshes with blend shapes come in as skinned meshes.
        static Mesh MeshOf(Transform t)
        {
            if (t.TryGetComponent(out SkinnedMeshRenderer skinned)) return skinned.sharedMesh;
            return t.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("Prefab created: " + path);
            return saved;
        }
    }
}

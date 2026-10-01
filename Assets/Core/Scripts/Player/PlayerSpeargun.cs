using AKI.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AKI.Player
{
    /// <summary>
    /// The player's speargun: held in front of the camera, fires on Attack (left mouse / gamepad) towards whatever is
    /// under the screen centre. The gun lags a little behind quick turns and kicks back on the shot.
    /// Uses the swim controller's input actions, so it is enabled and disabled together with it.
    /// </summary>
    [RequireComponent(typeof(FirstPersonSwimController))]
    public class PlayerSpeargun : MonoBehaviour
    {
        public Speargun gun;
        [Tooltip("Where the gun sits relative to the camera.")]
        public Vector3 holdPosition = new Vector3(0.14f, -0.16f, 0.7f);
        public Vector3 holdRotation = Vector3.zero;
        [Tooltip("How far ahead to look for the aim point under the crosshair.")]
        [Min(1f)] public float aimDistance = 40f;
        public LayerMask aimMask = Physics.DefaultRaycastLayers;

        [Header("Feel")]
        [Tooltip("Metres the gun jumps back on a shot.")]
        public float recoilKick = 0.07f;
        [Tooltip("Degrees the muzzle jumps up on a shot.")]
        public float recoilPitch = 7f;
        [Tooltip("1/s: how quickly the gun settles after a shot.")]
        [Min(0.1f)] public float recoilRecovery = 9f;
        [Tooltip("Seconds of lag behind turning the view (0 = rigid).")]
        [Range(0f, 0.1f)] public float sway = 0.03f;
        [Tooltip("Max sway angle (degrees).")]
        public float maxSway = 6f;

        FirstPersonSwimController swimmer;
        Camera cam;
        InputAction attackAction;
        bool wasLocked;
        float recoil;
        Vector3 swayAngles;
        Quaternion lastCamRotation;

        void Awake()
        {
            swimmer = GetComponent<FirstPersonSwimController>();
        }

        void Start()
        {
            cam = swimmer.playerCamera;
            attackAction = swimmer.Actions != null ? swimmer.Actions.FindActionMap("Player", true).FindAction("Attack") : null;
            if (attackAction == null) Debug.LogWarning("PlayerSpeargun: no 'Attack' action in the Player map.", this);

            if (gun != null)
            {
                gun.owner = transform;   // own arrows never hit the player
                if (cam != null && gun.transform.parent != cam.transform) gun.transform.SetParent(cam.transform, false);
            }
            if (cam != null) lastCamRotation = cam.transform.rotation;
            PlaceGun(0f);
        }

        void Update()
        {
            // the click that locks the cursor again must not fire
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool canShoot = locked && wasLocked && swimmer.State != FirstPersonSwimController.MoveState.Climbing;
            wasLocked = locked;

            if (canShoot && gun != null && attackAction != null && attackAction.WasPressedThisFrame())
                Fire();
        }

        public void Fire()
        {
            if (gun == null || cam == null) return;
            if (gun.Shoot(AimPoint()) != null) recoil = 1f;
        }

        // First thing under the screen centre that isn't the player; far point along the view if nothing.
        Vector3 AimPoint()
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 point = ray.GetPoint(aimDistance);
            float best = aimDistance;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, aimDistance, aimMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || hit.transform.IsChildOf(transform)) continue;
                best = hit.distance;
                point = hit.point;
            }
            return point;
        }

        void LateUpdate()
        {
            PlaceGun(Time.deltaTime);
        }

        void PlaceGun(float dt)
        {
            if (gun == null || cam == null) return;

            // lag behind turning: the view's angular speed, held against
            Quaternion camRotation = cam.transform.rotation;
            Vector3 target = Vector3.zero;
            if (dt > 0f)
            {
                (Quaternion.Inverse(lastCamRotation) * camRotation).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                if (!float.IsNaN(axis.x)) target = Vector3.ClampMagnitude(-axis * (angle / dt * sway), maxSway);
                swayAngles = Vector3.Lerp(swayAngles, target, 1f - Mathf.Exp(-dt * 12f));
                recoil *= Mathf.Exp(-dt * recoilRecovery);
            }
            lastCamRotation = camRotation;

            Transform t = gun.transform;
            t.localPosition = holdPosition + Vector3.back * (recoilKick * recoil);
            t.localRotation = Quaternion.Euler(holdRotation) * Quaternion.Euler(swayAngles) * Quaternion.Euler(-recoilPitch * recoil, 0f, 0f);
        }
    }
}

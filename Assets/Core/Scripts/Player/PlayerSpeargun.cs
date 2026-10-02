using AKI.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AKI.Player
{
    /// <summary>
    /// The player's speargun: held in front of the camera, fires on Attack (left mouse / gamepad) towards whatever is
    /// under the screen centre. Hold Aim (right mouse / left trigger) to bring it up: the arrow lines up with the
    /// centre of the view, the view zooms in a little and turning gets steadier.
    /// The gun lags a little behind quick turns. On the shot it kicks back and up on a spring, twisting a little
    /// differently every time, and the view kicks up and shakes (<see cref="CameraShake"/> on the camera).
    /// Uses the swim controller's input actions, so it is enabled and disabled together with it.
    /// </summary>
    [RequireComponent(typeof(FirstPersonSwimController))]
    public class PlayerSpeargun : MonoBehaviour
    {
        public Speargun gun;
        [Tooltip("Where the gun sits relative to the camera (hip).")]
        public Vector3 holdPosition = new Vector3(0.14f, -0.16f, 0.7f);
        public Vector3 holdRotation = Vector3.zero;
        [Tooltip("Size of the gun in the hands (the shot arrow takes the same size).")]
        [Min(0.1f)] public float gunScale = 1.35f;
        [Tooltip("How far ahead to look for the aim point under the crosshair.")]
        [Min(1f)] public float aimDistance = 40f;
        public LayerMask aimMask = Physics.DefaultRaycastLayers;

        [Header("Aiming down the arrow")]
        [Tooltip("Where the arrow tip sits in front of the eye while aiming (camera space).")]
        public Vector3 aimTipPosition = new Vector3(0f, -0.05f, 0.95f);
        [Tooltip("Seconds to bring the gun up.")]
        [Min(0.01f)] public float aimTime = 0.18f;
        [Tooltip("Field of view while aiming, as a share of the normal one.")]
        [Range(0.4f, 1f)] public float aimZoom = 0.8f;
        [Tooltip("Look sensitivity while aiming, as a share of the normal one.")]
        [Range(0.1f, 1f)] public float aimLookScale = 0.6f;
        [Tooltip("Sway and recoil while aiming, as a share of the hip ones.")]
        [Range(0f, 1f)] public float aimSteadiness = 0.35f;

        [Header("Recoil")]
        [Tooltip("Metres the gun jumps back on a shot.")]
        public float recoilKick = 0.07f;
        [Tooltip("Degrees the muzzle jumps up on a shot.")]
        public float recoilPitch = 7f;
        [Tooltip("Random twist on top (degrees of yaw and roll; the side jump is a share of the kick): every shot is a bit different.")]
        public float recoilRandom = 3f;
        [Tooltip("How fast the gun springs back after a shot (rad/s of the spring).")]
        [Min(0.1f)] public float recoilRecovery = 16f;
        [Tooltip("1 = settles without swinging past, less = it bounces a little before it settles.")]
        [Range(0.2f, 1.5f)] public float recoilDamping = 0.55f;

        [Header("View on the shot")]
        [Tooltip("Degrees the view kicks up (it swings back by itself).")]
        public float viewKick = 2.2f;
        [Tooltip("Random sideways part of the view kick (degrees).")]
        public float viewKickRandom = 0.8f;
        [Tooltip("Degrees of the short shake on the shot.")]
        public float viewShake = 0.7f;
        [Min(0.01f)] public float shakeSeconds = 0.3f;
        [Tooltip("The field of view jumps out by this share on a shot and snaps back.")]
        [Range(0f, 0.1f)] public float fovPunch = 0.03f;
        [Header("Feel")]
        [Tooltip("Seconds of lag behind turning the view (0 = rigid).")]
        [Range(0f, 0.1f)] public float sway = 0.03f;
        [Tooltip("Max sway angle (degrees).")]
        public float maxSway = 6f;

        FirstPersonSwimController swimmer;
        Camera cam;
        InputAction attackAction;
        InputAction aimAction;
        CameraShake cameraShake;
        GameplayCameraFX cameraFx;
        float fovKick;
        bool wasLocked;
        float aim;
        // the gun's recoil spring: offset (m) and angles (degrees) from its pose, and their speeds
        Vector3 recoilOffset, recoilOffsetVelocity;
        Vector3 recoilAngles, recoilAnglesVelocity;
        Vector3 swayAngles;
        Quaternion lastCamRotation;

        public bool IsAiming { get; private set; }

        /// <summary>The trigger was pulled, whether or not an arrow left the gun (not loaded, off the beat...).</summary>
        public static event System.Action TriggerPulled;

        /// <summary>0 = at the hip, 1 = fully aimed.</summary>
        public float Aim01 => aim;

        void Awake()
        {
            swimmer = GetComponent<FirstPersonSwimController>();
        }

        void Start()
        {
            cam = swimmer.playerCamera;
            InputActionMap map = swimmer.Actions != null ? swimmer.Actions.FindActionMap("Player", true) : null;
            attackAction = map?.FindAction("Attack");
            aimAction = map?.FindAction("Aim");
            if (attackAction == null) Debug.LogWarning("PlayerSpeargun: no 'Attack' action in the Player map.", this);
            if (aimAction == null) Debug.LogWarning("PlayerSpeargun: no 'Aim' action in the Player map.", this);

            if (gun != null)
            {
                gun.owner = transform;   // own arrows never hit the player
                if (cam != null && gun.transform.parent != cam.transform) gun.transform.SetParent(cam.transform, false);
            }
            if (cam != null)
            {
                lastCamRotation = cam.transform.rotation;
                cameraShake = cam.GetComponent<CameraShake>();
                if (cameraShake == null) cameraShake = cam.gameObject.AddComponent<CameraShake>();
                cameraFx = cam.GetComponentInChildren<GameplayCameraFX>();
            }
            PlaceGun(0f);
        }

        void OnDisable()
        {
            IsAiming = false;
            aim = 0f;
            if (swimmer != null)
            {
                swimmer.FovScale = 1f;
                swimmer.LookScale = 1f;
            }
        }

        void Update()
        {
            // the click that locks the cursor again must not fire
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool canUse = locked && wasLocked && swimmer.State != FirstPersonSwimController.MoveState.Climbing;
            wasLocked = locked;

            IsAiming = canUse && gun != null && aimAction != null && aimAction.IsPressed();
            aim = Mathf.MoveTowards(aim, IsAiming ? 1f : 0f, Time.deltaTime / aimTime);
            float s = Mathf.SmoothStep(0f, 1f, aim);
            fovKick *= Mathf.Exp(-18f * Time.deltaTime);
            swimmer.FovScale = Mathf.Lerp(1f, aimZoom, s) * (1f + fovKick);
            swimmer.LookScale = Mathf.Lerp(1f, aimLookScale, s);

            if (canUse && gun != null && attackAction != null && attackAction.WasPressedThisFrame())
            {
                TriggerPulled?.Invoke();
                Fire();
            }
        }

        public void Fire()
        {
            if (gun == null || cam == null) return;
            if (gun.Shoot(AimPoint()) == null) return;

            // aimed shots are steadier
            float steady = Mathf.Lerp(1f, aimSteadiness, Mathf.SmoothStep(0f, 1f, aim));
            const float peak = 0.52f;   // a spring kicked from rest peaks at about this share of v0 / spring
            float toSpeed = recoilRecovery / peak;
            Vector3 kick = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.05f, 0.25f), -1f) * recoilKick;
            Vector3 twist = new Vector3(-recoilPitch * Random.Range(0.8f, 1.15f),
                                        Random.Range(-recoilRandom, recoilRandom),
                                        Random.Range(-recoilRandom, recoilRandom));
            recoilOffsetVelocity += kick * (toSpeed * steady);
            recoilAnglesVelocity += twist * (toSpeed * steady);

            if (cameraShake != null)
            {
                cameraShake.Kick(new Vector3(-viewKick, Random.Range(-viewKickRandom, viewKickRandom), Random.Range(-viewKickRandom, viewKickRandom)) * steady);
                cameraShake.Shake(viewShake * steady, shakeSeconds);
            }
            fovKick = fovPunch * steady;
            if (cameraFx != null) cameraFx.ShotPunch(Mathf.Lerp(0.6f, 1f, steady));
        }

        // a damped spring pulling x back to zero
        void Spring(ref Vector3 x, ref Vector3 v, float dt)
        {
            float w = recoilRecovery;
            v += (-w * w * x - 2f * recoilDamping * w * v) * dt;
            x += v * dt;
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
            float s = Mathf.SmoothStep(0f, 1f, aim);
            float shake = Mathf.Lerp(1f, aimSteadiness, s);

            // lag behind turning: the view's angular speed, held against
            Quaternion camRotation = cam.transform.rotation;
            if (dt > 0f)
            {
                Vector3 target = Vector3.zero;
                (Quaternion.Inverse(lastCamRotation) * camRotation).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                if (!float.IsNaN(axis.x)) target = Vector3.ClampMagnitude(-axis * (angle / dt * sway), maxSway);
                swayAngles = Vector3.Lerp(swayAngles, target, 1f - Mathf.Exp(-dt * 12f));
                Spring(ref recoilOffset, ref recoilOffsetVelocity, dt);
                Spring(ref recoilAngles, ref recoilAnglesVelocity, dt);
            }
            lastCamRotation = camRotation;

            // hip pose, or the pose that puts the arrow on the line of sight
            Vector3 position = holdPosition;
            Quaternion rotation = Quaternion.Euler(holdRotation);
            if (s > 0f && gun.muzzle != null)
            {
                Quaternion aimRotation = Quaternion.Inverse(LocalToGun(gun.muzzle, out Vector3 muzzleInGun));
                Vector3 aimPosition = aimTipPosition - aimRotation * muzzleInGun;
                position = Vector3.Lerp(position, aimPosition, s);
                rotation = Quaternion.Slerp(rotation, aimRotation, s);
            }

            Transform t = gun.transform;
            t.localScale = Vector3.one * gunScale;
            t.localPosition = position + recoilOffset;
            t.localRotation = rotation * Quaternion.Euler(swayAngles * shake) * Quaternion.Euler(recoilAngles);
        }

        // Pose of a gun child relative to the gun root (the muzzle may sit under other children).
        Quaternion LocalToGun(Transform child, out Vector3 position)
        {
            Transform root = gun.transform;
            position = Vector3.Scale(root.InverseTransformPoint(child.position), root.localScale);   // in the gun's parent units
            return Quaternion.Inverse(root.rotation) * child.rotation;
        }
    }
}

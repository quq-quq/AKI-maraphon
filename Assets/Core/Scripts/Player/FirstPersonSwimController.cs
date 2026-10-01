using AKI.Water;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace AKI.Player
{
    /// <summary>
    /// First-person controller that walks on land and swims in <see cref="WaterSurface"/> water.
    ///
    /// Land:        walk / sprint / jump, head bob, footsteps, landing dip.
    /// Shallows:    wading - slower the deeper the water.
    /// Surface:     floats with the head just above the real waves (GPU wave height via <see cref="WaterProbe"/>),
    ///              bobs and rolls with them, gets pushed a little by their slope. Jump = hop out / climb a ledge.
    /// Under water: full 3D swimming in the look direction, Jump = up, Crouch/Ctrl = down, Sprint = fast swim.
    ///              The water's own movement (<see cref="WaterCurrent"/>) carries you along.
    ///              Thrust comes in rhythmic strokes with glide in between, momentum carries you.
    ///              Look down and swim (or press Crouch) at the surface to dive.
    ///
    /// Everything that sounds or shows (splashes, strokes, breath...) is exposed as UnityEvents.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonSwimController : MonoBehaviour
    {
        public enum MoveState { Grounded, Airborne, Wading, SurfaceSwimming, Underwater, Climbing }

        [Header("References")]
        [Tooltip("Pitch pivot at eye height; the camera is its child.")]
        public Transform cameraPivot;
        public Camera playerCamera;
        [Tooltip("Uses the Player map: Move, Look, Jump, Crouch, Sprint (+ Attack / Aim for the speargun).")]
        public InputActionAsset inputActions;

        [Header("Look")]
        public float mouseSensitivity = 0.09f;
        public float gamepadLookSpeed = 170f;
        [Range(0f, 0.1f)] public float lookSmoothing = 0.015f;
        [Range(60f, 89f)] public float maxPitch = 88f;

        [Header("Walking")]
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 7f;
        public float groundAcceleration = 55f;
        public float airAcceleration = 9f;
        public float jumpHeight = 1.15f;
        public float gravity = 22f;
        public float eyeHeight = 1.65f;
        public float stepLength = 1.9f;

        [Header("Swimming")]
        public float swimSpeed = 3.4f;
        public float fastSwimSpeed = 6.2f;
        [Tooltip("1/s: how quickly a stroke brings you up to speed.")]
        public float swimAcceleration = 4f;
        [Tooltip("1/s: how quickly you glide to a stop without input.")]
        public float waterDrag = 1.4f;
        [Tooltip("Strokes per second at normal speed.")]
        public float strokeRate = 1.1f;
        [Range(0f, 1f), Tooltip("0 = constant thrust, 1 = strong pulses with glide in between.")]
        public float strokePulse = 0.55f;
        [Tooltip("Height above the feet where the water has to reach before you swim instead of wade.")]
        public float chestHeight = 1.25f;
        [Tooltip("How far the eyes stay above the waves while floating.")]
        public float surfaceEyeHeight = 0.85f;
        [Tooltip("Natural frequency of the float spring (higher = follows the waves more tightly).")]
        public float surfaceSpring = 4.5f;
        [Tooltip("Slow upward drift under water when not swimming (m/s²).")]
        public float buoyancy = 0.3f;
        [Tooltip("Look at least this far down while swimming forward to dive from the surface.")]
        public float diveAngle = 22f;
        public float diveImpulse = 1.6f;
        [Tooltip("How much the slope of the waves pushes you around at the surface.")]
        public float wavePush = 1.2f;
        public float waterHopSpeed = 3.6f;
        [Tooltip("How much the water's movement (WaterCurrent: swell push and pull, drift) carries the swimmer.")]
        [Min(0f)] public float currentInfluence = 1f;

        [Header("Climbing out")]
        public float climbMaxHeight = 1.6f;
        public float climbDuration = 0.6f;
        public LayerMask climbMask = ~0;

        [Header("Camera feel")]
        public float headBobAmount = 0.045f;
        public float swimSwayDegrees = 1.3f;
        public float strafeRollDegrees = 3.5f;
        public float strokeSurge = 0.04f;
        public float waveTiltDegrees = 6f;
        public float sprintFovBoost = 7f;
        public float landingDip = 0.12f;

        [Header("Events")]
        public UnityEvent<float> onEnterWater = new UnityEvent<float>();   // impact speed
        public UnityEvent onExitWater = new UnityEvent();
        public UnityEvent onHeadUnderwater = new UnityEvent();
        public UnityEvent onHeadAboveWater = new UnityEvent();
        public UnityEvent onSwimStroke = new UnityEvent();
        public UnityEvent onFootstep = new UnityEvent();
        public UnityEvent onJump = new UnityEvent();
        public UnityEvent<float> onLand = new UnityEvent<float>();          // fall speed
        public UnityEvent onClimbOut = new UnityEvent();

        // ------------------------------------------------------------------ state (read-only for other scripts)
        public MoveState State { get; private set; }
        public bool IsSwimming => State == MoveState.SurfaceSwimming || State == MoveState.Underwater;
        public bool IsHeadUnderwater { get; private set; }
        /// <summary>Metres the eyes are below the wave surface (negative = above).</summary>
        public float EyeDepth { get; private set; }
        public Vector3 Velocity => velocity;
        /// <summary>The runtime copy of the input actions (enabled with this component), for other player scripts.</summary>
        public InputActionAsset Actions => actions;
        /// <summary>Multiplies the field of view (e.g. zoom while aiming). 1 = normal.</summary>
        public float FovScale { get; set; } = 1f;
        /// <summary>Multiplies look sensitivity (e.g. steadier while aiming). 1 = normal.</summary>
        public float LookScale { get; set; } = 1f;

        CharacterController controller;
        InputActionAsset actions;
        InputAction moveAction, lookAction, jumpAction, crouchAction, sprintAction;

        readonly WaterProbe probe = new WaterProbe();
        Vector3 velocity;
        float yaw, pitch;
        Vector2 smoothLook;
        float baseFov;

        float stepDistance;
        float bobPhase;
        float bobWeight;
        float strokePhase;
        float strokeKick;
        float landOffset, landVelocity;
        float rollSmooth;
        Vector3 waveTiltSmooth;
        float lastAirborneVy;
        bool wasGrounded;

        Vector3 climbFrom, climbTo;
        float climbT;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (cameraPivot == null && playerCamera != null) cameraPivot = playerCamera.transform.parent;
            baseFov = playerCamera != null ? playerCamera.fieldOfView : 70f;
            yaw = transform.eulerAngles.y;
            SetupInput();
        }

        void SetupInput()
        {
            // work on a copy so runtime tweaks never touch the project asset
            actions = inputActions != null ? Instantiate(inputActions) : CreateDefaultActions();
            var map = actions.FindActionMap("Player", true);
            moveAction = map.FindAction("Move", true);
            lookAction = map.FindAction("Look", true);
            jumpAction = map.FindAction("Jump", true);
            crouchAction = map.FindAction("Crouch", true);
            sprintAction = map.FindAction("Sprint", true);
            crouchAction.AddBinding("<Keyboard>/leftCtrl");   // "swim down" on the usual key too
        }

        static InputActionAsset CreateDefaultActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap("Player");
            var move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            var look = map.AddAction("Look", InputActionType.Value);
            look.AddBinding("<Pointer>/delta");
            look.AddBinding("<Gamepad>/rightStick");
            map.AddAction("Jump", InputActionType.Button).AddBinding("<Keyboard>/space");
            map.AddAction("Crouch", InputActionType.Button).AddBinding("<Keyboard>/c");
            map.AddAction("Sprint", InputActionType.Button).AddBinding("<Keyboard>/leftShift");
            var attack = map.AddAction("Attack", InputActionType.Button);
            attack.AddBinding("<Mouse>/leftButton");
            attack.AddBinding("<Gamepad>/rightTrigger");
            var aim = map.AddAction("Aim", InputActionType.Button);
            aim.AddBinding("<Mouse>/rightButton");
            aim.AddBinding("<Gamepad>/leftTrigger");
            return asset;
        }

        void OnEnable()
        {
            actions?.Enable();
            WaterProbe.Register(probe);
            LockCursor(true);
        }

        void OnDisable()
        {
            actions?.Disable();
            WaterProbe.Unregister(probe);
            LockCursor(false);
        }

        void OnDestroy()
        {
            if (actions != null && actions != inputActions) Destroy(actions);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

            UpdateLook(dt);

            probe.position = transform.position;
            float surfaceY = probe.HeightOr(float.NegativeInfinity);
            bool hasWater = probe.Water != null;

            if (State == MoveState.Climbing)
            {
                UpdateClimb(dt);
                UpdateHeadState(surfaceY, hasWater);
                return;
            }

            UpdateState(surfaceY, hasWater);

            switch (State)
            {
                case MoveState.SurfaceSwimming:
                case MoveState.Underwater:
                    Swim(dt, surfaceY);
                    break;
                default:
                    Walk(dt, surfaceY, hasWater);
                    break;
            }

            CollisionFlags flags = controller.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;

            UpdateHeadState(surfaceY, hasWater);
        }

        void UpdateLook(float dt)
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            Vector2 raw = lookAction.ReadValue<Vector2>();
            bool pointer = lookAction.activeControl != null && lookAction.activeControl.device is Pointer;
            Vector2 delta = (pointer ? raw * mouseSensitivity : raw * (gamepadLookSpeed * dt)) * LookScale;

            float k = lookSmoothing > 0f ? 1f - Mathf.Exp(-dt / lookSmoothing) : 1f;
            smoothLook = Vector2.Lerp(smoothLook, delta, k);
            yaw += smoothLook.x;
            pitch = Mathf.Clamp(pitch - smoothLook.y, -maxPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Which mode we are in, from how deep the body is in the water.
        void UpdateState(float surfaceY, bool hasWater)
        {
            float feet = transform.position.y;
            float chestDepth = hasWater ? surfaceY - (feet + chestHeight) : -100f;
            float eyeDepth = hasWater ? surfaceY - (feet + eyeHeight) : -100f;
            bool grounded = controller.isGrounded;
            MoveState previous = State;

            if (IsSwimming)
            {
                // leave the water when the body is mostly out of it, or standing in the shallows
                if (chestDepth < -0.35f || (grounded && chestDepth < 0.05f && velocity.y <= 0.1f))
                    State = grounded ? MoveState.Wading : MoveState.Airborne;
                else if (State == MoveState.Underwater && eyeDepth < 0.08f && velocity.y > -0.2f)
                    State = MoveState.SurfaceSwimming;
                else if (State == MoveState.SurfaceSwimming && eyeDepth > 0.6f)
                    State = MoveState.Underwater;
            }
            else
            {
                if (chestDepth > 0.25f && !(grounded && chestDepth < 0.4f))
                {
                    State = eyeDepth > 0.3f ? MoveState.Underwater : MoveState.SurfaceSwimming;
                    onEnterWater.Invoke(Mathf.Abs(velocity.y));
                }
                else if (grounded)
                    State = hasWater && surfaceY > feet + 0.15f ? MoveState.Wading : MoveState.Grounded;
                else
                    State = MoveState.Airborne;
            }

            if ((previous == MoveState.SurfaceSwimming || previous == MoveState.Underwater) && !IsSwimming)
                onExitWater.Invoke();
        }

        void UpdateHeadState(float surfaceY, bool hasWater)
        {
            float eyeY = cameraPivot != null ? cameraPivot.position.y : transform.position.y + eyeHeight;
            EyeDepth = hasWater ? surfaceY - eyeY : -100f;

            bool under = IsHeadUnderwater ? EyeDepth > -0.03f : EyeDepth > 0.03f;
            if (under == IsHeadUnderwater) return;
            IsHeadUnderwater = under;
            if (under) onHeadUnderwater.Invoke();
            else onHeadAboveWater.Invoke();
        }

        // ------------------------------------------------------------------ land

        void Walk(float dt, float surfaceY, bool hasWater)
        {
            bool grounded = controller.isGrounded;
            Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            bool sprint = sprintAction.IsPressed() && move.y > 0.1f;

            float speed = sprint ? sprintSpeed : walkSpeed;
            if (State == MoveState.Wading && hasWater)
            {
                float depth01 = Mathf.Clamp01((surfaceY - transform.position.y) / chestHeight);
                speed *= Mathf.Lerp(1f, 0.45f, depth01);
            }

            Vector3 wish = transform.forward * move.y + transform.right * move.x;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, wish * speed, (grounded ? groundAcceleration : airAcceleration) * dt);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;

            if (grounded)
            {
                if (!wasGrounded && lastAirborneVy < -3f)
                {
                    onLand.Invoke(-lastAirborneVy);
                    landVelocity -= Mathf.Min(-lastAirborneVy, 12f) * landingDip;
                }
                if (velocity.y < 0f) velocity.y = -2f;   // keep contact on slopes

                if (jumpAction.WasPressedThisFrame())
                {
                    velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight);
                    onJump.Invoke();
                }

                // footsteps + bob phase follow the distance actually walked
                float moved = horizontal.magnitude * dt;
                stepDistance += moved;
                bobPhase += moved / stepLength * Mathf.PI;
                if (stepDistance >= stepLength * 0.5f)
                {
                    stepDistance = 0f;
                    onFootstep.Invoke();
                }
            }
            else
            {
                velocity.y -= gravity * dt;
                lastAirborneVy = velocity.y;
            }

            bobWeight = Mathf.MoveTowards(bobWeight, grounded ? Mathf.Clamp01(horizontal.magnitude / walkSpeed) : 0f, dt * 4f);
            wasGrounded = grounded;
        }

        // ------------------------------------------------------------------ water

        void Swim(float dt, float surfaceY)
        {
            wasGrounded = false;
            lastAirborneVy = 0f;
            bobWeight = Mathf.MoveTowards(bobWeight, 0f, dt * 4f);

            Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            float vertical = (jumpAction.IsPressed() ? 1f : 0f) - (crouchAction.IsPressed() ? 1f : 0f);
            bool sprint = sprintAction.IsPressed();
            bool atSurface = State == MoveState.SurfaceSwimming;

            Transform look = cameraPivot != null ? cameraPivot : transform;
            Vector3 wish = look.forward * move.y + look.right * move.x;

            if (atSurface)
            {
                // dive: look down and swim, or press Crouch
                bool diving = crouchAction.IsPressed() || (move.y > 0.3f && pitch > diveAngle);
                if (diving)
                {
                    State = MoveState.Underwater;
                    atSurface = false;
                    velocity.y = Mathf.Min(velocity.y, -diveImpulse);
                }
                else
                {
                    wish.y = 0f;                                  // swim along the surface
                    wish = wish.normalized * Mathf.Min(wish.magnitude, 1f) * move.magnitude;
                    if (jumpAction.WasPressedThisFrame() && !TryStartClimb())
                    {
                        velocity.y = waterHopSpeed;               // hop out of the water
                        State = MoveState.Airborne;
                        onJump.Invoke();
                        return;
                    }
                }
            }
            if (!atSurface) wish += Vector3.up * vertical;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // rhythmic strokes: thrust peaks with each stroke, glide in between
            bool thrusting = wish.sqrMagnitude > 0.04f;
            float pulse = 0f;
            if (thrusting)
            {
                float rate = strokeRate * (sprint ? 1.45f : 1f) * Mathf.Lerp(0.6f, 1f, wish.magnitude);
                float before = strokePhase;
                strokePhase += dt * rate * Mathf.PI * 2f;
                if (Mathf.Floor(before / (Mathf.PI * 2f)) != Mathf.Floor(strokePhase / (Mathf.PI * 2f)))
                {
                    strokeKick = 1f;
                    onSwimStroke.Invoke();
                }
                float wave = 0.5f + 0.5f * Mathf.Sin(strokePhase);
                pulse = Mathf.Lerp(1f, 0.3f + 1.45f * wave * wave, strokePulse);
            }
            strokeKick = Mathf.MoveTowards(strokeKick, 0f, dt * 3f);

            float speed = sprint ? fastSwimSpeed : swimSpeed;
            // the water itself moves: swimming is relative to it, and without strokes you drift with it
            Vector3 flow = WaterCurrent.At(transform.position + Vector3.up * chestHeight) * currentInfluence;
            if (atSurface) flow.y = 0f;   // floating height is the waves' business
            Vector3 target = wish * (speed * pulse) + flow;
            float k = thrusting ? swimAcceleration : waterDrag;
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-k * dt));

            if (atSurface)
            {
                // float on the real waves: critically damped spring to "eyes just above the surface"
                float targetY = surfaceY - eyeHeight + surfaceEyeHeight;
                float error = targetY - transform.position.y;
                float w = surfaceSpring;
                velocity.y += (w * w * error - 2f * w * velocity.y) * dt;

                // the slope of the waves pushes you downhill a little
                Vector3 n = probe.HasData ? probe.Normal : Vector3.up;
                velocity.x += n.x * wavePush * dt;
                velocity.z += n.z * wavePush * dt;
            }
            else if (!thrusting)
            {
                velocity.y += buoyancy * dt;
            }

            // never pop out above the water while swimming under it
            if (!atSurface)
            {
                float maxY = surfaceY - eyeHeight + surfaceEyeHeight;
                if (transform.position.y > maxY && velocity.y > 0f) velocity.y *= 0.5f;
            }
        }

        // ------------------------------------------------------------------ climbing out onto a ledge

        bool TryStartClimb()
        {
            Vector3 fwd = transform.forward;
            Vector3 chest = transform.position + Vector3.up * chestHeight;
            float radius = controller.radius * 0.9f;

            if (!Physics.SphereCast(chest - fwd * 0.1f, radius, fwd, out RaycastHit wall, 1.0f, climbMask, QueryTriggerInteraction.Ignore))
                return false;

            // find the top of the ledge
            Vector3 probeTop = wall.point + fwd * (controller.radius + 0.15f) + Vector3.up * (climbMaxHeight + 0.5f);
            if (!Physics.Raycast(probeTop, Vector3.down, out RaycastHit top, climbMaxHeight + 1.5f, climbMask, QueryTriggerInteraction.Ignore))
                return false;
            float rise = top.point.y - transform.position.y;
            if (rise < 0.2f || rise > climbMaxHeight + chestHeight || Vector3.Angle(top.normal, Vector3.up) > 40f)
                return false;

            // is there room to stand up there?
            Vector3 standPos = top.point + Vector3.up * 0.05f;
            Vector3 p0 = standPos + Vector3.up * (controller.radius + 0.05f);
            Vector3 p1 = standPos + Vector3.up * (controller.height - controller.radius);
            if (Physics.CheckCapsule(p0, p1, controller.radius * 0.95f, climbMask, QueryTriggerInteraction.Ignore))
                return false;

            climbFrom = transform.position;
            climbTo = standPos;
            climbT = 0f;
            velocity = Vector3.zero;
            State = MoveState.Climbing;
            controller.enabled = false;
            onClimbOut.Invoke();
            return true;
        }

        void UpdateClimb(float dt)
        {
            climbT += dt / climbDuration;
            float t = Mathf.Clamp01(climbT);
            // first up, then forward: pull yourself over the edge
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.65f));
            float fwd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.65f));
            Vector3 p = new Vector3(
                Mathf.Lerp(climbFrom.x, climbTo.x, fwd),
                Mathf.Lerp(climbFrom.y, climbTo.y, up),
                Mathf.Lerp(climbFrom.z, climbTo.z, fwd));
            transform.position = p;

            if (t >= 1f)
            {
                controller.enabled = true;
                State = MoveState.Grounded;
                wasGrounded = true;
                onExitWater.Invoke();
            }
        }

        // ------------------------------------------------------------------ camera feel

        void LateUpdate()
        {
            if (cameraPivot == null) return;
            float dt = Time.deltaTime;
            float t = Time.time;
            bool swimming = IsSwimming;
            bool underwater = State == MoveState.Underwater;

            // landing dip spring
            landVelocity += (-landOffset * 90f - landVelocity * 14f) * dt;
            landOffset += landVelocity * dt;

            // walking bob
            Vector3 pos = new Vector3(Mathf.Cos(bobPhase) * headBobAmount * 0.5f,
                                      -Mathf.Abs(Mathf.Sin(bobPhase)) * headBobAmount, 0f) * bobWeight;
            pos.y += landOffset;

            float roll = 0f;
            float pitchOffset = 0f;
            Vector2 move = moveAction.ReadValue<Vector2>();

            if (swimming)
            {
                // slow floaty sway, stronger under water
                float sway = underwater ? swimSwayDegrees : swimSwayDegrees * 0.5f;
                roll += Mathf.Sin(t * 0.7f) * sway;
                pitchOffset += Mathf.Sin(t * 0.93f + 1.3f) * sway * 0.5f;
                // each stroke surges the view forward and a little up
                float kick = strokeKick * strokeKick;
                pos.z += kick * strokeSurge;
                pos.y += kick * strokeSurge * 0.5f + Mathf.Sin(strokePhase) * 0.015f;
            }

            // roll into strafes
            rollSmooth = Mathf.Lerp(rollSmooth, -move.x * strafeRollDegrees * (swimming ? 1f : 0.35f), 1f - Mathf.Exp(-dt * 6f));
            roll += rollSmooth;

            // at the surface the waves tilt the head
            Vector3 tilt = Vector3.zero;
            if (State == MoveState.SurfaceSwimming && probe.HasData)
            {
                Vector3 n = probe.Normal;
                Vector3 local = transform.InverseTransformDirection(n);
                tilt = new Vector3(local.z, 0f, -local.x) * Mathf.Rad2Deg * (waveTiltDegrees / 10f);
            }
            waveTiltSmooth = Vector3.Lerp(waveTiltSmooth, tilt, 1f - Mathf.Exp(-dt * 3f));

            cameraPivot.localRotation = Quaternion.Euler(pitch + pitchOffset + waveTiltSmooth.x, 0f, roll + waveTiltSmooth.z);
            if (playerCamera != null)
            {
                playerCamera.transform.localPosition = pos;
                float speed01 = Mathf.Clamp01(new Vector3(velocity.x, 0f, velocity.z).magnitude / (swimming ? fastSwimSpeed : sprintSpeed));
                float targetFov = (baseFov + sprintFovBoost * Mathf.SmoothStep(0f, 1f, (speed01 - 0.55f) / 0.45f)) * FovScale;
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-dt * 5f));
            }
        }
    }
}

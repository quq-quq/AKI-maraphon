using System;
using AKI.Player;
using AKI.Water;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AKI.Menu
{
    /// <summary>
    /// The main menu and the way into the game, all in one camera move:
    ///   Menu        the cutscene camera stays exactly where it was placed in the scene (position, rotation and field
    ///               of view are set by hand); the grandfather sits in his boat on the waves.
    ///   Cutscene    any key: he stands up, runs and dives (<see cref="FishmanCutscene"/>). The camera doesn't move.
    ///   Possessing  as he leaves the boat the camera flies from its place into his head, looking where he flies,
    ///               and takes the player camera's field of view on the way.
    ///   Possessed   first person through his eyes into the water (after the animation ends the dive carries on by
    ///               itself: falling, then slowed down by the water).
    ///   Settling    under the water the view levels out...
    ///   Playing     ...and exactly there the player (<see cref="player"/>, inactive until then) takes over:
    ///               same position, same direction, first person, swimming.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        enum Phase { Menu, Cutscene, Possessing, Possessed, Settling, Playing }

        [Header("Scene")]
        public FishmanCutscene cutscene;
        [Tooltip("Camera of the menu and the cutscene (with an AudioListener), placed by hand. Destroyed when the player takes over.")]
        public Camera cutsceneCamera;
        [Tooltip("The first-person player object, kept inactive until the hand-over.")]
        public GameObject player;
        [Tooltip("The player's camera (empty = the first camera under the player).")]
        public Camera playerCamera;

        [Header("Into his eyes")]
        [Tooltip("When in the dive (0..1 of Run_To_Dive) the camera starts flying into his head.")]
        [Range(0f, 1f)] public float possessAt = 0.45f;
        [Tooltip("Seconds the flight into his head takes.")]
        [Min(0.05f)] public float possessSeconds = 0.6f;
        [Tooltip("Eye position in front of the head bone (m).")]
        public float eyeForward = 0.12f;
        [Tooltip("Largest look up / down while diving (degrees).")]
        [Range(0f, 89f)] public float maxDivePitch = 70f;

        [Header("Into the water")]
        [Tooltip("Depth under the sea level where the view settles and the player takes over (m).")]
        [Min(0.1f)] public float handOverDepth = 1.2f;
        [Tooltip("Seconds under water after which the player takes over even if the dive was too shallow to reach " +
                 "Hand Over Depth (the water stops a weak dive short, and the game would wait forever).")]
        [Min(0.1f)] public float handOverAfterSeconds = 1f;

        [Header("Coming to in the boat")]
        [Tooltip("After he passed out (not an ending): seconds he sits in the boat once his eyes are open, then he " +
                 "dives again by himself - no menu.")]
        [Min(0f)] public float respawnDiveDelay = 1.2f;

        [Header("Start hint")]
        [Tooltip("Shown at the bottom of the menu until he gets up. Empty = no hint.")]
        public string startHint = "Нажмите любую кнопку";
        [Min(8)] public int startHintSize = 34;
        public Color startHintColour = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("Seconds for the view to level out under the water.")]
        [Min(0.05f)] public float settleSeconds = 0.6f;
        [Tooltip("How quickly the water slows the dive down (1/s).")]
        [Min(0f)] public float waterDrag = 2.5f;

        [Header("Current")]
        [Tooltip("While the menu plays, the wind (and the current it drives) doesn't turn and the current doesn't wander: it flows one way. Back to normal once the player takes over.")]
        public bool steadyCurrentInMenu = true;
        [Tooltip("Seconds over which the current's wandering comes back after the hand-over.")]
        [Min(0f)] public float currentReleaseSeconds = 3f;

        [Header("Events")]
        public UnityEvent onCutsceneStarted = new UnityEvent();
        [Tooltip("The camera is in his head (first person begins).")]
        public UnityEvent onPossessed = new UnityEvent();
        [Tooltip("His head went under the water.")]
        public UnityEvent onEnteredWater = new UnityEvent();
        [Tooltip("The player has control.")]
        public UnityEvent onGameStarted = new UnityEvent();

        /// <summary>Scene-independent: the menu is over, he gets up to dive (for listeners that outlive the scene).</summary>
        public static event Action MenuEnded;
        /// <summary>Scene-independent: his head went under the water in the dive.</summary>
        public static event Action EnteredWater;

        /// <summary>Set before a reload after passing out: the next menu skips itself - he comes to in the boat and
        /// dives again by himself. Read (and cleared) by the menu of the reloaded scene.</summary>
        public static bool RespawnInBoat;

        Phase phase = Phase.Menu;
        Vector3 diveDirection;      // flat, from the boat to where he ends
        float phaseTime;
        Vector3 fromPosition;       // the camera's own pose and field of view when the flight into his head starts
        Quaternion fromRotation;
        float fromFieldOfView;
        Vector3 lastHead;
        Vector3 headVelocity;
        Vector3 eyeVelocity;        // the view's own motion once it no longer follows the animation
        bool inWater;
        float inWaterTime;
        float playerFieldOfView = 72f;
        Vector3 playerEyeOffset = new Vector3(0f, 1.65f, 0f);
        Wind heldWind;              // the menu's steady current: what was changed, and the values to give back
        float windTurnRate;
        WaterCurrent heldCurrent;
        float currentVariation;
        float releaseTime = -1f;
        float respawnTime = -1f;    // >= 0: coming to in the boat, counting to the dive
        CanvasGroup hint;
        float hintFade = -1f;       // >= 0: the hint is fading out

        void Start()
        {
            if (playerCamera == null && player != null) playerCamera = player.GetComponentInChildren<Camera>(true);
            if (playerCamera != null)
            {
                playerFieldOfView = playerCamera.fieldOfView;
                playerEyeOffset = player.transform.InverseTransformPoint(playerCamera.transform.position);
            }
            if (player != null) player.SetActive(false);
            if (steadyCurrentInMenu) HoldCurrent();
            if (RespawnInBoat)
            {
                RespawnInBoat = false;
                respawnTime = 0f;
            }
            else BuildHint();   // he came to in the boat and dives by himself: nothing to press

            // which way he dives: where the view looks if his head isn't moving yet
            cutscene.MeasureDive(out Vector3 seated, out Vector3 end);
            diveDirection = Flat(end - seated);
            diveDirection = diveDirection.sqrMagnitude > 0.01f ? diveDirection.normalized : Flat(cutscene.transform.forward).normalized;
            lastHead = seated;
        }

        void Update()
        {
            if (phase == Phase.Menu && AnyPress()) StartCutscene();
            UpdateHint();
            // came to in the boat: once the eyes are open (the blackout is over), a moment, then he dives again
            if (respawnTime >= 0f && phase == Phase.Menu && !Blackout.IsRunning)
            {
                respawnTime += Time.deltaTime;
                if (respawnTime >= respawnDiveDelay) StartCutscene();
            }
            if (releaseTime >= 0f) ReleaseCurrent();
        }

        // ------------------------------------------------------------------ steady current in the menu

        // The wind stops turning (the current follows the wind) and the current stops wandering.
        void HoldCurrent()
        {
            heldWind = Wind.Active;
            if (heldWind != null)
            {
                windTurnRate = heldWind.turnRate;
                heldWind.turnRate = 0f;
            }
            heldCurrent = WaterCurrent.Active;
            if (heldCurrent != null)
            {
                currentVariation = heldCurrent.directionVariation;
                heldCurrent.directionVariation = 0f;
            }
        }

        // After the hand-over: the wind turns again from where it is (no jump), the wandering eases back in.
        void ReleaseCurrent()
        {
            if (heldWind != null) heldWind.turnRate = windTurnRate;
            heldWind = null;
            releaseTime += Time.deltaTime;
            float k = currentReleaseSeconds > 0f ? Mathf.Clamp01(releaseTime / currentReleaseSeconds) : 1f;
            if (heldCurrent != null) heldCurrent.directionVariation = Mathf.Lerp(0f, currentVariation, Mathf.SmoothStep(0f, 1f, k));
            if (k >= 1f)
            {
                heldCurrent = null;
                releaseTime = -1f;
            }
        }

        /// <summary>Starts the dive (what any key does in the menu).</summary>
        public void StartCutscene()
        {
            if (phase != Phase.Menu) return;
            cutscene.Play();
            Enter(Phase.Cutscene);
            onCutsceneStarted.Invoke();
            MenuEnded?.Invoke();
            if (hint != null) hintFade = 0f;
        }

        // any key, a click of any mouse button or a gamepad button
        static bool AnyPress()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame
                    || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame))
                || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame
                    || Gamepad.current.startButton.wasPressedThisFrame));
        }

        // ------------------------------------------------------------------ start hint

        void BuildHint()
        {
            if (string.IsNullOrEmpty(startHint)) return;
            var root = new GameObject("Start Hint", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            hint = root.AddComponent<CanvasGroup>();
            hint.blocksRaycasts = false;
            hint.interactable = false;

            var label = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            var rect = label.rectTransform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = new Vector2(0f, 0.06f);
            rect.anchorMax = new Vector2(1f, 0.14f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = startHintSize;
            label.color = startHintColour;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = startHint;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        }

        // breathes softly while waiting, fades out when he gets up
        void UpdateHint()
        {
            if (hint == null) return;
            if (hintFade < 0f)
            {
                hint.alpha = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.2f);
                return;
            }
            hintFade += Time.unscaledDeltaTime;
            hint.alpha = Mathf.Min(hint.alpha, 1f - hintFade / 0.4f);
            if (hintFade >= 0.4f)
            {
                Destroy(hint.gameObject);
                hint = null;
            }
        }

        // The animation is posed in Update: the camera follows it here.
        void LateUpdate()
        {
            if (phase == Phase.Playing || cutsceneCamera == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            phaseTime += dt;

            Vector3 head = cutscene.Head.position;
            if (phase == Phase.Menu)
            {
                lastHead = head;   // the camera stays where it was placed; only keep track of the rocking head
                return;
            }
            headVelocity = Vector3.Lerp(headVelocity, (head - lastHead) / dt, 1f - Mathf.Exp(-10f * dt));
            lastHead = head;
            Transform cam = cutsceneCamera.transform;

            switch (phase)
            {
                case Phase.Cutscene:
                    if (cutscene.DiveProgress >= possessAt) Enter(Phase.Possessing);
                    break;

                case Phase.Possessing:
                {
                    // from where the camera stands into his eyes
                    EyePose(head, out Vector3 eye, out Quaternion eyeRotation);
                    float k = Mathf.SmoothStep(0f, 1f, phaseTime / possessSeconds);
                    cam.SetPositionAndRotation(Vector3.Lerp(fromPosition, eye, k), Quaternion.Slerp(fromRotation, eyeRotation, k));
                    cutsceneCamera.fieldOfView = Mathf.Lerp(fromFieldOfView, playerFieldOfView, k);
                    if (k > 0.85f) cutscene.SetActorVisible(false);   // the camera is inside the head now
                    if (k >= 1f)
                    {
                        Enter(Phase.Possessed);
                        onPossessed.Invoke();
                    }
                    break;
                }
                case Phase.Possessed:
                {
                    if (!cutscene.IsFinished)
                    {
                        EyePose(head, out Vector3 eye, out Quaternion eyeRotation);
                        eyeVelocity = (eye - cam.position) / dt;
                        cam.SetPositionAndRotation(eye, eyeRotation);
                    }
                    else
                    {
                        // the animation is over: the dive carries on by itself
                        Carry(cam, dt);
                        if (eyeVelocity.sqrMagnitude > 0.01f) cam.rotation = Quaternion.Slerp(cam.rotation, LookAlong(eyeVelocity), 1f - Mathf.Exp(-6f * dt));
                    }
                    CheckWater(cam.position);
                    if (inWater) inWaterTime += dt;
                    if (Depth(cam.position) >= handOverDepth || inWaterTime >= handOverAfterSeconds) Enter(Phase.Settling);
                    break;
                }
                case Phase.Settling:
                {
                    // slowed down by the water, the view levels out to how the player will look
                    Carry(cam, dt);
                    float k = Mathf.SmoothStep(0f, 1f, phaseTime / settleSeconds);
                    cam.rotation = Quaternion.Slerp(fromRotation, Level(fromRotation), k);
                    if (k >= 1f) HandOver();
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ camera poses

        // In his head, looking where he flies.
        void EyePose(Vector3 head, out Vector3 position, out Quaternion rotation)
        {
            Vector3 heading = headVelocity.sqrMagnitude > 0.25f ? headVelocity : diveDirection;
            rotation = LookAlong(heading);
            position = head + rotation * Vector3.forward * eyeForward;
        }

        Quaternion LookAlong(Vector3 direction)
        {
            Vector3 flat = Flat(direction);
            if (flat.sqrMagnitude < 1e-4f) flat = diveDirection;
            float limit = Mathf.Tan(maxDivePitch * Mathf.Deg2Rad) * flat.magnitude;
            direction.y = Mathf.Clamp(direction.y, -limit, limit);
            return Quaternion.LookRotation(direction.sqrMagnitude > 1e-6f ? direction : diveDirection, Vector3.up);
        }

        // Falling through the air, slowed down by the water.
        void Carry(Transform cam, float dt)
        {
            bool under = Depth(cam.position) > 0f;
            if (under) eyeVelocity *= Mathf.Exp(-waterDrag * dt);
            else eyeVelocity += Physics.gravity * dt;
            cam.position += eyeVelocity * dt;
        }

        static Quaternion Level(Quaternion rotation)
        {
            Vector3 forward = Flat(rotation * Vector3.forward);
            if (forward.sqrMagnitude < 1e-4f) forward = Flat(rotation * Vector3.up);
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        // ------------------------------------------------------------------ water and hand-over

        void CheckWater(Vector3 eye)
        {
            if (inWater || Depth(eye) <= 0f) return;
            inWater = true;
            cutscene.SetCarriedHarpoonVisible(false);
            onEnteredWater.Invoke();
            EnteredWater?.Invoke();
        }

        float Depth(Vector3 point)
        {
            WaterSurface water = WaterSurface.FindAt(point);
            if (water == null && WaterSurface.Instances.Count > 0) water = WaterSurface.Instances[0];
            return water != null ? water.WaterLevel - point.y : 0f;
        }

        // The player appears exactly where the view is, looking the same way (its own look starts level).
        void HandOver()
        {
            Transform cam = cutsceneCamera.transform;
            if (player != null)
            {
                Quaternion yaw = Level(cam.rotation);
                player.transform.SetPositionAndRotation(cam.position - yaw * playerEyeOffset, yaw);
                player.SetActive(true);
            }
            CheckWater(cam.position);
            if (heldWind != null || heldCurrent != null) releaseTime = 0f;
            Enter(Phase.Playing);
            // destroyed rather than switched off: the water looks the audio listener up again and finds the player's
            Destroy(cutsceneCamera.gameObject);
            cutsceneCamera = null;
            onGameStarted.Invoke();
        }

        void Enter(Phase next)
        {
            Transform cam = cutsceneCamera != null ? cutsceneCamera.transform : null;
            if (next == Phase.Possessing && cam != null)
            {
                fromPosition = cam.position;
                fromRotation = cam.rotation;
                fromFieldOfView = cutsceneCamera.fieldOfView;
            }
            if (next == Phase.Possessed) eyeVelocity = headVelocity;   // the dive goes on at his speed
            if (next == Phase.Settling && cam != null) fromRotation = cam.rotation;
            phase = next;
            phaseTime = 0f;
        }

        static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || cutscene == null || cutscene.Head == null) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            Gizmos.DrawLine(cutscene.Head.position, cutscene.Head.position + diveDirection * 3f);
        }
    }
}

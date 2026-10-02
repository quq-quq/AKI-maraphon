using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AKI.Menu
{
    /// <summary>
    /// Put on the menu / cutscene camera next to a Volume with a Bokeh <see cref="DepthOfField"/>.
    ///  - Keeps the focus on <see cref="target"/> (the boat), so it stays sharp while the sea and sky behind it melt away.
    ///  - While the menu shows, turns the camera gently after the boat like an operator would, holding it where the scene
    ///    framed it: the swell lifts and drops the boat by metres, which through the long lens would throw it about the
    ///    screen. Stops when the cutscene starts (the cutscene flies the camera from wherever it is then).
    /// Works on the volume's own copy of the profile, the asset is left alone.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class MenuCameraFocus : MonoBehaviour
    {
        [Tooltip("What stays in focus and in frame.")]
        public Transform target;
        [Tooltip("Point above the target's pivot that is focused on and framed (m): the fisherman rather than the keel.")]
        public float targetHeight = 0.6f;
        [Tooltip("Volume whose depth of field is driven. Found on this object when empty.")]
        public Volume volume;
        [Tooltip("Focus is pulled towards the target in this many seconds.")]
        [Min(0.01f)] public float followSeconds = 0.25f;

        [Header("Framing")]
        [Tooltip("Turn after the target while the menu shows.")]
        public bool keepFramed = true;
        [Tooltip("Seconds the camera takes to catch up with the boat: slow is a calm operator, fast a nervous one.")]
        [Min(0.05f)] public float frameSeconds = 1.4f;
        [Tooltip("Share of the boat's rise and fall the camera rides along with (an operator on the same swell), so " +
                 "the horizon doesn't swing through the frame. The rest is followed by turning.")]
        [Range(0f, 1f)] public float heaveFollow = 0.6f;
        [Tooltip("Stops the framing when its cutscene starts. Found in the scene when empty.")]
        public MainMenuController menu;

        Camera cam;
        DepthOfField dof;
        float distance;
        Vector2 framePoint;
        bool framing;
        float baseHeight, targetBaseHeight;

        Vector3 Point => target.position + Vector3.up * targetHeight;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (target == null) { enabled = false; return; }
            if (volume == null)
                foreach (var v in GetComponents<Volume>())
                    if (v.sharedProfile != null && v.sharedProfile.Has<DepthOfField>()) { volume = v; break; }
            if (volume != null) volume.profile.TryGet(out dof);
            distance = Vector3.Distance(transform.position, Point);

            // hold the target where the scene has it now
            Vector3 vp = cam.WorldToViewportPoint(Point);
            framePoint = new Vector2(vp.x, vp.y);
            framing = keepFramed && vp.z > 0f;
            baseHeight = transform.position.y;
            targetBaseHeight = target.position.y;
            if (menu == null) menu = FindFirstObjectByType<MainMenuController>();
            if (menu != null) menu.onCutsceneStarted.AddListener(StopFraming);
        }

        void OnDestroy()
        {
            if (menu != null) menu.onCutsceneStarted.RemoveListener(StopFraming);
        }

        void StopFraming() => framing = false;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (framing)
            {
                Vector3 p = transform.position;
                float ride = baseHeight + (target.position.y - targetBaseHeight) * heaveFollow;
                p.y = Mathf.Lerp(p.y, ride, 1f - Mathf.Exp(-dt / frameSeconds));
                transform.position = p;

                // look at the target, then turn away from it by the angles of its spot on the screen
                float halfV = cam.fieldOfView * 0.5f;
                float halfH = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect) * 0.5f;
                Quaternion look = Quaternion.LookRotation(Point - transform.position, Vector3.up);
                Quaternion wanted = look * Quaternion.Euler((framePoint.y - 0.5f) * 2f * halfV, -(framePoint.x - 0.5f) * 2f * halfH, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-dt / frameSeconds));
            }

            if (dof == null) return;
            float wantedDistance = Vector3.Distance(transform.position, Point);
            distance = Mathf.Lerp(distance, wantedDistance, 1f - Mathf.Exp(-dt / followSeconds));
            dof.focusDistance.Override(Mathf.Max(0.1f, distance));
        }
    }
}

using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// Put on the player camera. Drives the lens effects of <see cref="WaterLensFeature"/>:
    ///  - when the camera leaves the water the lens is soaked: a sheet of water slides off, then drops run down
    ///    and evaporate over <see cref="dryTime"/> seconds,
    ///  - near the surface the waterline is drawn across the lens.
    /// Requires the renderer feature (menu AKI/Water/Install Lens Effect).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class WaterCameraEffects : MonoBehaviour
    {
        [Tooltip("Seconds until the lens is completely dry after surfacing.")]
        [Range(0.5f, 20f)] public float dryTime = 6f;
        [Tooltip("Amount of drops left on the lens.")]
        [Range(0f, 1.5f)] public float drops = 1f;
        [Tooltip("How strongly water on the lens bends the image.")]
        [Range(0f, 2f)] public float distortion = 1f;
        [Tooltip("Depth (m) the camera has to reach before surfacing counts as leaving the water (hysteresis).")]
        [Min(0f)] public float submergeDepth = 0.2f;

        static readonly int WetnessId = Shader.PropertyToID("_WaterLensWetness");
        static readonly int SinceExitId = Shader.PropertyToID("_WaterLensSinceExit");
        static readonly int NearSurfaceId = Shader.PropertyToID("_WaterLensNearSurface");
        static readonly int DistortionId = Shader.PropertyToID("_WaterLensDistortion");
        static readonly int DropsId = Shader.PropertyToID("_WaterLensDrops");
        static readonly int LevelId = Shader.PropertyToID("_WaterLevelGlobal");
        static readonly int CamPosId = Shader.PropertyToID("_WaterLensCamPos");
        static readonly int NearFwdId = Shader.PropertyToID("_WaterLensNearFwd");
        static readonly int NearRightId = Shader.PropertyToID("_WaterLensNearRight");
        static readonly int NearUpId = Shader.PropertyToID("_WaterLensNearUp");

        Camera cam;
        bool submerged;
        float exitTime = -1000f;
        float wetness;

        /// <summary>1 right after surfacing, 0 when the lens is dry.</summary>
        public float Wetness => wetness;

        /// <summary>Soak the lens now (e.g. a splash hits the camera).</summary>
        public void Splash(float amount = 1f)
        {
            exitTime = Now - (1f - Mathf.Clamp01(amount)) * dryTime;
        }

        static float Now => Application.isPlaying ? Time.time : (float)UnityEditor_TimeSinceStartup();

        static double UnityEditor_TimeSinceStartup()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartup;
#endif
        }

        void OnEnable()
        {
            cam = GetComponent<Camera>();
        }

        void OnDisable()
        {
            WaterLensFeature.Active = false;
            Shader.SetGlobalFloat(WetnessId, 0f);
        }

        void LateUpdate()
        {
            WaterSurface water = FindWater(transform.position);
            if (water == null || cam == null)
            {
                WaterLensFeature.Active = false;
                return;
            }

            float level = water.WaterLevel;
            float depth = level - transform.position.y;
            Material src = water.ActiveMaterial;
            float band = src != null
                ? src.GetFloat("_WaveAmplitude") * 2f + src.GetFloat("_CellWaveHeight") + 0.4f + cam.nearClipPlane
                : 1.5f;

            // leaving the water soaks the lens
            if (!submerged && depth > submergeDepth) submerged = true;
            else if (submerged && depth < -band * 0.5f)
            {
                submerged = false;
                exitTime = Now;
            }

            float since = Now - exitTime;
            wetness = submerged ? 0f : Mathf.Clamp01(1f - since / dryTime);
            wetness = wetness * wetness * (3f - 2f * wetness);   // ease out: drops linger, then go
            bool nearSurface = Mathf.Abs(depth) < band;

            WaterLensFeature.Active = wetness > 0.001f || nearSurface;
            if (!WaterLensFeature.Active) return;

            Material lens = WaterLensFeature.SharedMaterial;
            if (lens != null && src != null) lens.CopyPropertiesFromMaterial(src);

            Shader.SetGlobalFloat(WetnessId, wetness);
            Shader.SetGlobalFloat(SinceExitId, since);
            Shader.SetGlobalFloat(NearSurfaceId, nearSurface ? 1f : 0f);
            Shader.SetGlobalFloat(DistortionId, distortion);
            Shader.SetGlobalFloat(DropsId, drops);
            Shader.SetGlobalFloat(LevelId, level);

            // near plane of the camera in world space (the "glass" the water sits on)
            Transform tr = cam.transform;
            float n = cam.nearClipPlane;
            float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * n;
            float halfW = halfH * cam.aspect;
            Shader.SetGlobalVector(CamPosId, tr.position);
            Shader.SetGlobalVector(NearFwdId, tr.forward * n);
            Shader.SetGlobalVector(NearRightId, tr.right * halfW);
            Shader.SetGlobalVector(NearUpId, tr.up * halfH);
        }

        static WaterSurface FindWater(Vector3 p)
        {
            var list = WaterSurface.Instances;
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsInsideArea(p)) return list[i];
            return list.Count > 0 ? list[0] : null;
        }
    }
}

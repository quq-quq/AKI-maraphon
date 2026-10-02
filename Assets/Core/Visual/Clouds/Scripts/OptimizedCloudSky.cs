using UnityEngine;
using AKI.Water;

namespace AKI.Clouds
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class OptimizedCloudSky : MonoBehaviour
    {
        public static OptimizedCloudSky Active { get; private set; }
        public Material material;
        [Header("GPU budget")]
        [Range(2, 8)] public int downsample = 4;
        [InspectorName("Maximum Buffer Edge"), Range(256, 1024)] public int maximumBufferWidth = 384;
        [Range(12, 48)] public int raySteps = 32;
        public bool showInSceneView = true;
        [Tooltip("Skip only well below the wave zone; the water shader handles the actual waterline.")]
        public bool skipUnderwater = true;
        public float waterLevel;
        [Min(1), Tooltip("Minimum depth before clouds start fading. Expanded automatically to cover the water material's wave band.")]
        public float underwaterSafeDepth = 6;
        [Min(.5f)] public float underwaterFadeDistance = 2;
        [Header("Cloud layer (metres)")]
        public float baseHeight = 650;
        [Min(100)] public float thickness = 500;
        [Range(2000, 30000)] public float maxDistance = 15000;
        [Range(400, 4000)] public float shapeScale = 1800;
        [Range(.15f, .9f)] public float coverage = .53f;
        [Range(.001f, .03f)] public float extinction = .009f;
        [Header("Smooth GPU-only animation")]
        public Vector2 wind = new Vector2(6, 2);
        [Range(0, .03f)] public float evolution = .0035f;
        [Header("Light")]
        public Color ambientColor = new Color(.68f, .74f, .82f);
        public Color sunlightColor = new Color(.98f, .99f, 1);
        [Range(0, 2)] public float silverLining = .45f;

        static readonly int WaveAmplitude = Shader.PropertyToID("_WaveAmplitude");
        static readonly int CellWaveHeight = Shader.PropertyToID("_CellWaveHeight");

        public float GetCloudVisibility(Camera camera)
        {
            if (!skipUnderwater) return 1;
            float level = waterLevel;
            float safeDepth = Mathf.Max(1, underwaterSafeDepth);
            var water = WaterSurface.FindAt(camera.transform.position);
            if (water != null)
            {
                level = water.WaterLevel;
                var waterMaterial = water.ActiveMaterial;
                if (waterMaterial != null)
                {
                    // Same conservative wave zone as WaterSurfaceBand / WaterCameraEffects.
                    // Never hide clouds while a wave trough can still reveal the sky.
                    float amplitude = waterMaterial.HasProperty(WaveAmplitude) ? waterMaterial.GetFloat(WaveAmplitude) : 0;
                    float cellHeight = waterMaterial.HasProperty(CellWaveHeight) ? waterMaterial.GetFloat(CellWaveHeight) : 0;
                    safeDepth = Mathf.Max(safeDepth, amplitude * 2 + cellHeight + .4f + camera.nearClipPlane);
                }
            }
            float depth = level - camera.transform.position.y;
            float t = Mathf.Clamp01((depth - safeDepth) / Mathf.Max(.5f, underwaterFadeDistance));
            return 1 - t * t * (3 - 2 * t);
        }

        void OnEnable() { Active = this; ApplySettings(); }
        void OnDisable() { if (Active == this) Active = null; }
        void OnValidate()
        {
            thickness = Mathf.Max(100, thickness); shapeScale = Mathf.Max(400, shapeScale);
            if (isActiveAndEnabled) { Active = this; ApplySettings(); }
        }
        public void ApplySettings()
        {
            if (material == null) return;
            material.SetVector("_CloudLayer", new Vector4(baseHeight, baseHeight + thickness, maxDistance, shapeScale));
            material.SetVector("_CloudWind", new Vector4(wind.x, wind.y, evolution, 0));
            material.SetVector("_CloudShape", new Vector4(coverage, extinction, silverLining, raySteps));
            material.SetColor("_CloudAmbient", ambientColor);
            material.SetColor("_CloudSunColor", sunlightColor);
            material.SetFloat("_CloudTimeOverride", -1);
        }
    }
}

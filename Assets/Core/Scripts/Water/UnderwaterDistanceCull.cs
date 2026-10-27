using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Water
{
    /// <summary>
    /// Stops drawing the renderers under this object (the coral reef) past the distance where the underwater haze has
    /// swallowed everything, while a camera is wholly under the water. Down there the underwater effect paints nothing
    /// but haze beyond that distance (AKI/WaterUnderwater: the view distance is capped at the fog end, where the haze
    /// is complete), whatever stands behind it, so those corals only cost draw calls. Above the water, and in the band
    /// the waves move through, everything is drawn as before: from up there the reef can show through the surface.
    /// The renderers go to their own layer (<see cref="layerName"/>) and the cameras cull that layer by distance
    /// (Camera.layerCullDistances), which matters most where there is no GPU Resident Drawer (the web build).
    /// </summary>
    public class UnderwaterDistanceCull : MonoBehaviour
    {
        static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
        static readonly int CellWaveHeightId = Shader.PropertyToID("_CellWaveHeight");
        static readonly int FogEndId = Shader.PropertyToID("_UnderFogEnd");
        static readonly int MaxDistanceId = Shader.PropertyToID("_UnderMaxDistance");

        [Tooltip("Layer the renderers are moved to (Project Settings > Tags and Layers).")]
        public string layerName = "Reef";
        [Tooltip("Added to the haze's end distance (m): at least half the size of the biggest object, so nothing whose " +
                 "edge could still show is dropped.")]
        [Min(0f)] public float margin = 5f;

        int layer = -1;
        readonly Dictionary<Camera, float> applied = new Dictionary<Camera, float>();

        void Awake()
        {
            layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogWarning("UnderwaterDistanceCull: no layer '" + layerName + "', the reef is drawn at every distance.", this);
                return;
            }
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCameraRenders;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRenders;
            foreach (KeyValuePair<Camera, float> pair in applied)
                if (pair.Key != null && pair.Value > 0f) SetDistance(pair.Key, 0f);
            applied.Clear();
        }

        // Right before the camera culls: the haze distance while it is wholly under the water, 0 (= its far plane) else.
        void BeforeCameraRenders(ScriptableRenderContext context, Camera cam)
        {
            if (layer < 0 || cam.cameraType != CameraType.Game) return;
            float distance = UnderwaterCullDistance(cam);
            if (applied.TryGetValue(cam, out float current) && current == distance) return;
            SetDistance(cam, distance);
            applied[cam] = distance;
        }

        float UnderwaterCullDistance(Camera cam)
        {
            Vector3 p = cam.transform.position;
            WaterSurface water = WaterSurface.FindAt(p);
            // no haze without the underwater effect (the cheap fallback water): everything stays
            Material m = water != null && water.UnderwaterEffectOn ? water.ActiveMaterial : null;
            if (m == null) return 0f;
            // the band around the mean level the waves reach: the same one the water's shaders split the view in
            float band = m.GetFloat(WaveAmplitudeId) * 2f + m.GetFloat(CellWaveHeightId) + 0.4f + cam.nearClipPlane;
            if (p.y > water.WaterLevel - band) return 0f;
            float hazeEnd = Mathf.Min(m.GetFloat(MaxDistanceId), Mathf.Max(m.GetFloat(FogEndId), 1f));
            return hazeEnd + margin;
        }

        void SetDistance(Camera cam, float distance)
        {
            float[] distances = cam.layerCullDistances;   // a copy: the other layers keep theirs
            distances[layer] = distance;
            cam.layerCullDistances = distances;
            if (distance > 0f) cam.layerCullSpherical = true;   // the haze grows with the distance in every direction
        }
    }
}

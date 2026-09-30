using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Water
{
    /// <summary>
    /// Blends the water between a calm and a storm preset (two materials using AKI/Water).
    /// Everything follows the blend: waves, foam, colours, the underwater look and the swimming physics
    /// (they all read the water's active material).
    /// <code>
    /// GetComponent&lt;WaterWeather&gt;().SetStorminess(1f, 20f);   // storm rolls in over 20 seconds
    /// </code>
    /// The blended material only exists at runtime and is never saved into the scene.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaterSurface))]
    public class WaterWeather : MonoBehaviour
    {
        [Tooltip("Preset used at storminess 0.")]
        public Material calm;
        [Tooltip("Preset used at storminess 1.")]
        public Material storm;
        [Range(0f, 1f)] public float storminess;

        WaterSurface surface;
        Material blended;
        float applied = -1f;
        float target;
        float speed;

        /// <summary>Smoothly changes the weather. duration 0 = instantly.</summary>
        public void SetStorminess(float value, float duration)
        {
            target = Mathf.Clamp01(value);
            if (duration <= 0f)
            {
                storminess = target;
                speed = 0f;
            }
            else speed = Mathf.Abs(target - storminess) / duration;
        }

        void OnEnable()
        {
            surface = GetComponent<WaterSurface>();
            target = storminess;
            applied = -1f;
            RenderPipelineManager.beginContextRendering += OnBeginRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            if (surface != null) surface.SetMaterialOverride(null);
            if (blended != null) DestroyImmediate(blended);
            blended = null;
        }

        void OnValidate()
        {
            target = storminess;
            speed = 0f;
        }

        void Update()
        {
            if (speed > 0f)
            {
                storminess = Mathf.MoveTowards(storminess, target, speed * Time.deltaTime);
                if (Mathf.Approximately(storminess, target)) speed = 0f;
            }
            if (!Mathf.Approximately(storminess, applied)) Apply();
        }

        // The blend is re-applied right before every frame is rendered, so it survives anything that resets
        // runtime materials in the editor (asset imports etc.). Costs a few hundred property copies per frame.
        void OnBeginRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            Apply();
        }

        void Apply()
        {
            applied = storminess;
            if (surface == null || calm == null || storm == null) return;

            // fully calm: draw with the calm asset itself (keeps the scene/prefab referencing a real material)
            if (storminess <= 0.0001f)
            {
                surface.SetMaterialOverride(calm == surface.material ? null : calm);
                return;
            }

            if (blended == null)
            {
                blended = new Material(calm) { name = "Water (weather blend)", hideFlags = HideFlags.HideAndDontSave };
            }
            // floats and colours are interpolated; shader keywords come from the calm preset (both use the same toggles)
            blended.Lerp(calm, storm, Mathf.SmoothStep(0f, 1f, storminess));
            surface.SetMaterialOverride(blended);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// Melts an object's meshes away in noise patches over a few seconds (AKI/DissolveLit). Its materials are swapped
    /// for dissolve copies with the same textures and values, so it looks unchanged until the holes open.
    /// Particles, lines and trails are left alone. When done the meshes stay hidden and <c>onDone</c> is called.
    /// </summary>
    public class Dissolver : MonoBehaviour
    {
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int ScaleId = Shader.PropertyToID("_DissolveScale");
        static readonly int EdgeId = Shader.PropertyToID("_DissolveEdge");
        static readonly int EdgeColorId = Shader.PropertyToID("_DissolveEdgeColor");

        readonly List<Material> instances = new List<Material>();
        readonly List<Renderer> renderers = new List<Renderer>();
        float duration;
        float progress;
        Action onDone;

        public float Progress => progress;

        /// <summary>
        /// Starts melting <paramref name="target"/>: <paramref name="noiseScale"/> noise cells per metre, rim in
        /// <paramref name="edgeColor"/> (black = no glow).
        /// </summary>
        public static Dissolver Begin(GameObject target, Shader shader, float seconds, float noiseScale, Color edgeColor, Action onDone = null)
        {
            if (shader == null) shader = Shader.Find("AKI/DissolveLit");
            var dissolver = target.AddComponent<Dissolver>();
            dissolver.duration = Mathf.Max(0.01f, seconds);
            dissolver.onDone = onDone;

            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Material[] source = r.sharedMaterials;
                var swapped = new Material[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    swapped[i] = Convert(source[i], shader, noiseScale, edgeColor);
                    dissolver.instances.Add(swapped[i]);
                }
                r.sharedMaterials = swapped;
                dissolver.renderers.Add(r);
            }
            return dissolver;
        }

        static Material Convert(Material source, Shader shader, float noiseScale, Color edgeColor)
        {
            var m = new Material(shader) { name = (source != null ? source.name : "Default") + " (dissolve)" };
            if (source != null)
            {
                m.CopyPropertiesFromMaterial(source);
                foreach (string keyword in source.shaderKeywords) m.EnableKeyword(keyword);
                // built-in style materials
                if (!source.HasProperty("_BaseMap") && source.HasProperty("_MainTex")) m.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                if (!source.HasProperty("_BaseColor") && source.HasProperty("_Color")) m.SetColor("_BaseColor", source.GetColor("_Color"));
            }
            m.SetFloat(DissolveId, 0f);
            m.SetFloat(ScaleId, noiseScale);
            m.SetFloat(EdgeId, 0.05f);
            m.SetColor(EdgeColorId, edgeColor);
            return m;
        }

        void Update()
        {
            if (progress >= 1f) return;
            progress = Mathf.Min(1f, progress + Time.deltaTime / duration);
            float value = Mathf.SmoothStep(0f, 1f, progress);
            foreach (Material m in instances) m.SetFloat(DissolveId, value);

            if (progress < 1f) return;
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = false;
            onDone?.Invoke();
        }

        void OnDestroy()
        {
            foreach (Material m in instances) Destroy(m);
        }
    }
}

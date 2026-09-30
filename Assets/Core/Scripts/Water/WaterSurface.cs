using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AKI.Water
{
    /// <summary>
    /// Builds a flat grid mesh for the AKI/Water shader (all wave motion happens on the GPU) and,
    /// optionally, keeps it centred under a camera so a finite grid reads as an endless ocean.
    /// The grid snaps to its own cell size, so vertices never "swim" over the world-space waves.
    ///
    /// Also owns the full-screen "under water" effect (fog, caustics, light shafts) that switches on
    /// by itself when a camera dips below the wavy surface, and swaps to a cheap material/mesh when the
    /// active URP asset has no depth/opaque texture (mobile).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterSurface : MonoBehaviour
    {
        const string UnderwaterChildName = "~UnderwaterEffect";

        [Header("Mesh")]
        [Min(10f)] public float size = 400f;
        [Range(16, 512)] public int resolution = 256;
        [Tooltip("Grid resolution used together with the fallback material.")]
        [Range(16, 256)] public int fallbackResolution = 192;

        [Header("Follow")]
        [Tooltip("Keeps the grid centred under this transform (snapped to the cell size). Leave empty for a fixed pool/lake.")]
        public Transform followTarget;
        public bool followMainCamera = true;

        [Header("Materials")]
        public Material material;
        [Tooltip("Used when the active URP asset does not provide Depth + Opaque textures.")]
        public Material fallbackMaterial;

        [Header("Underwater")]
        [Tooltip("Full-screen effect used while the camera is below the surface. Uses the AKI/WaterUnderwater shader.")]
        public Shader underwaterShader;
        public bool underwaterEffect = true;

        [Header("Underwater State (audio, gameplay)")]
        [Tooltip("Whose position counts as \"the player\". Empty = the AudioListener, or the main camera if there is none.")]
        public Transform listenerOverride;
        [Tooltip("Height (m) around the mean water level over which UnderwaterAmount fades 0 -> 1. Hides the wave height at the waterline so sounds crossfade instead of flickering.")]
        [Min(0.01f)] public float transitionHeight = 0.6f;
        [Tooltip("Extra depth (m) needed to flip IsUnderwater back, prevents flicker at the surface.")]
        [Min(0f)] public float hysteresis = 0.1f;
        [Tooltip("Invoked when the listener goes below (true) or above (false) the surface.")]
        public UnityEvent<bool> onUnderwaterChanged = new UnityEvent<bool>();

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Mesh mesh;
        int builtResolution;
        float builtSize;
        float baseY;
        bool baseYSet;
        RenderPipelineAsset lastAsset;
        bool underwaterDirty = true;

        GameObject underwaterGo;
        MeshRenderer underwaterRenderer;
        Mesh underwaterMesh;
        Material underwaterMaterial;

        static readonly string[] SyncedKeywords = { "_CAUSTICS", "_GODRAYS" };
        static readonly List<WaterSurface> instances = new List<WaterSurface>();
        static readonly int RayTexId = Shader.PropertyToID("_WaterRayTex");

        AudioListener cachedListener;
        float nextListenerSearch;
        bool underwater;
        float underwaterAmount;
        float listenerDepth;

        // ------------------------------------------------------------------ underwater state API

        /// <summary>All active water surfaces.</summary>
        public static IReadOnlyList<WaterSurface> Instances => instances;

        /// <summary>Raised when any water's underwater state changes (surface, isUnderwater).</summary>
        public static event Action<WaterSurface, bool> AnyUnderwaterChanged;

        /// <summary>True while the listener (player camera / audio listener) is below this water's surface.</summary>
        public bool IsUnderwater => underwater;

        /// <summary>0 above the surface .. 1 clearly below it, smooth over <see cref="transitionHeight"/>. Use it to crossfade sounds.</summary>
        public float UnderwaterAmount => underwaterAmount;

        /// <summary>Metres between the mean water level and the listener (positive = below the surface).</summary>
        public float ListenerDepth => listenerDepth;

        /// <summary>Mean water level (world Y). Waves move the real surface a little above and below it.</summary>
        public float WaterLevel => transform.position.y;

        /// <summary>True if the world point is inside this water's area and below its mean level.</summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            return IsInsideArea(worldPoint) && worldPoint.y < WaterLevel;
        }

        /// <summary>True if the point is under any water surface.</summary>
        public static bool IsPointUnderwater(Vector3 worldPoint)
        {
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].ContainsPoint(worldPoint)) return true;
            return false;
        }

        /// <summary>Highest UnderwaterAmount of any water at this position (0..1).</summary>
        public static float GetUnderwaterAmount(Vector3 worldPoint)
        {
            float best = 0f;
            for (int i = 0; i < instances.Count; i++)
                best = Mathf.Max(best, instances[i].AmountAt(worldPoint));
            return best;
        }

        /// <summary>True if the point is inside this water's square area (ignores height).</summary>
        public bool IsInsideArea(Vector3 p)
        {
            Vector3 c = transform.position;
            float half = size * 0.5f;
            return Mathf.Abs(p.x - c.x) <= half && Mathf.Abs(p.z - c.z) <= half;
        }

        float AmountAt(Vector3 p)
        {
            if (!IsInsideArea(p)) return 0f;
            return Mathf.Clamp01((WaterLevel - p.y) / (2f * transitionHeight) + 0.5f);
        }

        Vector3 ListenerPosition()
        {
            if (listenerOverride != null) return listenerOverride.position;
            if (cachedListener == null && Time.unscaledTime >= nextListenerSearch)
            {
                cachedListener = FindFirstObjectByType<AudioListener>();
                nextListenerSearch = Time.unscaledTime + 1f;
            }
            if (cachedListener != null) return cachedListener.transform.position;
            return Camera.main != null ? Camera.main.transform.position : transform.position + Vector3.up * 1000f;
        }

        void UpdateUnderwaterState()
        {
            Vector3 p = ListenerPosition();
            listenerDepth = WaterLevel - p.y;
            underwaterAmount = AmountAt(p);

            // hysteresis: needs a bit more depth to switch on than to stay on
            bool inArea = IsInsideArea(p);
            bool now = underwater
                ? inArea && listenerDepth > -hysteresis
                : inArea && listenerDepth > 0f;

            if (now == underwater) return;
            underwater = now;
            if (!Application.isPlaying) return;
            onUnderwaterChanged.Invoke(underwater);
            AnyUnderwaterChanged?.Invoke(this, underwater);
        }

        void OnEnable()
        {
            if (!instances.Contains(this)) instances.Add(this);
            Shader.SetGlobalTexture(RayTexId, WaterTextures.RayField);
            Setup();
            Rebuild(true);
            ApplyMaterial();
            RequestUnderwaterSetup();
        }

        void OnDisable()
        {
            instances.Remove(this);
            underwater = false;
            underwaterAmount = 0f;
            if (mesh != null)
            {
                DestroyImmediate(mesh);
                mesh = null;
            }
            TeardownUnderwater();
        }

        void OnValidate()
        {
            baseYSet = false;
            if (isActiveAndEnabled)
            {
                Setup();
                Rebuild(false);
                ApplyMaterial();
                RequestUnderwaterSetup();
            }
        }

        // Child objects can't be created during Awake/OnValidate, so this waits for the next editor tick
        // (LateUpdate alone is not enough: in edit mode it only runs when something in the scene changes).
        void RequestUnderwaterSetup()
        {
            underwaterDirty = true;
        #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled && underwaterDirty)
                    {
                        underwaterDirty = false;
                        SetupUnderwater();
                    }
                };
            }
        #endif
        }

        void Setup()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            if (!baseYSet)
            {
                baseY = transform.position.y;
                baseYSet = true;
            }
        }

        /// <summary>Re-applies mesh and material after fields were changed from code.</summary>
        public void Refresh()
        {
            Setup();
            Rebuild(false);
            ApplyMaterial();
            SetupUnderwater();
        }

        void LateUpdate()
        {
            // The pipeline asset can change at runtime (quality settings).
            if (lastAsset != GraphicsSettings.currentRenderPipeline)
            {
                Rebuild(false);
                ApplyMaterial();
                underwaterDirty = true;
            }

            if (underwaterDirty || (underwaterGo == null && underwaterEffect))
            {
                underwaterDirty = false;
                SetupUnderwater();
            }

            Transform target = followTarget;
            if (target == null && followMainCamera && Camera.main != null)
                target = Camera.main.transform;
            if (target != null)
            {
                float cell = size / Mathf.Max(1, builtResolution);
                Vector3 p = target.position;
                p.x = Mathf.Round(p.x / cell) * cell;
                p.z = Mathf.Round(p.z / cell) * cell;
                p.y = baseY;
                transform.position = p;
            }

            PublishMeshCell();
            UpdateUnderwaterState();
            SyncUnderwaterMaterial();
        }

        bool SceneTexturesAvailable()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return urp == null || (urp.supportsCameraDepthTexture && urp.supportsCameraOpaqueTexture);
        }

        bool UsingFallback => fallbackMaterial != null && !SceneTexturesAvailable();

        /// <summary>The material currently used (the fallback on platforms without depth/opaque textures).</summary>
        public Material ActiveMaterial => UsingFallback ? fallbackMaterial : material;

        void ApplyMaterial()
        {
            lastAsset = GraphicsSettings.currentRenderPipeline;
            Material m = ActiveMaterial;
            if (m != null && meshRenderer.sharedMaterial != m) meshRenderer.sharedMaterial = m;
        }

        void Rebuild(bool force)
        {
            int res = UsingFallback ? Mathf.Min(fallbackResolution, resolution) : resolution;
            if (!force && mesh != null && res == builtResolution && Mathf.Approximately(size, builtSize)) return;

            if (mesh != null) DestroyImmediate(mesh);
            mesh = BuildGrid(size, res);
            meshFilter.sharedMesh = mesh;
            builtResolution = res;
            builtSize = size;
            PublishMeshCell();
        }

        // Tells the shader how coarse the grid is, so it can drop waves the grid cannot represent.
        void PublishMeshCell()
        {
            Shader.SetGlobalFloat("_WaterMeshCell", size / Mathf.Max(1, builtResolution));
        }

        // ------------------------------------------------------------------ underwater

        void SetupUnderwater()
        {
            // needs depth + opaque texture, so it is PC-only like the rest of the heavy effects
            bool wanted = underwaterEffect && !UsingFallback && (underwaterShader != null || Shader.Find("AKI/WaterUnderwater") != null);
            if (!wanted)
            {
                if (underwaterRenderer != null) underwaterRenderer.enabled = false;
                return;
            }

            if (underwaterGo == null)
            {
                Transform existing = transform.Find(UnderwaterChildName);
                if (existing != null) DestroyImmediate(existing.gameObject);   // stale copy from a domain reload

                underwaterGo = new GameObject(UnderwaterChildName) { hideFlags = HideFlags.HideAndDontSave };
                underwaterGo.transform.SetParent(transform, false);

                // three vertices are enough for a full-screen triangle; the shader ignores their positions,
                // the huge bounds only keep the renderer from being culled
                underwaterMesh = new Mesh { name = "UnderwaterTriangle", hideFlags = HideFlags.HideAndDontSave };
                underwaterMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
                underwaterMesh.triangles = new[] { 0, 1, 2 };
                underwaterMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

                underwaterGo.AddComponent<MeshFilter>().sharedMesh = underwaterMesh;
                underwaterRenderer = underwaterGo.AddComponent<MeshRenderer>();
                underwaterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                underwaterRenderer.receiveShadows = false;
                underwaterRenderer.lightProbeUsage = LightProbeUsage.Off;
                underwaterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                underwaterRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            if (underwaterMaterial == null)
            {
                Shader s = underwaterShader != null ? underwaterShader : Shader.Find("AKI/WaterUnderwater");
                underwaterMaterial = new Material(s) { name = "WaterUnderwater (instance)", hideFlags = HideFlags.HideAndDontSave };
            }

            underwaterRenderer.sharedMaterial = underwaterMaterial;
            underwaterRenderer.enabled = true;
            SyncUnderwaterMaterial();
        }

        // Feeds the water material's values (same property names) and keyword state to the underwater effect.
        void SyncUnderwaterMaterial()
        {
            Material src = ActiveMaterial;
            if (underwaterMaterial == null || src == null) return;

            underwaterMaterial.CopyPropertiesFromMaterial(src);
            underwaterMaterial.renderQueue = (int)RenderQueue.Transparent - 100;
            foreach (string kw in SyncedKeywords)
            {
                if (src.IsKeywordEnabled(kw)) underwaterMaterial.EnableKeyword(kw);
                else underwaterMaterial.DisableKeyword(kw);
            }
        }

        void TeardownUnderwater()
        {
            if (underwaterGo != null) DestroyImmediate(underwaterGo);
            if (underwaterMesh != null) DestroyImmediate(underwaterMesh);
            if (underwaterMaterial != null) DestroyImmediate(underwaterMaterial);
            underwaterGo = null;
            underwaterRenderer = null;
            underwaterMesh = null;
            underwaterMaterial = null;
        }

        // ------------------------------------------------------------------ grid

        static Mesh BuildGrid(float size, int quads)
        {
            int verts = quads + 1;
            var positions = new Vector3[verts * verts];
            float half = size * 0.5f;
            float step = size / quads;

            for (int z = 0; z < verts; z++)
            for (int x = 0; x < verts; x++)
                positions[z * verts + x] = new Vector3(x * step - half, 0f, z * step - half);

            var indices = new int[quads * quads * 6];
            int i = 0;
            for (int z = 0; z < quads; z++)
            for (int x = 0; x < quads; x++)
            {
                int a = z * verts + x;
                int b = a + 1;
                int c = a + verts;
                int d = c + 1;
                indices[i++] = a; indices[i++] = c; indices[i++] = b;
                indices[i++] = b; indices[i++] = c; indices[i++] = d;
            }

            var m = new Mesh
            {
                name = "WaterGrid",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = verts * verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            m.vertices = positions;
            m.triangles = indices;
            // Vertices are displaced on the GPU: give the bounds generous vertical room so nothing is culled.
            m.bounds = new Bounds(Vector3.zero, new Vector3(size, 40f, size));
            return m;
        }
    }
}

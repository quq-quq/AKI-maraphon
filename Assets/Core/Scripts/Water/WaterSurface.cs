using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AKI.Water
{
    /// <summary>
    /// Builds the flat geometry for the AKI/Water shader (all wave motion happens on the GPU) and,
    /// optionally, keeps it centred under a camera so a finite grid reads as an endless ocean.
    /// A pool / lake is one grid; the open ocean (<see cref="expandingGrid"/>) is a geometry clipmap around the camera
    /// (<see cref="WaterClipmap"/>): fine cells by the camera, doubling level by level out to the horizon.
    /// Everything snaps to its own cell size, so vertices never "swim" over the world-space waves.
    /// Right before each camera renders it also works out the surface right around that camera
    /// (_WaterCamPlane), which the surface, the underwater overlay and the lens draw the waterline from.
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
        [Tooltip("Open ocean: a clipmap around the camera - small cells near it, doubling level by level out to the horizon.")]
        public bool expandingGrid = false;
        [Tooltip("Open ocean: cell size (m) next to the camera.")]
        [Range(0.03f, 1f)] public float clipCell = 0.15f;
        [Tooltip("Open ocean: vertices per level ~ (8 x this)^2. Lower = faster, coarser.")]
        [Range(4, 40)] public int clipDensity = 20;
        [Tooltip("Open ocean: levels, each with cells twice the size of the one inside it.")]
        [Range(2, 10)] public int clipLevels = 8;
        [Tooltip("Pool / lake grid only (unused by the open ocean): distance (m) over which the cell size doubles.")]
        [Min(5f)] public float detailRadius = 60f;
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

        [Header("Height Probes (physics)")]
        [Tooltip("WaterHeight.compute - evaluates the wave maths for WaterProbe (swimming, floating objects).")]
        public ComputeShader heightCompute;

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

        WaterClipmap clipmap;
        RenderTexture camPlane;
        int camPlaneKernel = -1;
        static readonly int CamPlaneId = Shader.PropertyToID("_WaterCamPlane");

        GameObject underwaterGo;
        MeshRenderer underwaterRenderer;
        Mesh underwaterMesh;
        Material underwaterMaterial;

        static readonly string[] SyncedKeywords = { "_CAUSTICS", "_GODRAYS", "_FFT_WAVES" };
        static readonly List<WaterSurface> instances = new List<WaterSurface>();
        static readonly int RayTexId = Shader.PropertyToID("_WaterRayTex");

        // ------------------------------------------------------------------ height probes
        const int MaxProbes = 64;
        static readonly List<WaterProbe> probes = new List<WaterProbe>();
        readonly List<WaterProbe> inFlight = new List<WaterProbe>();
        readonly Vector4[] probePointData = new Vector4[MaxProbes];
        ComputeBuffer probePoints;
        ComputeBuffer probeResults;
        bool readbackPending;
        float readbackStart;
        float inFlightTime;     // game time the heights being read back describe
        float readbackLatency = 0.05f;
        int probeKernel = -1;
        Shader propertyCacheShader;
        string[] floatProps = new string[0];
        string[] vectorProps = new string[0];

        internal static void RegisterProbe(WaterProbe probe)
        {
            if (probe != null && !probes.Contains(probe)) probes.Add(probe);
        }

        internal static void UnregisterProbe(WaterProbe probe)
        {
            probes.Remove(probe);
        }

        /// <summary>The water whose area contains the point, or null.</summary>
        public static WaterSurface FindAt(Vector3 worldPoint)
        {
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].IsInsideArea(worldPoint)) return instances[i];
            return null;
        }

        void UpdateProbes()
        {
            if (!Application.isPlaying) return;

            for (int i = 0; i < probes.Count; i++)
            {
                WaterProbe p = probes[i];
                if (p.Water == null || !p.Water.isActiveAndEnabled || !p.Water.IsInsideArea(p.position))
                {
                    p.Water = FindAt(p.position);
                    p.HasData = false;
                }
            }

            if (readbackPending || heightCompute == null || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) return;
            Material src = ActiveMaterial;
            if (src == null) return;

            inFlight.Clear();
            for (int i = 0; i < probes.Count && inFlight.Count < MaxProbes; i++)
            {
                if (probes[i].Water != this) continue;
                probePointData[inFlight.Count] = probes[i].position;
                inFlight.Add(probes[i]);
            }
            if (inFlight.Count == 0) return;

            if (probePoints == null)
            {
                probePoints = new ComputeBuffer(MaxProbes, 16);
                probeResults = new ComputeBuffer(MaxProbes, 16);
            }
            if (probeKernel < 0) probeKernel = heightCompute.FindKernel("SampleHeights");

            CopyMaterialToCompute(src);
            probePoints.SetData(probePointData, 0, 0, inFlight.Count);
            heightCompute.SetInt("_ProbeCount", inFlight.Count);
            // results arrive a few frames later: evaluate the waves at the time they will be used
            heightCompute.SetFloat("_ProbeTime", Time.timeSinceLevelLoad + readbackLatency);
            heightCompute.SetFloat("_ProbeWaterLevel", WaterLevel);
            PublishMeshCell(heightCompute);
            // the FFT textures hold the sea of this frame, the Gerstner maths is evaluated ahead by the latency
            inFlightTime = Time.time + (SetupProbeWaves(src) ? 0f : readbackLatency);
            heightCompute.SetBuffer(probeKernel, "_ProbePoints", probePoints);
            heightCompute.SetBuffer(probeKernel, "_ProbeResults", probeResults);
            heightCompute.Dispatch(probeKernel, (inFlight.Count + 63) / 64, 1, 1);

            readbackPending = true;
            readbackStart = Time.realtimeSinceStartup;
            AsyncGPUReadback.Request(probeResults, inFlight.Count * 16, 0, OnProbeReadback);
        }

        // FFT ocean: the probes read the same displacement textures the surface is drawn with
        bool SetupProbeWaves(Material src) => SetupComputeWaves(src, probeKernel);

        bool SetupComputeWaves(Material src, int kernel)
        {
            var fftKeyword = new LocalKeyword(heightCompute, "_FFT_WAVES");
            OceanFFT ocean = OceanFFT.Active;
            bool fft = src.IsKeywordEnabled("_FFT_WAVES") && ocean != null && ocean.GetDisplacement(0) != null;
            heightCompute.SetKeyword(fftKeyword, fft);
            if (!fft) return false;

            for (int i = 0; i < 3; i++)
            {
                heightCompute.SetTexture(kernel, "_OceanDisp" + i, ocean.GetDisplacement(i));
                heightCompute.SetTexture(kernel, "_OceanDeriv" + i, ocean.GetDerivatives(i));
            }
            heightCompute.SetVector("_OceanLengthScales", ocean.LengthScales);
            return true;
        }

        // ------------------------------------------------------------------ the surface around each camera

        // Right before a camera renders: the plane of the surface right where it is (WaterWaves.hlsl,
        // WaterCameraPlane), into a 1x1 texture every shader that draws the waterline reads. Once per camera instead
        // of in every vertex of the surface.
        void BeforeCameraRenders(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || heightCompute == null || !SystemInfo.supportsComputeShaders) return;
            Vector3 p = cam.transform.position;
            // the water the camera is in (or the first one, if it is in none) answers for it
            WaterSurface owner = FindAt(p);
            if (owner == null && instances.Count > 0) owner = instances[0];
            if (owner != this) return;
            Material src = ActiveMaterial;
            if (src == null) return;

            if (camPlane == null)
            {
                camPlane = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "WaterCamPlane"
                };
                camPlane.Create();
            }
            if (camPlaneKernel < 0) camPlaneKernel = heightCompute.FindKernel("CameraPlane");

            CopyMaterialToCompute(src);
            SetupComputeWaves(src, camPlaneKernel);
            PublishMeshCell(heightCompute);
            heightCompute.SetFloat("_ProbeWaterLevel", WaterLevel);
            heightCompute.SetVector("_CamPlanePos", p);
            Vector4 time = Shader.GetGlobalVector("_Time");
            heightCompute.SetFloat("_CamPlaneTime", time.y > 0f ? time.y : Time.timeSinceLevelLoad);
            // the clipmap's cells by the camera are tiny: the exact waves; one coarse grid: its own triangle
            heightCompute.SetFloat("_CamPlaneExact", clipmap != null ? 1f : 0f);
            heightCompute.SetTexture(camPlaneKernel, "_CamPlaneOut", camPlane);
            heightCompute.Dispatch(camPlaneKernel, 1, 1, 1);
            Shader.SetGlobalTexture(CamPlaneId, camPlane);
        }

        void OnProbeReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (this == null || request.hasError) return;

            readbackLatency = Mathf.Lerp(readbackLatency, Time.realtimeSinceStartup - readbackStart, 0.2f);
            var data = request.GetData<Vector4>();
            for (int i = 0; i < inFlight.Count && i < data.Length; i++)
            {
                WaterProbe p = inFlight[i];
                if (p.Water != this) continue;
                float sampleDt = inFlightTime - p.SampleTime;
                p.HeightRate = p.HasData && sampleDt > 1e-3f ? (data[i].x - p.Height) / sampleDt : 0f;
                p.SampleTime = inFlightTime;
                p.Height = data[i].x;
                p.Normal = new Vector3(data[i].y, data[i].z, data[i].w);
                p.HasData = true;
            }
        }

        // Every float / colour / vector of the water material -> same-named globals of the compute shader.
        void CopyMaterialToCompute(Material src)
        {
            if (propertyCacheShader != src.shader)
            {
                propertyCacheShader = src.shader;
                var floats = new List<string>();
                var vectors = new List<string>();
                for (int i = 0; i < src.shader.GetPropertyCount(); i++)
                {
                    var type = src.shader.GetPropertyType(i);
                    if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range) floats.Add(src.shader.GetPropertyName(i));
                    else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector) vectors.Add(src.shader.GetPropertyName(i));
                }
                floatProps = floats.ToArray();
                vectorProps = vectors.ToArray();
            }
            foreach (string n in floatProps) heightCompute.SetFloat(n, src.GetFloat(n));
            foreach (string n in vectorProps) heightCompute.SetVector(n, src.GetVector(n));
        }

        void ReleaseProbeBuffers()
        {
            probePoints?.Release();
            probeResults?.Release();
            probePoints = null;
            probeResults = null;
            readbackPending = false;
        }

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
            RenderPipelineManager.beginCameraRendering += BeforeCameraRenders;
            Shader.SetGlobalTexture(RayTexId, WaterTextures.RayField);
            Setup();
            Rebuild(true);
            ApplyMaterial();
            RequestUnderwaterSetup();
        }

        void OnDisable()
        {
            instances.Remove(this);
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRenders;
            ReleaseProbeBuffers();
            clipmap?.Destroy();
            clipmap = null;
            if (camPlane != null)
            {
                camPlane.Release();
                DestroyImmediate(camPlane);
                camPlane = null;
            }
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
                validating = true;    // no child objects can be made in here: the clipmap is rebuilt a moment later
                Rebuild(false);
                validating = false;
                ApplyMaterial();
                RequestUnderwaterSetup();
            }
        }

        bool validating;
        bool geometryDirty;

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

            if (geometryDirty) Rebuild(false);

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
                float cell = InnerCell;
                Vector3 p = target.position;
                p.x = Mathf.Round(p.x / cell) * cell;
                p.z = Mathf.Round(p.z / cell) * cell;
                p.y = baseY;
                transform.position = p;
            }
            if (clipmap != null) clipmap.UpdatePositions(target != null ? target.position : transform.position, WaterLevel);

            PublishMeshCell();
            UpdateProbes();
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
            clipmap?.SetMaterial(m);
        }

        void Rebuild(bool force)
        {
            if (validating)
            {
                // no objects can be made or switched in OnValidate: rebuild a moment later
                geometryDirty = true;
            #if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled && geometryDirty) Rebuild(false); };
            #endif
                return;
            }
            geometryDirty = false;
            if (expandingGrid)
            {
                // the open ocean: the clipmap draws, the component's own renderer stays off
                if (mesh != null)
                {
                    DestroyImmediate(mesh);
                    mesh = null;
                    meshFilter.sharedMesh = null;
                }
                meshRenderer.enabled = false;
                if (clipmap != null && !force && clipmap.Matches(clipCell, clipDensity, clipLevels, size)) return;
                if (clipmap == null) clipmap = new WaterClipmap(transform);
                clipmap.Build(clipCell, clipDensity, clipLevels, size, ActiveMaterial);
                builtExpanding = true;
                builtSize = size;
                PublishMeshCell();
                return;
            }
            if (clipmap != null)
            {
                clipmap.Destroy();
                clipmap = null;
            }
            meshRenderer.enabled = true;
            if (meshFilter.sharedMesh == null) force = true;

            int res = UsingFallback ? Mathf.Min(fallbackResolution, resolution) : resolution;
            if (!force && mesh != null && res == builtResolution && Mathf.Approximately(size, builtSize)
                && expandingGrid == builtExpanding && Mathf.Approximately(detailRadius, builtRadius)) return;

            if (mesh != null) DestroyImmediate(mesh);
            mesh = BuildGrid(size, res, expandingGrid, detailRadius);
            meshFilter.sharedMesh = mesh;
            builtResolution = res;
            builtSize = size;
            builtExpanding = expandingGrid;
            builtRadius = detailRadius;
            PublishMeshCell();
        }

        bool builtExpanding;
        float builtRadius;

        /// <summary>Size (m) of the grid cells at the centre of the mesh (next to the camera for the open ocean).</summary>
        public float InnerCell => clipmap != null ? clipmap.InnerCell : InnerCellSize(size, Mathf.Max(1, builtResolution), builtExpanding, builtRadius);

        // distance (m) over which the cells grow by one inner cell
        float MeshGrowth => clipmap != null ? clipmap.Growth : builtExpanding ? builtRadius : 1e9f;

        static float InnerCellSize(float size, int quads, bool expanding, float radius)
        {
            if (!expanding) return size / quads;
            float k = Mathf.Log(1f + size * 0.5f / radius);
            return radius * k * 2f / quads;
        }

        // Tells the shaders how coarse the grid is (at the centre and how it grows), so they can drop waves the grid
        // cannot represent instead of aliasing them into spikes.
        void PublishMeshCell()
        {
            Shader.SetGlobalFloat("_WaterMeshCell", InnerCell);
            Shader.SetGlobalFloat("_WaterMeshGrowth", MeshGrowth);
            Shader.SetGlobalVector("_WaterMeshCenter", transform.position);
        }

        void PublishMeshCell(ComputeShader cs)
        {
            cs.SetFloat("_WaterMeshCell", InnerCell);
            cs.SetFloat("_WaterMeshGrowth", MeshGrowth);
            cs.SetVector("_WaterMeshCenter", transform.position);
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

        // Uniform grid, or an "expanding" one: coordinate u in [-1, 1] maps to radius * (e^(k|u|) - 1), so the cell size
        // grows linearly with distance from the centre (inner cell * (1 + distance / radius)).
        static Mesh BuildGrid(float size, int quads, bool expanding, float radius)
        {
            int verts = quads + 1;
            var positions = new Vector3[verts * verts];
            float half = size * 0.5f;
            float k = Mathf.Log(1f + half / radius);

            float Coord(int i)
            {
                float u = i / (float)quads * 2f - 1f;
                if (!expanding) return u * half;
                return Mathf.Sign(u) * radius * (Mathf.Exp(k * Mathf.Abs(u)) - 1f);
            }

            for (int z = 0; z < verts; z++)
            for (int x = 0; x < verts; x++)
                positions[z * verts + x] = new Vector3(Coord(x), 0f, Coord(z));

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

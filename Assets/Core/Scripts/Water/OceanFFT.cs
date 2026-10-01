using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Water
{
    /// <summary>
    /// FFT ocean (Tessendorf). Builds the sea from a JONSWAP spectrum of real wind waves and evaluates it on the GPU
    /// every frame with an inverse FFT, in three cascades of different size (so nothing visibly tiles):
    /// long swells, medium waves and small chop. Produces, per cascade:
    ///   displacement (xyz + "turbulence" used for foam) and slopes/derivatives (normals, choppiness).
    /// The results are published as global shader textures that AKI/Water (keyword _FFT_WAVES), the underwater
    /// effect, the lens and the height probes read.
    /// Put it next to WaterSurface. Wind speed is the main storm control.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OceanFFT : MonoBehaviour
    {
        const int Size = 256;

        [Tooltip("OceanFFT.compute")]
        public ComputeShader fftCompute;

        [Header("Wind")]
        [Tooltip("m/s. ~5 calm, ~10 fresh breeze, ~18 storm, ~25 severe storm.")]
        [Range(1f, 35f)] public float windSpeed = 24f;
        [Range(0f, 360f)] public float windDirection = 30f;
        [Tooltip("Distance (km) the wind has blown over open water. Longer = bigger, longer swells.")]
        [Range(1f, 1000f)] public float fetchKm = 300f;

        [Header("Shape")]
        [Tooltip("Overall wave height multiplier.")]
        [Range(0f, 3f)] public float waveScale = 1f;
        [Tooltip("How sharp and pinched the crests get (horizontal displacement).")]
        [Range(0f, 1.6f)] public float choppiness = 1.3f;
        [Tooltip("0 = waves from all directions, 1 = all follow the wind.")]
        [Range(0f, 1f)] public float spreadBlend = 0.85f;
        [Range(0f, 1f)] public float swell = 0.2f;
        [Range(1f, 7f)] public float peakEnhancement = 3.3f;
        [Tooltip("Removes the very shortest waves (metres).")]
        [Range(0f, 0.2f)] public float shortWavesFade = 0.01f;

        [Header("Foam")]
        [Tooltip("How fast foam from breaking crests fades away.")]
        [Range(0.05f, 3f)] public float foamDecay = 0.8f;

        [Header("Cascades (patch size in metres)")]
        public float lengthScale0 = 250f;
        public float lengthScale1 = 17f;
        public float lengthScale2 = 5f;

        static readonly int[] DispIds = { Shader.PropertyToID("_OceanDisp0"), Shader.PropertyToID("_OceanDisp1"), Shader.PropertyToID("_OceanDisp2") };
        static readonly int[] DerivIds = { Shader.PropertyToID("_OceanDeriv0"), Shader.PropertyToID("_OceanDeriv1"), Shader.PropertyToID("_OceanDeriv2") };
        static readonly int LengthScalesId = Shader.PropertyToID("_OceanLengthScales");

        /// <summary>The ocean currently publishing its textures (null if none).</summary>
        public static OceanFFT Active { get; private set; }

        public Vector4 LengthScales => new Vector4(lengthScale0, lengthScale1, lengthScale2, 0f);

        /// <summary>Angular frequency of the dominant waves (JONSWAP peak, rad/s); their period is 2π / this.</summary>
        public float PeakOmega => 22f * Mathf.Pow(9.81f * 9.81f / (Mathf.Max(windSpeed, 0.5f) * fetchKm * 1000f), 1f / 3f);

        /// <summary>
        /// Turns the wind (degrees, same convention as <see cref="windDirection"/>). The spectrum is rebuilt only when
        /// it moved enough to matter; the wave phases stay, so the sea re-forms smoothly instead of jumping.
        /// </summary>
        public void SetWindDirection(float degrees)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(degrees, windDirection)) < 0.25f) return;
            windDirection = Mathf.Repeat(degrees, 360f);
            spectrumDirty = true;
        }

        /// <summary>Direction the waves run in, on the XZ plane (same angle the spectrum uses).</summary>
        public Vector2 WaveDirection => new Vector2(Mathf.Cos(windDirection * Mathf.Deg2Rad), Mathf.Sin(windDirection * Mathf.Deg2Rad));
        public RenderTexture GetDisplacement(int cascade) => cascades != null ? cascades[cascade].displacement : null;
        public RenderTexture GetDerivatives(int cascade) => cascades != null ? cascades[cascade].derivatives : null;

        class Cascade
        {
            public float length;
            public RenderTexture h0k, waveData, h0, specA, specB, displacement, derivatives;
            public Texture2D noise;
        }

        Cascade[] cascades;
        bool spectrumDirty = true;
        bool resetFoam = true;
        int kInit, kPack, kTime, kFftH, kFftV, kAssemble;

        public float SimulationTime => Application.isPlaying ? Time.time : (float)EditorTime();

        void OnEnable()
        {
            if (fftCompute == null || !SystemInfo.supportsComputeShaders) return;
            kInit = fftCompute.FindKernel("InitSpectrum");
            kPack = fftCompute.FindKernel("PackConjugate");
            kTime = fftCompute.FindKernel("TimeSpectrum");
            kFftH = fftCompute.FindKernel("IFFTHorizontal");
            kFftV = fftCompute.FindKernel("IFFTVertical");
            kAssemble = fftCompute.FindKernel("Assemble");

            float[] lengths = { lengthScale0, lengthScale1, lengthScale2 };
            cascades = new Cascade[3];
            for (int i = 0; i < 3; i++)
            {
                cascades[i] = new Cascade
                {
                    length = lengths[i],
                    h0k = NewRT(RenderTextureFormat.RGFloat, false),
                    waveData = NewRT(RenderTextureFormat.ARGBFloat, false),
                    h0 = NewRT(RenderTextureFormat.ARGBFloat, false),
                    specA = NewRT(RenderTextureFormat.ARGBFloat, false),
                    specB = NewRT(RenderTextureFormat.ARGBFloat, false),
                    displacement = NewRT(RenderTextureFormat.ARGBHalf, true),
                    derivatives = NewRT(RenderTextureFormat.ARGBHalf, true),
                    noise = GaussianNoise(1000 + i * 77)
                };
            }
            spectrumDirty = true;
            resetFoam = true;
            Active = this;
        }

        void OnDisable()
        {
            if (cascades != null)
            {
                foreach (var c in cascades)
                {
                    foreach (var rt in new[] { c.h0k, c.waveData, c.h0, c.specA, c.specB, c.displacement, c.derivatives })
                        if (rt != null) rt.Release();
                    if (c.noise != null) DestroyImmediate(c.noise);
                }
            }
            cascades = null;
            if (Active == this) Active = null;
        }

        void OnValidate()
        {
            spectrumDirty = true;
            if (cascades != null)
            {
                cascades[0].length = lengthScale0;
                cascades[1].length = lengthScale1;
                cascades[2].length = lengthScale2;
            }
        }

        void Update()
        {
            Simulate(Application.isPlaying ? Time.deltaTime : 1f / 60f);
        }

        void Simulate(float dt)
        {
            if (cascades == null || fftCompute == null) return;
            if (spectrumDirty) InitSpectra();

            fftCompute.SetFloat("_Time", SimulationTime);
            fftCompute.SetFloat("_DeltaTime", dt);
            fftCompute.SetFloat("_Lambda", choppiness);
            fftCompute.SetFloat("_FoamDecay", foamDecay);
            fftCompute.SetFloat("_Reset", resetFoam ? 1f : 0f);
            resetFoam = false;

            int groups = Size / 8;
            for (int i = 0; i < cascades.Length; i++)
            {
                Cascade c = cascades[i];
                fftCompute.SetTexture(kTime, "_WaveData", c.waveData);
                fftCompute.SetTexture(kTime, "_H0", c.h0);
                fftCompute.SetTexture(kTime, "_SpecA", c.specA);
                fftCompute.SetTexture(kTime, "_SpecB", c.specB);
                fftCompute.Dispatch(kTime, groups, groups, 1);

                fftCompute.SetTexture(kFftH, "_SpecA", c.specA);
                fftCompute.SetTexture(kFftH, "_SpecB", c.specB);
                fftCompute.Dispatch(kFftH, Size, 1, 1);
                fftCompute.SetTexture(kFftV, "_SpecA", c.specA);
                fftCompute.SetTexture(kFftV, "_SpecB", c.specB);
                fftCompute.Dispatch(kFftV, Size, 1, 1);

                fftCompute.SetTexture(kAssemble, "_SpecA", c.specA);
                fftCompute.SetTexture(kAssemble, "_SpecB", c.specB);
                fftCompute.SetTexture(kAssemble, "_Displacement", c.displacement);
                fftCompute.SetTexture(kAssemble, "_Derivatives", c.derivatives);
                fftCompute.Dispatch(kAssemble, groups, groups, 1);

                c.displacement.GenerateMips();
                c.derivatives.GenerateMips();

                Shader.SetGlobalTexture(DispIds[i], c.displacement);
                Shader.SetGlobalTexture(DerivIds[i], c.derivatives);
            }
            Shader.SetGlobalVector(LengthScalesId, LengthScales);
            Active = this;
        }

        // The spectrum only changes with the settings.
        void InitSpectra()
        {
            spectrumDirty = false;
            const float g = 9.81f;
            float fetch = fetchKm * 1000f;
            float u = Mathf.Max(windSpeed, 0.5f);
            float alpha = 0.076f * Mathf.Pow(u * u / (fetch * g), 0.22f);
            float peakOmega = PeakOmega;

            fftCompute.SetInt("_Size", Size);
            fftCompute.SetFloat("_WindSpeed", u);
            fftCompute.SetFloat("_WindAngle", windDirection * Mathf.Deg2Rad);
            fftCompute.SetFloat("_Alpha", alpha);
            fftCompute.SetFloat("_PeakOmega", peakOmega);
            fftCompute.SetFloat("_Gamma", peakEnhancement);
            fftCompute.SetFloat("_SpreadBlend", spreadBlend);
            fftCompute.SetFloat("_Swell", swell);
            fftCompute.SetFloat("_ShortWavesFade", shortWavesFade);
            fftCompute.SetFloat("_Scale", waveScale);

            // each cascade owns a band of wave numbers, so no wave is counted twice
            float boundary1 = 2f * Mathf.PI / lengthScale1 * 6f;
            float boundary2 = 2f * Mathf.PI / lengthScale2 * 6f;
            float[] low = { 0.0001f, boundary1, boundary2 };
            float[] high = { boundary1, boundary2, 9999f };

            int groups = Size / 8;
            for (int i = 0; i < cascades.Length; i++)
            {
                Cascade c = cascades[i];
                fftCompute.SetFloat("_LengthScale", c.length);
                fftCompute.SetFloat("_CutoffLow", low[i]);
                fftCompute.SetFloat("_CutoffHigh", high[i]);
                fftCompute.SetTexture(kInit, "_Noise", c.noise);
                fftCompute.SetTexture(kInit, "_H0K", c.h0k);
                fftCompute.SetTexture(kInit, "_WaveData", c.waveData);
                fftCompute.Dispatch(kInit, groups, groups, 1);

                fftCompute.SetTexture(kPack, "_H0K", c.h0k);
                fftCompute.SetTexture(kPack, "_H0", c.h0);
                fftCompute.Dispatch(kPack, groups, groups, 1);
            }
        }

        static RenderTexture NewRT(RenderTextureFormat format, bool mips)
        {
            var rt = new RenderTexture(Size, Size, 0, format, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                useMipMap = mips,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = mips ? FilterMode.Trilinear : FilterMode.Point,
                anisoLevel = mips ? 4 : 0,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
            return rt;
        }

        // Two independent pairs of normally distributed random numbers per texel (Box-Muller).
        static Texture2D GaussianNoise(int seed)
        {
            var rnd = new System.Random(seed);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
            var data = new Color[Size * Size];
            for (int i = 0; i < data.Length; i++)
                data[i] = new Color(Gauss(rnd), Gauss(rnd), Gauss(rnd), Gauss(rnd));
            tex.SetPixels(data);
            tex.Apply(false, false);
            return tex;
        }

        static float Gauss(System.Random rnd)
        {
            double u1 = 1.0 - rnd.NextDouble();
            double u2 = rnd.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        static double EditorTime()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartup;
#endif
        }
    }
}

using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// Small procedural textures used by the water shaders. Generated once on the CPU and shared.
    /// </summary>
    public static class WaterTextures
    {
        const int Size = 256;
        const int CausticCells = 7;
        static Texture2D rayField;

        /// <summary>
        /// Tileable light patterns:
        /// R = fine filaments and G = broad swells (underwater light shafts),
        /// B = sharp caustic web (light focused by the waves onto the sea bed).
        /// Replaces dozens of sin/cos per pixel with a few texture fetches.
        /// </summary>
        public static Texture2D RayField
        {
            get
            {
                if (rayField == null) rayField = Build();
                return rayField;
            }
        }

        static Texture2D Build()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "WaterLightPatterns",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.HideAndDontSave
            };

            var points = CausticPoints();
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size;
                float v = (y + 0.5f) / Size;
                float fine = Filaments(u, v, 4, 2, -3, 5, 2, -6, 0.0f);
                float broad = Filaments(u, v, 2, 1, -1, 3, 1, -2, 1.7f);
                float caustic = Caustic(u, v, points);
                pixels[y * Size + x] = new Color32((byte)(fine * 255f), (byte)(broad * 255f), (byte)(caustic * 255f), 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        // Web of soft filaments from domain-warped ridged sines. Integer frequencies -> tiles seamlessly.
        static float Filaments(float u, float v, int m0, int n0, int m1, int n1, int m2, int n2, float seed)
        {
            const float tau = Mathf.PI * 2f;
            float wu = u + 0.050f * Mathf.Sin(tau * (2f * v) + seed) + 0.030f * Mathf.Sin(tau * (3f * v + u) + seed * 2f);
            float wv = v + 0.050f * Mathf.Sin(tau * (2f * u) + seed * 1.3f) + 0.030f * Mathf.Sin(tau * (u - 3f * v) + seed * 0.7f);

            float r0 = Ridge(m0 * wu + n0 * wv + seed * 0.3f);
            float r1 = Ridge(m1 * wu + n1 * wv + seed * 0.7f + 0.21f);
            float r2 = Ridge(m2 * wu + n2 * wv + seed * 1.1f + 0.53f);

            float net = r0 + r1 + r2;
            float prod = (0.35f + r0) * (0.35f + r1) * (0.35f + r2);
            return Mathf.Clamp01(net * 0.55f + prod * 1.1f - 0.15f);
        }

        static float Ridge(float x)
        {
            float r = 1f - Mathf.Abs(Mathf.Sin(Mathf.PI * x));
            return r * r * r;
        }

        static Vector2[] CausticPoints()
        {
            var rnd = new System.Random(1234);
            var pts = new Vector2[CausticCells * CausticCells];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Vector2(0.15f + 0.7f * (float)rnd.NextDouble(), 0.15f + 0.7f * (float)rnd.NextDouble());
            return pts;
        }

        // Caustic network: the borders of a warped, tiling Voronoi diagram are bright thin lines,
        // brighter where several borders meet - the shape light takes when waves focus it.
        static float Caustic(float u, float v, Vector2[] points)
        {
            const float tau = Mathf.PI * 2f;
            int n = CausticCells;
            // integer-period warp keeps it tileable; bends the straight Voronoi edges into organic curves
            float wu = u + 0.035f * Mathf.Sin(tau * (2f * v + u)) + 0.02f * Mathf.Sin(tau * (5f * v));
            float wv = v + 0.035f * Mathf.Sin(tau * (2f * u - v)) + 0.02f * Mathf.Sin(tau * (4f * u));
            float px = wu * n, py = wv * n;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);

            float f1 = 9f, f2 = 9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = ((gx % n) + n) % n, wy = ((gy % n) + n) % n;
                Vector2 p = points[wy * n + wx];
                float dx = gx + p.x - px, dy = gy + p.y - py;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }

            float edge = f2 - f1;                          // 0 on a cell border
            float line = Mathf.Exp(-edge * 9f);            // thin bright line
            float glow = Mathf.Exp(-edge * 3f) * 0.25f;    // soft light around it
            return Mathf.Clamp01(line + glow);
        }
    }
}

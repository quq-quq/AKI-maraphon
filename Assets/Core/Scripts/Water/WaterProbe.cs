using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// Asks the water "how high is the surface here?". Evaluated on the GPU with exactly the same wave maths
    /// the surface is drawn with, and read back asynchronously (results are a couple of frames old, and
    /// time-predicted to compensate).
    /// <code>
    /// var probe = new WaterProbe();
    /// WaterProbe.Register(probe);
    /// probe.position = transform.position;          // every frame
    /// if (probe.HasData) float h = probe.Height;    // world Y of the wavy surface above/below that point
    /// </code>
    /// </summary>
    public sealed class WaterProbe
    {
        /// <summary>Where to measure (only x/z matter).</summary>
        public Vector3 position;

        /// <summary>World Y of the wave surface at <see cref="position"/>.</summary>
        public float Height { get; internal set; }

        /// <summary>Surface normal at that point.</summary>
        public Vector3 Normal { get; internal set; } = Vector3.up;

        /// <summary>True once at least one GPU result has arrived.</summary>
        public bool HasData { get; internal set; }

        /// <summary>The water this probe is inside, or null.</summary>
        public WaterSurface Water { get; internal set; }

        /// <summary>Starts updating the probe (safe to call more than once).</summary>
        public static void Register(WaterProbe probe) => WaterSurface.RegisterProbe(probe);

        public static void Unregister(WaterProbe probe) => WaterSurface.UnregisterProbe(probe);

        /// <summary>Surface height if known, otherwise the mean water level (or <paramref name="fallback"/> with no water).</summary>
        public float HeightOr(float fallback)
        {
            if (HasData) return Height;
            return Water != null ? Water.WaterLevel : fallback;
        }
    }
}

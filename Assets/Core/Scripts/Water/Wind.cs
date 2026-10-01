using UnityEngine;

namespace AKI.Water
{
    /// <summary>
    /// The wind over the sea. It keeps turning clockwise (seen from above), so the waves of the <see cref="OceanFFT"/>
    /// slowly swing round and the <see cref="WaterCurrent"/> it drives turns with it: things carried by the water go
    /// round in wide loops instead of drifting off to the edge of the world. One per scene, on the water.
    /// </summary>
    public class Wind : MonoBehaviour
    {
        [Tooltip("Where the wind blows to now (compass degrees: 0 = +Z, 90 = +X).")]
        [Range(0f, 360f)] public float heading = 60f;
        [Tooltip("Clockwise turn in degrees per minute (36 = a full circle in 10 minutes, 0 = steady wind).")]
        public float turnRate = 36f;

        static Wind active;

        /// <summary>The scene's wind, or null.</summary>
        public static Wind Active => active;

        /// <summary>Where the wind blows to, on the XZ plane.</summary>
        public Vector3 Direction => Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

        void OnEnable()
        {
            active = this;
        }

        void OnDisable()
        {
            if (active == this) active = null;
        }

        void Start()
        {
            Apply();
        }

        void Update()
        {
            heading = Mathf.Repeat(heading + turnRate / 60f * Time.deltaTime, 360f);
            Apply();
        }

        // the ocean measures its wind angle from +X towards +Z: compass heading h is 90 - h there
        void Apply()
        {
            OceanFFT ocean = GetComponent<OceanFFT>();
            if (ocean == null) ocean = OceanFFT.Active;
            if (ocean != null) ocean.SetWindDirection(90f - heading);
        }
    }
}

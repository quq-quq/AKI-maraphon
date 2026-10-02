using AKI.Water;
using UnityEngine;

namespace AKI.Menu
{
    /// <summary>
    /// Put next to the <see cref="OceanFFT"/>. The sea stays as calm as the scene has it while the menu and the cutscene
    /// play, then, once the player takes over, rises over <see cref="riseSeconds"/> to the game's sea: big rolling waves
    /// with whitecaps breaking on their crests (the foam follows the waves by itself).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OceanFFT))]
    public class MenuSeaState : MonoBehaviour
    {
        [Tooltip("Starts the rise when its player takes over. Found in the scene when empty.")]
        public MainMenuController menu;
        [Tooltip("Wave height of the game (OceanFFT wave scale); the menu keeps the one set on the ocean.")]
        [Range(0f, 3f)] public float gameWaveScale = 1f;
        [Tooltip("Seconds for the sea to rise from the menu's calm to the game's.")]
        [Min(0f)] public float riseSeconds = 12f;

        OceanFFT ocean;
        float calmScale;
        float riseStart = -1f;

        void Start()
        {
            ocean = GetComponent<OceanFFT>();
            calmScale = ocean.waveScale;
            if (menu == null) menu = FindFirstObjectByType<MainMenuController>();
            if (menu != null) menu.onGameStarted.AddListener(Rise);
            else Rise();   // no menu: the game starts right away
        }

        void OnDestroy()
        {
            if (menu != null) menu.onGameStarted.RemoveListener(Rise);
        }

        void Rise() => riseStart = Time.time;

        void Update()
        {
            if (riseStart < 0f) return;
            float k = riseSeconds > 0f ? Mathf.Clamp01((Time.time - riseStart) / riseSeconds) : 1f;
            ocean.SetWaveScale(Mathf.Lerp(calmScale, gameWaveScale, Mathf.SmoothStep(0f, 1f, k)));
            if (k >= 1f) enabled = false;
        }
    }
}

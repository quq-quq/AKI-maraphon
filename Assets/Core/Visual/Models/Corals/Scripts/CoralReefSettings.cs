using UnityEngine;

namespace AKI.Reef
{
    [CreateAssetMenu(menuName = "AKI/Coral Reef Settings")]
    public sealed class CoralReefSettings : ScriptableObject
    {
        public int seed = 10423;
        [Min(32)] public float terrainSize = 240;
        [Min(12)] public float terrainHeight = 36;
        public float terrainBaseY = -32;
        [Range(0.25f, 2)] public float hillHeightMultiplier = 1;
        [Header("Basin shape (meters)")]
        [Min(20)] public float basinRadius = 100;
        [Range(2, 28)] public float rimRise = 22;
        [Range(6, 24)] public int outerMountainCount = 16;
        [Range(.5f, 6)] public float innerHillHeight = 3.5f;
        [Range(12, 100)] public int innerHillCount = 72;
        [Range(0, 500)] public int hillClusters = 180;
        [Range(0, 500)] public int sparseCorals = 110;
        [Range(3, 7)] public int minClusterCount = 3;
        [Range(3, 7)] public int maxClusterCount = 7;
        [Range(0.5f, 5)] public float clusterRadius = 2.5f;
        [Range(0, 60)] public float coralMaxSlope = 32;
        public Vector2 coralScaleRange = new Vector2(0.65f, 1.45f);
        public Material terrainMaterial;
        public GameObject[] coralPrefabs;
        [Tooltip("Six shared pastel materials per species: species index * 6 + palette index.")]
        public Material[] pastelMaterials;
    }
}

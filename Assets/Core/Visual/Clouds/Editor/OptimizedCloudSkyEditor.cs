using UnityEngine;
using UnityEditor;

namespace AKI.Clouds.Editor
{
    [CustomEditor(typeof(OptimizedCloudSky))]
    public sealed class OptimizedCloudSkyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var sky = (OptimizedCloudSky)target;
            EditorGUILayout.HelpBox("True 3D volume. Noise is baked, animation is GPU-only. Clouds remain visible across the wave zone, fade deeper underwater, then skip entirely. Reflection / preview cameras skip. No cloud shadow map or temporal history.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ultra Light")) Preset(sky, 24, 4, 320);
            if (GUILayout.Button("Optimized")) Preset(sky, 32, 4, 384);
            if (GUILayout.Button("Sharper")) Preset(sky, 40, 2, 640);
            EditorGUILayout.EndHorizontal();
            DrawDefaultInspector();
            EditorGUILayout.LabelField("Last cloud buffer", OptimizedCloudsFeature.LastBufferWidth + " x " + OptimizedCloudsFeature.LastBufferHeight);
            EditorGUILayout.LabelField("Maximum march steps", sky.raySteps.ToString());
            EditorGUILayout.HelpBox("Existing skybox and sun are preserved. Wind is metres/second; Evolution changes the volume shape continuously. Only sky-depth pixels receive clouds, so birds/terrain are not painted over.", MessageType.None);
        }
        static void Preset(OptimizedCloudSky sky, int steps, int downsample, int cap)
        {
            Undo.RecordObject(sky, "Cloud GPU budget preset");
            sky.raySteps = steps; sky.downsample = downsample; sky.maximumBufferWidth = cap;
            sky.ApplySettings(); EditorUtility.SetDirty(sky);
        }
    }
}

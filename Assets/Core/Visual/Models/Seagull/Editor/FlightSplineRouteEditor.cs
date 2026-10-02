using UnityEngine;
using UnityEditor;

namespace OptimizedSeagulls.Editor
{
    [CustomEditor(typeof(FlightSplineRoute))]
    public sealed class FlightSplineRouteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var route = (FlightSplineRoute)target;
            if (route.spline == null) return;
            EditorGUILayout.HelpBox("Move route points in Scene view, then Bake. No spline generation takes place during gameplay.", MessageType.Info);
            EditorGUILayout.LabelField("Baked route", route.spline.Length.ToString("F0") + " m / " + route.spline.SampleCount + " samples");
            if (GUILayout.Button("Bake Flight Spline"))
            {
                Undo.RecordObject(route.spline, "Bake flight spline");
                route.spline.Bake(); AssetDatabase.SaveAssets(); SceneView.RepaintAll();
            }
        }
        void OnSceneGUI()
        {
            var route = (FlightSplineRoute)target;
            if (route.spline == null || route.spline.controlPoints == null) return;
            for (int i = 0; i < route.spline.controlPoints.Length; i++)
            {
                Vector3 world = route.transform.TransformPoint(route.spline.controlPoints[i]);
                Handles.Label(world, "Route " + (i + 1));
                EditorGUI.BeginChangeCheck(); Vector3 moved = Handles.PositionHandle(world, route.transform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(route.spline, "Move flight control point");
                    route.spline.controlPoints[i] = route.transform.InverseTransformPoint(moved);
                    EditorUtility.SetDirty(route.spline);
                }
            }
        }
    }
}

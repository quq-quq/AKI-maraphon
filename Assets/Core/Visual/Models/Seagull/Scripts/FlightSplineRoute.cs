using UnityEngine;

namespace OptimizedSeagulls
{
    public sealed class FlightSplineRoute : MonoBehaviour
    {
        public BakedFlightSpline spline;
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (spline == null || !spline.IsBaked) return;
            Gizmos.color = new Color(.9f, .7f, .15f, .9f);
            spline.Evaluate(0, out var previous, out _);
            for (int i = 1; i <= 128; i++)
            {
                spline.Evaluate(spline.Length * i / 128, out var next, out _);
                Gizmos.DrawLine(transform.TransformPoint(previous), transform.TransformPoint(next));
                previous = next;
            }
        }
#endif
    }
}

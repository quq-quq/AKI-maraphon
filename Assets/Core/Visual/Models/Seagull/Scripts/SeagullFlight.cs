using UnityEngine;

namespace OptimizedSeagulls
{
    public sealed class SeagullFlight : MonoBehaviour
    {
        public FlightSplineRoute route;
        public Animator wingAnimator;
        [Min(.1f)] public float speed = 10;
        [Range(0, 1)] public float startingProgress;
        [Min(.1f)] public float flapSeconds = 2.5f;
        [Min(.1f)] public float glideSeconds = 9;
        public float animationPhase;
        float distance, cycle;
        bool lastFlapping, initialized;
        static readonly int Flapping = Animator.StringToHash("Flapping");

        void OnEnable() => ResetFlight();
        public void ResetFlight()
        {
            distance = route != null && route.spline != null ? route.spline.Length * startingProgress : 0;
            cycle = animationPhase; initialized = false;
            Tick(0);
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float deltaTime)
        {
            if (route == null || route.spline == null || !route.spline.IsBaked) return;
            distance = Mathf.Repeat(distance + speed * deltaTime, route.spline.Length);
            route.spline.Evaluate(distance, out var p, out var q);
            transform.SetPositionAndRotation(route.transform.TransformPoint(p), route.transform.rotation * q);
            cycle = Mathf.Repeat(cycle + deltaTime, flapSeconds + glideSeconds);
            bool flapping = cycle < flapSeconds;
            if (wingAnimator != null && wingAnimator.runtimeAnimatorController != null && (!initialized || flapping != lastFlapping))
                wingAnimator.SetBool(Flapping, flapping);
            lastFlapping = flapping; initialized = true;
        }
    }
}

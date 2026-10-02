using UnityEngine;

namespace OptimizedSeagulls
{
    [CreateAssetMenu(menuName = "Seagulls/Baked Closed Flight Spline")]
    public sealed class BakedFlightSpline : ScriptableObject
    {
        public Vector3[] controlPoints;
        [SerializeField, HideInInspector] Vector3[] positions;
        [SerializeField, HideInInspector] Quaternion[] rotations;
        [SerializeField] float length;
        public float Length => length;
        public int SampleCount => positions == null ? 0 : positions.Length;
        public bool IsBaked => SampleCount > 1 && length > 0;

        public void Evaluate(float distance, out Vector3 position, out Quaternion rotation)
        {
            if (!IsBaked) { position = Vector3.zero; rotation = Quaternion.identity; return; }
            float sample = Mathf.Repeat(distance, length) * positions.Length / length;
            int a = Mathf.Min((int)sample, positions.Length - 1), b = (a + 1) % positions.Length;
            float t = sample - a;
            position = Vector3.LerpUnclamped(positions[a], positions[b], t);
            rotation = Quaternion.SlerpUnclamped(rotations[a], rotations[b], t);
        }

#if UNITY_EDITOR
        // Spline evaluation, arc-length calculation and banking occur only in the editor.
        public void Bake()
        {
            if (controlPoints == null || controlPoints.Length < 4)
                throw new System.InvalidOperationException("A closed route needs at least four control points.");
            const int denseCount = 4096, sampleCount = 512;
            var dense = new Vector3[denseCount + 1];
            var distances = new float[denseCount + 1];
            int n = controlPoints.Length;
            for (int i = 0; i <= denseCount; i++)
            {
                float u = (float)i / denseCount * n;
                int k = (int)u % n; float t = u - Mathf.Floor(u);
                Vector3 a = controlPoints[(k + n - 1) % n], b = controlPoints[k];
                Vector3 c = controlPoints[(k + 1) % n], d = controlPoints[(k + 2) % n];
                dense[i] = .5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            }
            length = distances[denseCount];
            if (length < 1) throw new System.InvalidOperationException("Flight route is too short.");
            positions = new Vector3[sampleCount]; rotations = new Quaternion[sampleCount];
            int cursor = 1;
            for (int i = 0; i < sampleCount; i++)
            {
                float distance = length * i / sampleCount;
                while (cursor < denseCount && distances[cursor] < distance) cursor++;
                float t = Mathf.InverseLerp(distances[cursor - 1], distances[cursor], distance);
                positions[i] = Vector3.Lerp(dense[cursor - 1], dense[cursor], t);
            }
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 incoming = (positions[i] - positions[(i + sampleCount - 1) % sampleCount]).normalized;
                Vector3 outgoing = (positions[(i + 1) % sampleCount] - positions[i]).normalized;
                Vector3 tangent = (incoming + outgoing).normalized;
                float turn = Vector3.SignedAngle(incoming, outgoing, Vector3.up);
                float bank = Mathf.Clamp(-turn * 9, -22, 22);
                rotations[i] = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.AngleAxis(bank, Vector3.forward);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}

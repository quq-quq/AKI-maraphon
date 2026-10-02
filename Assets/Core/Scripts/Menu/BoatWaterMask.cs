using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Menu
{
    /// <summary>
    /// Keeps the sea out of an open boat. The water surface is drawn wherever it is, also inside the hull (the inner
    /// floor sits below the waterline outside), where it shows and foams as the waves pass under the boat.
    /// At start this builds a lid along the rim of the hull: its outline seen from above, each point at the height of
    /// the rim right there (following the sheer of the bow and stern), a little lower and inset, and puts it under the
    /// hull with the invisible <c>AKI/WaterMask</c> shader, which hides the water behind it. Everything opaque in the
    /// boat (its floor, whoever sits in it) stays visible. The hull mesh must have Read/Write enabled on import.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoatWaterMask : MonoBehaviour
    {
        [Tooltip("The hull mesh. Empty = the child called Boat, or else the first mesh under this object.")]
        public MeshFilter hull;
        [Tooltip("AKI/WaterMask (referenced so it is included in builds).")]
        public Shader maskShader;
        [Tooltip("How far under the top of the rim the lid sits (m).")]
        [Min(0f)] public float drop = 0.03f;
        [Tooltip("How far inside the outer edge of the rim the lid ends (m).")]
        [Min(0f)] public float inset = 0.03f;

        GameObject lidObject;
        Mesh lidMesh;
        Material material;

        void Start()
        {
            if (hull == null)
            {
                Transform boat = transform.Find("Boat");
                hull = boat != null ? boat.GetComponent<MeshFilter>() : GetComponentInChildren<MeshFilter>();
            }
            if (hull == null || hull.sharedMesh == null)
            {
                Debug.LogWarning("BoatWaterMask: no hull mesh.", this);
                return;
            }
            if (maskShader == null) maskShader = Shader.Find("AKI/WaterMask");
            if (maskShader == null)
            {
                Debug.LogWarning("BoatWaterMask: shader AKI/WaterMask not found.", this);
                return;
            }

            lidMesh = BuildLid(hull.sharedMesh);
            material = new Material(maskShader) { name = "Water Mask" };
            lidObject = new GameObject("Water Mask");
            lidObject.transform.SetParent(hull.transform, false);   // rides along with the hull
            lidObject.AddComponent<MeshFilter>().sharedMesh = lidMesh;
            var renderer = lidObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            if (lidObject != null) Destroy(lidObject);
            if (lidMesh != null) Destroy(lidMesh);
            if (material != null) Destroy(material);
        }

        // A fan over the hull's outline seen from above, each outline point a little under the rim right there, the
        // middle at the lowest of them: never above the rim.
        Mesh BuildLid(Mesh source)
        {
            var outline = new List<Vector3>();
            Bounds b = source.bounds;
            if (source.isReadable)
            {
                Vector3[] vertices = source.vertices;
                var flat = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) flat[i] = new Vector2(vertices[i].x, vertices[i].z);
                float near = Mathf.Max(b.size.x, b.size.z) * 0.03f;
                foreach (Vector2 q in ConvexHull(flat))
                {
                    float rim = float.NegativeInfinity;
                    for (int i = 0; i < vertices.Length; i++)
                        if ((flat[i] - q).sqrMagnitude <= near * near) rim = Mathf.Max(rim, vertices[i].y);
                    outline.Add(new Vector3(q.x, rim, q.y));
                }
            }
            else
            {
                // without Read/Write: a canoe shape from the bounds, flat and well under the top (the ends rise)
                Debug.LogWarning($"BoatWaterMask: enable Read/Write on the import of '{source.name}' for a lid that fits the hull. Using a rough canoe shape.", this);
                bool longX = b.size.x > b.size.z;
                float halfLength = (longX ? b.extents.x : b.extents.z) * 0.9f;
                float halfWidth = (longX ? b.extents.z : b.extents.x) * 0.85f;
                float top = b.max.y - b.size.y * 0.2f;
                const int steps = 32;
                for (int i = 0; i < steps; i++)
                {
                    float a = i * 2f * Mathf.PI / steps;
                    float along = Mathf.Cos(a) * halfLength;
                    float across = Mathf.Sign(Mathf.Sin(a)) * Mathf.Pow(Mathf.Abs(Mathf.Sin(a)), 0.6f) * halfWidth;
                    outline.Add(longX ? new Vector3(b.center.x + along, top, b.center.z + across)
                                      : new Vector3(b.center.x + across, top, b.center.z + along));
                }
            }

            // a little lower and inside, and the middle
            Vector3 middle = Vector3.zero;
            foreach (Vector3 p in outline) middle += new Vector3(p.x, 0f, p.z);
            middle /= outline.Count;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector3 p = outline[i];
                Vector3 toMiddle = new Vector3(middle.x - p.x, 0f, middle.z - p.z);
                Vector3 shift = toMiddle.magnitude > inset ? toMiddle.normalized * inset : toMiddle;
                outline[i] = new Vector3(p.x, p.y - drop, p.z) + shift;
                lowest = Mathf.Min(lowest, outline[i].y);
            }
            middle.y = lowest;

            var lidVertices = new List<Vector3> { middle };
            lidVertices.AddRange(outline);
            var triangles = new List<int>();
            for (int i = 0; i < outline.Count; i++)
            {
                triangles.Add(0);
                triangles.Add(1 + i);
                triangles.Add(1 + (i + 1) % outline.Count);
            }

            var lid = new Mesh { name = "Boat Water Mask" };
            lid.SetVertices(lidVertices);
            lid.SetTriangles(triangles, 0);
            lid.RecalculateBounds();
            return lid;
        }

        // Andrew's monotone chain: the outline around the points.
        static List<Vector2> ConvexHull(Vector2[] points)
        {
            var sorted = new List<Vector2>(points);
            sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (Vector2 p in sorted)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                sorted.Reverse();
            }
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }
    }
}

using System.Collections.Generic;
using AKI.Water;
using UnityEngine;

namespace AKI.Weapons
{
    /// <summary>
    /// The line between the speargun and its shot arrow. It is laid out along the arrow's path as it flies (paid out
    /// from the gun, never taken back in), then sinks and sways with the water, settles on the bottom, and at full
    /// length goes taut and holds the arrow back. Verlet rope with drag against the (slowly moving) water and a little
    /// stiffness, drawn as a lit twisted tube that never gets thinner than a couple of pixels.
    /// The next shot cuts the line at the gun; once the arrow is gone the line sinks and fades out.
    /// Created by <see cref="HarpoonProjectile"/> on launch, as a separate object so it can outlive the arrow.
    /// </summary>
    [DefaultExecutionOrder(200)]   // after the player has placed the gun and the arrow has moved
    public class HarpoonRope : MonoBehaviour
    {
        const float SegmentLength = 0.25f;
        const float StepTime = 1f / 90f;
        const int MaxStepsPerFrame = 4;
        const int Iterations = 8;
        const int Sides = 7;
        const int Subdivisions = 3;          // smooth curve samples per segment
        const float TwistPitch = 5f;         // one strand twist per this many diameters

        // water: drag pulls the line towards the water's own (slow, wandering) motion; a wet line sinks slowly
        const float WaterDrag = 3.5f;
        const float WaterGravity = 0.8f;
        const float CurrentSpeed = 0.05f;
        const float AirDrag = 0.05f;
        const float Stiffness = 0.15f;       // resistance to sharp bends
        const float MaxPointSpeed = 12f;     // m/s: solver corrections must not turn into wild flailing
        const float FadeDelay = 1.5f;        // after the arrow is gone
        const float FadeTime = 1f;

        static Material fallbackMaterial;

        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector3> prev = new List<Vector3>();
        readonly List<Vector3> frameStart = new List<Vector3>();
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        readonly RaycastHit[] hits = new RaycastHit[4];

        Transform anchor;
        Transform owner;
        HarpoonProjectile arrow;
        Vector3 tailLocal;
        float radius;
        float maxLength;
        bool gunAttached;
        bool hadArrow;
        float fade = 1f;
        float sinceArrowGone;
        float accumulator;
        Vector3 lastHead, lastTail;
        Mesh mesh;
        int builtRings = -1;

        /// <summary>True once the whole line is out: it no longer pays out and holds the arrow.</summary>
        public bool FullyPaidOut => (pos.Count - 1) * SegmentLength >= maxLength;

        /// <summary>Line from <paramref name="gun"/>'s anchor to <paramref name="shot"/> (tied at <paramref name="tail"/>, arrow local).</summary>
        public static HarpoonRope Create(Speargun gun, HarpoonProjectile shot, Vector3 tail)
        {
            var go = new GameObject("HarpoonRope");
            var rope = go.AddComponent<HarpoonRope>();
            rope.Init(gun, shot, tail);
            return rope;
        }

        void Init(Speargun gun, HarpoonProjectile shot, Vector3 tail)
        {
            anchor = gun.RopeAnchor;
            owner = gun.Owner;
            arrow = shot;
            tailLocal = tail;
            radius = gun.ropeWidth * 0.5f;
            maxLength = gun.ropeLength;
            gunAttached = anchor != null;
            hadArrow = true;

            mesh = new Mesh { name = "HarpoonRope" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = gun.ropeMaterial != null ? gun.ropeMaterial : FallbackMaterial();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            lastHead = gunAttached ? anchor.position : Tail;
            lastTail = Tail;
            Add(pos.Count, lastHead, Vector3.zero);
            Add(pos.Count, lastTail, Vector3.zero);
        }

        /// <summary>The gun fired again: cut the line at the gun, the loose end drifts and sinks.</summary>
        public void Cut()
        {
            gunAttached = false;
        }

        /// <summary>
        /// Called by the arrow after it moved: once the whole line is out, its end (<paramref name="tailWorld"/>)
        /// can't get further from the gun than the line is long. Measured from the gun itself, not from the next
        /// rope point (that one is pulled along by the arrow, and the two would keep yanking each other).
        /// Returns the correction for the arrow and takes the outward part out of its velocity.
        /// </summary>
        public Vector3 Tether(Vector3 tailWorld, ref Vector3 velocity)
        {
            if (!gunAttached || anchor == null || !FullyPaidOut) return Vector3.zero;
            Vector3 d = tailWorld - anchor.position;
            float dist = d.magnitude;
            if (dist <= maxLength) return Vector3.zero;
            Vector3 dir = d / dist;
            float outward = Vector3.Dot(velocity, dir);
            if (outward > 0f) velocity -= dir * outward;
            return -dir * (dist - maxLength);
        }

        bool HasArrow => arrow != null;

        Vector3 Tail => arrow.transform.TransformPoint(tailLocal);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (hadArrow && !HasArrow) hadArrow = false;
            Vector3 head = gunAttached && anchor != null ? anchor.position : pos[0];
            Vector3 tail = HasArrow ? Tail : pos[pos.Count - 1];

            PayOut(head, tail);

            frameStart.Clear();
            frameStart.AddRange(pos);

            accumulator += dt;
            int steps = Mathf.Min(MaxStepsPerFrame, Mathf.FloorToInt(accumulator / StepTime));
            for (int s = 0; s < steps; s++)
            {
                float k = (s + 1f) / steps;   // ends move smoothly between frames
                Step(StepTime, Vector3.Lerp(lastHead, head, k), Vector3.Lerp(lastTail, tail, k));
            }
            accumulator = steps == MaxStepsPerFrame ? 0f : accumulator - steps * StepTime;
            lastHead = head;
            lastTail = tail;

            Collide();

            if (!HasArrow)
            {
                sinceArrowGone += dt;
                fade = 1f - Mathf.Clamp01((sinceArrowGone - FadeDelay) / FadeTime);
                if (fade <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            BuildMesh();
        }

        // ------------------------------------------------------------------ simulation

        void Add(int index, Vector3 p, Vector3 velocityPerStep)
        {
            pos.Insert(index, p);
            prev.Insert(index, p - velocityPerStep);
        }

        // The spool: while there is line left, new segments come out where the line is pulled: behind the flying arrow,
        // and at either end once the line is taut (player swimming away, fish pulling). Sag alone doesn't pull line out.
        void PayOut(Vector3 head, Vector3 tail)
        {
            int guard = 64;
            bool taut = Vector3.Distance(head, tail) > 0.97f * (pos.Count - 1) * SegmentLength;
            while (HasArrow && (arrow.IsFlying || taut) && !FullyPaidOut && guard-- > 0)
            {
                Vector3 from = pos[pos.Count - 2];
                Vector3 d = tail - from;
                if (d.magnitude <= SegmentLength) break;
                // laid out along the arrow's path, at rest in the water
                Add(pos.Count - 1, from + d.normalized * SegmentLength, Vector3.zero);
            }
            while (gunAttached && taut && !FullyPaidOut && guard-- > 0)
            {
                Vector3 to = pos[1];
                Vector3 d = head - to;
                if (d.magnitude <= SegmentLength) break;
                Add(1, to + d.normalized * SegmentLength, Vector3.zero);
            }
        }

        void Step(float h, Vector3 head, Vector3 tail)
        {
            int n = pos.Count;
            bool pinHead = gunAttached;
            bool pinTail = HasArrow;
            float waterKeep = 1f - Mathf.Exp(-WaterDrag * h);
            float airKeep = Mathf.Exp(-AirDrag * h);
            float t = Time.time;

            for (int i = 0; i < n; i++)
            {
                if ((i == 0 && pinHead) || (i == n - 1 && pinTail)) continue;
                Vector3 p = pos[i];
                Vector3 v = p - prev[i];
                Vector3 accel;
                if (WaterSurface.IsPointUnderwater(p))
                {
                    v = Vector3.Lerp(v, Current(p, t) * h, waterKeep);   // drag relative to the water
                    accel = Vector3.down * WaterGravity;
                }
                else
                {
                    v *= airKeep;
                    accel = Physics.gravity;
                }
                v = Vector3.ClampMagnitude(v, MaxPointSpeed * h);
                prev[i] = p;
                pos[i] = p + v + accel * (h * h);
            }

            for (int k = 0; k < Iterations; k++)
            {
                if (pinHead) pos[0] = head;
                if (pinTail) pos[n - 1] = tail;

                // a line only pulls, never pushes
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 d = pos[i + 1] - pos[i];
                    float dist = d.magnitude;
                    if (dist <= SegmentLength) continue;
                    float wa = (i == 0 && pinHead) ? 0f : 1f;
                    float wb = (i + 1 == n - 1 && pinTail) ? 0f : 1f;
                    float w = wa + wb;
                    if (w <= 0f) continue;
                    Vector3 fix = d * ((dist - SegmentLength) / (dist * w));
                    pos[i] += fix * wa;
                    pos[i + 1] -= fix * wb;
                }

                // a little stiffness: neighbours two apart don't fold onto each other
                for (int i = 0; i < n - 2; i++)
                {
                    Vector3 d = pos[i + 2] - pos[i];
                    float dist = d.magnitude;
                    float rest = 1.7f * SegmentLength;
                    if (dist >= rest || dist < 1e-5f) continue;
                    float wa = (i == 0 && pinHead) ? 0f : 1f;
                    float wb = (i + 2 == n - 1 && pinTail) ? 0f : 1f;
                    float w = wa + wb;
                    if (w <= 0f) continue;
                    Vector3 push = d * (Stiffness * (dist - rest) / (dist * w));
                    pos[i] += push * wa;
                    pos[i + 2] -= push * wb;
                }
            }
            if (pinHead) pos[0] = head;
            if (pinTail) pos[n - 1] = tail;
        }

        // slow wandering water movement, so a slack line keeps swaying
        static Vector3 Current(Vector3 p, float t)
        {
            float x = Mathf.PerlinNoise(p.x * 0.15f + t * 0.07f, p.z * 0.15f) - 0.5f;
            float y = Mathf.PerlinNoise(p.y * 0.15f + 13.7f, p.x * 0.15f + t * 0.05f) - 0.5f;
            float z = Mathf.PerlinNoise(p.z * 0.15f - t * 0.06f, p.y * 0.15f + 41.3f) - 0.5f;
            return new Vector3(x, y * 0.5f, z) * (2f * CurrentSpeed);
        }

        // Once per frame: the line lies on the bottom and on rocks instead of sinking through them.
        void Collide()
        {
            int n = pos.Count;
            for (int i = 0; i < n; i++)
            {
                if ((i == 0 && gunAttached) || (i == n - 1 && HasArrow)) continue;
                Vector3 from = i < frameStart.Count ? frameStart[i] : pos[i];
                Vector3 d = pos[i] - from;
                float len = d.magnitude;
                if (len < 1e-5f) continue;

                int count = Physics.RaycastNonAlloc(from, d / len, hits, len + radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                RaycastHit nearest = default;
                for (int h = 0; h < count; h++)
                {
                    if (hits[h].distance >= best || hits[h].distance <= 0f) continue;
                    if (owner != null && hits[h].transform.IsChildOf(owner)) continue;
                    best = hits[h].distance;
                    nearest = hits[h];
                }
                if (best == float.MaxValue) continue;
                pos[i] = nearest.point + nearest.normal * radius;
                prev[i] = pos[i];   // friction: it stays where it landed
            }
        }

        // ------------------------------------------------------------------ mesh

        void BuildMesh()
        {
            BuildPath();
            int rings = path.Count;
            vertices.Clear();
            normals.Clear();
            uvs.Clear();

            Camera cam = Camera.main;
            float pixelSize = cam != null ? 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight) : 0f;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

            Vector3 tangent = (path[1] - path[0]).normalized;
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float along = 0f;
            float pitch = Mathf.Max(radius * 2f * TwistPitch, 1e-3f);

            for (int r = 0; r < rings; r++)
            {
                Vector3 next = path[Mathf.Min(r + 1, rings - 1)];
                Vector3 before = path[Mathf.Max(r - 1, 0)];
                Vector3 t = (next - before).normalized;
                if (t.sqrMagnitude < 1e-6f) t = tangent;
                // carry the ring orientation along: keep the last normal, minus its part along the new tangent.
                // Stable even where the line doubles back (a rotation between opposite tangents would flip it).
                Vector3 n0 = normal - t * Vector3.Dot(normal, t);
                if (n0.sqrMagnitude < 1e-6f) n0 = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
                normal = n0.normalized;
                tangent = t;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                if (r > 0) along += Vector3.Distance(path[r], path[r - 1]);

                // never thinner than ~2.5 px, so the line stays readable far away
                float minRadius = Vector3.Distance(path[r], camPos) * pixelSize * 1.25f;
                float rr = Mathf.Max(radius, minRadius) * fade;

                for (int s = 0; s <= Sides; s++)
                {
                    float a = s / (float)Sides * Mathf.PI * 2f;
                    Vector3 n = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    vertices.Add(path[r] + n * rr);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)Sides, along / pitch));
                }
            }

            if (rings != builtRings)
            {
                mesh.Clear();
                triangles.Clear();
                for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = r * (Sides + 1) + s;
                    int b = a + Sides + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (rings != builtRings)
            {
                mesh.SetTriangles(triangles, 0);
                builtRings = rings;
            }
            mesh.RecalculateBounds();
        }

        // Catmull-Rom through the simulated points: a smooth line out of a few dozen points.
        void BuildPath()
        {
            path.Clear();
            int n = pos.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = pos[Mathf.Max(i - 1, 0)], p1 = pos[i], p2 = pos[i + 1], p3 = pos[Mathf.Min(i + 2, n - 1)];
                for (int s = 0; s < Subdivisions; s++)
                {
                    float t = s / (float)Subdivisions;
                    float t2 = t * t, t3 = t2 * t;
                    path.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            path.Add(pos[n - 1]);
            if (path.Count < 2) path.Add(pos[n - 1] + Vector3.forward * 1e-3f);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        static Material FallbackMaterial()
        {
            if (fallbackMaterial != null) return fallbackMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            fallbackMaterial = new Material(shader) { name = "HarpoonRope (fallback)" };
            fallbackMaterial.SetColor("_BaseColor", new Color(0.95f, 0.85f, 0.5f));
            fallbackMaterial.SetFloat("_Smoothness", 0.45f);
            return fallbackMaterial;
        }
    }
}

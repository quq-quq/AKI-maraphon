using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AKI.Water
{
    /// <summary>
    /// The open ocean's geometry: a geometry clipmap around the camera, after gasgiant's FFT-Ocean (OceanGeometry.cs,
    /// MIT, Copyright (c) 2020 Ivan Pensionerov). A fine square in the middle, then rings whose cells double in size
    /// level by level, thin "trim" strips that fill the gap left by snapping each level to its own grid, and a skirt
    /// out to the edge of the sea. Every level snaps to twice its cell size, so vertices never swim over the waves,
    /// and the outer edge of each level only uses every other vertex (no cracks against the coarser level outside).
    /// Higher up the finest levels are dropped: they would be smaller than a pixel.
    /// The pieces are hidden child renderers with the water's material, so they draw like the water always did.
    /// </summary>
    sealed class WaterClipmap
    {
        readonly Transform root;
        readonly List<Element> rings = new List<Element>();
        readonly List<Element> trims = new List<Element>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Element center;
        Element skirt;

        float cell;          // finest cell (m)
        int density;
        int levels;
        float skirtSize;
        float seaSize;

        static readonly Quaternion[] TrimRotations =
        {
            Quaternion.AngleAxis(180f, Vector3.up),
            Quaternion.AngleAxis(90f, Vector3.up),
            Quaternion.AngleAxis(270f, Vector3.up),
            Quaternion.identity,
        };

        const string RootName = "~OceanClipmap";

        public WaterClipmap(Transform parent)
        {
            // a stale copy from before a domain reload (hidden objects outlive it, the reference to them does not)
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform old = parent.GetChild(i);
                if (old.name != RootName) continue;
                foreach (MeshFilter f in old.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && f.sharedMesh.hideFlags == HideFlags.HideAndDontSave) Object.DestroyImmediate(f.sharedMesh);
                Object.DestroyImmediate(old.gameObject);
            }
            root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave }.transform;
            root.SetParent(parent, false);
        }

        /// <summary>Cell size (m) of the finest level drawn right now (grows when the finest levels are dropped).</summary>
        public float InnerCell { get; private set; }

        /// <summary>Distance (m) over which the cell size grows by one inner cell: cell(d) ~ inner * (1 + d / this).</summary>
        public float Growth => GridSize() * InnerCell * 0.5f;

        int GridSize() => 4 * density + 1;

        public bool Matches(float cellSize, int vertexDensity, int clipLevels, float size) =>
            Mathf.Approximately(cell, cellSize) && density == vertexDensity && levels == clipLevels && Mathf.Approximately(seaSize, size);

        /// <summary>(Re)builds the meshes. Cell = finest cell (m), density 1..40 (vertices per level ~ (8 density)^2).</summary>
        public void Build(float cellSize, int vertexDensity, int clipLevels, float size, Material material)
        {
            DestroyMeshesAndChildren();
            cell = cellSize;
            density = vertexDensity;
            levels = clipLevels;
            seaSize = size;

            int k = GridSize();
            // reach the edge of the sea with the skirt (in units of the outermost scale)
            float outer = LengthScale() * 2f * Mathf.Pow(2f, levels);
            skirtSize = Mathf.Max(0.05f, (size * 0.5f) / outer);

            center = NewElement("Center", CreatePlaneMesh(2 * k, 2 * k, 1f, Seams.All), material);
            Mesh ring = CreateRingMesh(k, 1f);
            Mesh trim = CreateTrimMesh(k, 1f);
            for (int i = 0; i < levels; i++)
            {
                rings.Add(NewElement("Ring " + i, ring, material));
                trims.Add(NewElement("Trim " + i, trim, material));
            }
            skirt = NewElement("Skirt", CreateSkirtMesh(k, skirtSize), material);
            InnerCell = cell;
        }

        public void SetMaterial(Material material)
        {
            if (center == null) return;
            center.Renderer.sharedMaterial = material;
            skirt.Renderer.sharedMaterial = material;
            foreach (Element e in rings) e.Renderer.sharedMaterial = material;
            foreach (Element e in trims) e.Renderer.sharedMaterial = material;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        public void Destroy()
        {
            DestroyMeshesAndChildren();
            if (root != null) Object.DestroyImmediate(root.gameObject);
        }

        void DestroyMeshesAndChildren()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
            foreach (Mesh m in meshes) if (m != null) Object.DestroyImmediate(m);
            meshes.Clear();
            rings.Clear();
            trims.Clear();
            center = skirt = null;
        }

        // ------------------------------------------------------------------ placement

        // the patch size (m) the original measured everything in: the finest level spans about two of them
        float LengthScale() => cell * GridSize();

        /// <summary>Centres the levels on the viewer (snapped), at the water level.</summary>
        public void UpdatePositions(Vector3 viewer, float waterLevel)
        {
            if (center == null) return;
            int k = GridSize();
            int active = ActiveLevels(viewer.y - waterLevel);
            Vector3 up = Vector3.up * waterLevel;

            float scale = ClipLevelScale(-1, active);
            InnerCell = scale;
            Vector3 previousSnapped = Snap(viewer, scale * 2f);
            center.Transform.position = previousSnapped + OffsetFromCenter(-1, active) + up;
            center.Transform.localScale = new Vector3(scale, 1f, scale);

            for (int i = 0; i < levels; i++)
            {
                bool on = i < active;
                if (rings[i].Transform.gameObject.activeSelf != on)
                {
                    rings[i].Transform.gameObject.SetActive(on);
                    trims[i].Transform.gameObject.SetActive(on);
                }
                if (!on) continue;

                scale = ClipLevelScale(i, active);
                Vector3 centerOffset = OffsetFromCenter(i, active);
                Vector3 snapped = Snap(viewer, scale * 2f);

                Vector3 trimPosition = centerOffset + snapped + scale * (k - 1) / 2f * new Vector3(1f, 0f, 1f);
                int shiftX = previousSnapped.x - snapped.x < float.Epsilon ? 1 : 0;
                int shiftZ = previousSnapped.z - snapped.z < float.Epsilon ? 1 : 0;
                trimPosition += shiftX * (k + 1) * scale * Vector3.right;
                trimPosition += shiftZ * (k + 1) * scale * Vector3.forward;
                trims[i].Transform.position = trimPosition + up;
                trims[i].Transform.rotation = TrimRotations[shiftX + 2 * shiftZ];
                trims[i].Transform.localScale = new Vector3(scale, 1f, scale);

                rings[i].Transform.position = snapped + centerOffset + up;
                rings[i].Transform.localScale = new Vector3(scale, 1f, scale);
                previousSnapped = snapped;
            }

            scale = LengthScale() * 2f * Mathf.Pow(2f, levels);
            skirt.Transform.position = new Vector3(-1f, 0f, -1f) * scale * (skirtSize + 0.5f - 0.5f / k) + previousSnapped + up;
            skirt.Transform.localScale = new Vector3(scale, 1f, scale);
        }

        int ActiveLevels(float height)
        {
            return levels - Mathf.Clamp((int)Mathf.Log((1.7f * Mathf.Abs(height) + 1f) / LengthScale(), 2f), 0, levels);
        }

        float ClipLevelScale(int level, int active)
        {
            return LengthScale() / GridSize() * Mathf.Pow(2f, levels - active + level + 1);
        }

        Vector3 OffsetFromCenter(int level, int active)
        {
            int k = GridSize();
            return (Mathf.Pow(2f, levels) + GeometricProgressionSum(2f, 2f, levels - active + level + 1, levels - 1))
                   * LengthScale() / k * (k - 1) / 2f * new Vector3(-1f, 0f, -1f);
        }

        static float GeometricProgressionSum(float b0, float q, int n1, int n2)
        {
            return b0 / (1f - q) * (Mathf.Pow(q, n2) - Mathf.Pow(q, n1));
        }

        static Vector3 Snap(Vector3 coords, float scale)
        {
            if (coords.x >= 0f) coords.x = Mathf.Floor(coords.x / scale) * scale;
            else coords.x = Mathf.Ceil((coords.x - scale + 1f) / scale) * scale;

            if (coords.z < 0f) coords.z = Mathf.Floor(coords.z / scale) * scale;
            else coords.z = Mathf.Ceil((coords.z - scale + 1f) / scale) * scale;

            coords.y = 0f;
            return coords;
        }

        // ------------------------------------------------------------------ meshes

        Element NewElement(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            return new Element(go.transform, r);
        }

        Mesh Keep(Mesh mesh)
        {
            mesh.hideFlags = HideFlags.HideAndDontSave;
            // vertices are displaced on the GPU: generous bounds so nothing is culled
            Bounds b = mesh.bounds;
            b.Expand(new Vector3(0f, 80f, 0f));
            mesh.bounds = b;
            meshes.Add(mesh);
            return mesh;
        }

        Mesh CreateSkirtMesh(int k, float outerBorderScale)
        {
            var mesh = new Mesh { name = "Clipmap skirt" };
            var combine = new CombineInstance[8];
            Mesh quad = CreatePlaneMesh(1, 1, 1f, Seams.None, 0, false);
            Mesh hStrip = CreatePlaneMesh(k, 1, 1f, Seams.None, 0, false);
            Mesh vStrip = CreatePlaneMesh(1, k, 1f, Seams.None, 0, false);

            var cornerQuadScale = new Vector3(outerBorderScale, 1f, outerBorderScale);
            var midQuadScaleVert = new Vector3(1f / k, 1f, outerBorderScale);
            var midQuadScaleHor = new Vector3(outerBorderScale, 1f, 1f / k);

            combine[0].transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, cornerQuadScale);
            combine[0].mesh = quad;
            combine[1].transform = Matrix4x4.TRS(Vector3.right * outerBorderScale, Quaternion.identity, midQuadScaleVert);
            combine[1].mesh = hStrip;
            combine[2].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1f), Quaternion.identity, cornerQuadScale);
            combine[2].mesh = quad;
            combine[3].transform = Matrix4x4.TRS(Vector3.forward * outerBorderScale, Quaternion.identity, midQuadScaleHor);
            combine[3].mesh = vStrip;
            combine[4].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1f) + Vector3.forward * outerBorderScale, Quaternion.identity, midQuadScaleHor);
            combine[4].mesh = vStrip;
            combine[5].transform = Matrix4x4.TRS(Vector3.forward * (outerBorderScale + 1f), Quaternion.identity, cornerQuadScale);
            combine[5].mesh = quad;
            combine[6].transform = Matrix4x4.TRS(Vector3.right * outerBorderScale + Vector3.forward * (outerBorderScale + 1f), Quaternion.identity, midQuadScaleVert);
            combine[6].mesh = hStrip;
            combine[7].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1f) + Vector3.forward * (outerBorderScale + 1f), Quaternion.identity, cornerQuadScale);
            combine[7].mesh = quad;
            mesh.CombineMeshes(combine, true);
            Object.DestroyImmediate(quad);
            Object.DestroyImmediate(hStrip);
            Object.DestroyImmediate(vStrip);
            return Keep(mesh);
        }

        Mesh CreateTrimMesh(int k, float lengthScale)
        {
            var mesh = new Mesh { name = "Clipmap trim" };
            var combine = new CombineInstance[2];
            combine[0].mesh = CreatePlaneMesh(k + 1, 1, lengthScale, Seams.None, 1, false);
            combine[0].transform = Matrix4x4.TRS(new Vector3(-k - 1, 0f, -1f) * lengthScale, Quaternion.identity, Vector3.one);
            combine[1].mesh = CreatePlaneMesh(1, k, lengthScale, Seams.None, 1, false);
            combine[1].transform = Matrix4x4.TRS(new Vector3(-1f, 0f, -k - 1) * lengthScale, Quaternion.identity, Vector3.one);
            mesh.CombineMeshes(combine, true);
            Object.DestroyImmediate(combine[0].mesh);
            Object.DestroyImmediate(combine[1].mesh);
            return Keep(mesh);
        }

        Mesh CreateRingMesh(int k, float lengthScale)
        {
            var mesh = new Mesh { name = "Clipmap ring" };
            if ((2 * k + 1) * (2 * k + 1) >= 256 * 256) mesh.indexFormat = IndexFormat.UInt32;
            var combine = new CombineInstance[4];
            combine[0].mesh = CreatePlaneMesh(2 * k, (k - 1) / 2, lengthScale, Seams.Bottom | Seams.Right | Seams.Left, 0, false);
            combine[0].transform = Matrix4x4.identity;
            combine[1].mesh = CreatePlaneMesh(2 * k, (k - 1) / 2, lengthScale, Seams.Top | Seams.Right | Seams.Left, 0, false);
            combine[1].transform = Matrix4x4.TRS(new Vector3(0f, 0f, k + 1 + (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);
            combine[2].mesh = CreatePlaneMesh((k - 1) / 2, k + 1, lengthScale, Seams.Left, 0, false);
            combine[2].transform = Matrix4x4.TRS(new Vector3(0f, 0f, (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);
            combine[3].mesh = CreatePlaneMesh((k - 1) / 2, k + 1, lengthScale, Seams.Right, 0, false);
            combine[3].transform = Matrix4x4.TRS(new Vector3(k + 1 + (k - 1) / 2, 0f, (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);
            mesh.CombineMeshes(combine, true);
            foreach (CombineInstance c in combine) Object.DestroyImmediate(c.mesh);
            return Keep(mesh);
        }

        Mesh CreatePlaneMesh(int width, int height, float lengthScale, Seams seams = Seams.None, int trianglesShift = 0, bool keep = true)
        {
            var mesh = new Mesh { name = "Clipmap plane" };
            if ((width + 1) * (height + 1) >= 256 * 256) mesh.indexFormat = IndexFormat.UInt32;
            var vertices = new Vector3[(width + 1) * (height + 1)];
            var triangles = new int[width * height * 2 * 3];
            var normals = new Vector3[(width + 1) * (height + 1)];

            for (int i = 0; i < height + 1; i++)
            {
                for (int j = 0; j < width + 1; j++)
                {
                    int x = j;
                    int z = i;
                    // the outer edge only uses every other vertex: it meets the coarser level without cracks
                    if ((i == 0 && (seams & Seams.Bottom) != 0) || (i == height && (seams & Seams.Top) != 0))
                        x = x / 2 * 2;
                    if ((j == 0 && (seams & Seams.Left) != 0) || (j == width && (seams & Seams.Right) != 0))
                        z = z / 2 * 2;
                    vertices[j + i * (width + 1)] = new Vector3(x, 0f, z) * lengthScale;
                    normals[j + i * (width + 1)] = Vector3.up;
                }
            }

            int tris = 0;
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    int k = j + i * (width + 1);
                    if ((i + j + trianglesShift) % 2 == 0)
                    {
                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + width + 2;
                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 2;
                        triangles[tris++] = k + 1;
                    }
                    else
                    {
                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + 1;
                        triangles[tris++] = k + 1;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + width + 2;
                    }
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            return keep ? Keep(mesh) : mesh;
        }

        sealed class Element
        {
            public readonly Transform Transform;
            public readonly MeshRenderer Renderer;

            public Element(Transform transform, MeshRenderer renderer)
            {
                Transform = transform;
                Renderer = renderer;
            }
        }

        [System.Flags]
        enum Seams
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8,
            All = Left | Right | Top | Bottom
        }
    }
}

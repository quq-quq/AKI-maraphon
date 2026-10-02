using System;
using UnityEditor;
using UnityEngine;

namespace AKI.Fish.Editor
{
    // Replace only the embedded mesh data, retaining its object/file ID.
    // The original FBX hierarchy, Avatar and animation clips are not re-exported.
    public sealed class FishMeshOptimizationPostprocessor : AssetPostprocessor
    {
        const string ModelPath = "Assets/Core/Visual/Models/Fish/Fish.fbx";
        const string MeshPath = "Assets/Core/Visual/Models/Fish/Optimization/Fish_OptimizedMesh.asset";

        public override uint GetVersion() => 3;

        void OnPostprocessModel(GameObject model)
        {
            if (assetPath != ModelPath) return;
            context.DependsOnSourceAsset(MeshPath);
            var optimized = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (optimized == null) throw new InvalidOperationException("Missing optimized tuna mesh: " + MeshPath);
            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null || renderer.name != "Fish_Mesh") throw new InvalidOperationException("Unexpected tuna renderer.");
            var mesh = renderer.sharedMesh;
            if (optimized.bindposes.Length != renderer.bones.Length || optimized.subMeshCount != mesh.subMeshCount)
                throw new InvalidOperationException("Optimized tuna rig or material layout mismatch.");
            var originalBindposes = mesh.bindposes;
            for (int b = 0; b < originalBindposes.Length; b++)
                for (int i = 0; i < 16; i++)
                    if (Mathf.Abs(originalBindposes[b][i] - optimized.bindposes[b][i]) > .0001f)
                        throw new InvalidOperationException("Optimized tuna bind pose mismatch.");

            // Imported meshes may already be marked non-readable at this callback.
            // Native serialization replaces the buffers without requiring runtime
            // Read/Write, and preserves the destination object's identity.
            string originalName = mesh.name;
            EditorUtility.CopySerialized(optimized, mesh);
            mesh.name = originalName;
            mesh.UploadMeshData(true);
            // Keep the original mesh name and renderer.localBounds for stable
            // serialized references and animated culling bounds.
        }
    }
}

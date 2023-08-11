using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Indirection texture that contains the index of the textures at specific points in space
    private Texture3D indirectionTexture;

    // List of texture "segments" that will be sparsely stored in the world
    private List<Texture3D> editedVoxels;

    // Initialize the voxel edits handler
    internal override void Init()
    {
        editedVoxels = new List<Texture3D>();
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the terrain world immediately
    public void ApplyVoxelEdit<T>(T edit) where T : struct, IVoxelEdit
    {
        // Get the edit's world AABB
        float3 extents = edit.GetWorldExtents();
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        // Find the chunks affected by the edit
        if (terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output))
        {
            for (int i = 0; i < output.Value.Length; i++)
            {
                // Fetch chunk offsets + scale (like for compute shader)
                VoxelChunk chunk = terrain.Chunks[output.Value[i]];
                Vector3 offset = Vector3.one * (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) * 0.5F;
                Vector3 chunkOffset = (chunk.transform.position - offset) / VoxelUtils.VoxelSize;

                float scale = ((chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) / VoxelUtils.VoxelSize);

                // Begin the jobs for the affected chunks (synchronous)
                var job = new VoxelEditJob<T>
                {
                    chunkOffset = new float3(chunkOffset.x, chunkOffset.y, chunkOffset.z),
                    scale = scale,
                    voxels = chunk.voxels,
                    edit = edit,
                    worldScale = VoxelUtils.VoxelSize,
                }.Schedule(VoxelUtils.Volume, 512);

                // try generating the mesh immediately
                if (!terrain.VoxelMesher.TryGenerateMeshImmediate(chunk, chunk.AsContainer(), true, out _, job))
                {
                    // if not possible, fallback to async
                    terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true, job);
                }
            }

            output.Value.Dispose();

            Debug.Log("Appled immediate voxel edit");
        }

    }

    private void OnDrawGizmos()
    {
        /*
        var gm = GameObject.FindGameObjectWithTag("cueb");

        var edit = new SphereEdit {
            center = new float3(gm.transform.position.x, gm.transform.position.y, gm.transform.position.z),
            radius = 5
        };

        float3 extents = edit.GetWorldExtents();
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        if (terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output))
        {
            foreach (var item in output)
            {
                Gizmos.DrawWireCube(item.WorldCenter(), item.WorldSize());
            }

            output.Value.Dispose();
        }
        */
    }
}

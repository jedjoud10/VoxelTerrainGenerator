using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Maximum number of mesh jobs reserved for editing
    [Range(1, 8)]
    public int reservedMeshJobs = 1;

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
    public void ApplyVoxelEdit(IVoxelEdit edit)
    {
        // Get the edit's world AABB
        Vector3 extents = edit.GetWorldExtents();
        Vector3 center = edit.GetWorldCenter();

        float3 centerFloat3 = new float3(center.x, center.y, center.z);
        float3 extentsFloat3 = new float3(extents.x, extents.y, extents.z);

        float3 min = centerFloat3 - extentsFloat3 / 2;
        float3 max = centerFloat3 + extentsFloat3 / 2;

        // Find the chunks affected by the edit
        if (terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output))
        {
            VoxelChunk[] chunks = new VoxelChunk[output.Value.Length];
            for (int i = 0; i < output.Value.Length; i++)
            {
                chunks[i] = terrain.Chunks[output.Value[i]];
            }

            // Begin the jobs for the affected chunks (synchronous)
            edit.BeginEditJobs(chunks);
            output?.Dispose();
            NativeArray<JobHandle> handles = new NativeArray<JobHandle>(edit.GetJobHandles(), Allocator.Temp);

            // Wait for completion immediately
            JobHandle.CompleteAll(handles);

            foreach (var chunk in chunks)
            {
                terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true);
            }

            Debug.Log("Appled immediate voxel edit");

            handles.Dispose();
        }

    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
// We will assume that the player can only edits the LOD0 chunks
// Everytime we edit a chunk (LOD0), we keep a copy of the edited voxel data on the CPU
// Everytime we generate a new chunk, we look at the CPU voxel data and use it to overwrite the data if needed
// This makes it so that the data that needs to be serialized / deserialized scales with the number of chunks edited
// and not the number of edits in the world

public class VoxelEdits : VoxelBehaviour
{
    // Max number of chunks we should edit at the same time (should be less than or equal to max mesh jobs)
    [Range(0, 8)]
    public int maxImmediateMeshEditJobsPerEdit = 1;

    // Sparse array of voxel edits to be applied to new chunks
    // TODO: Implement some sort of streaming system so we don't have to keep these in memory all the time
    private List<SparseVoxelData> sparseVoxelData;

    // Initialize the voxel edits handler
    internal override void Init()
    {
        sparseVoxelData = new List<SparseVoxelData>();
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the terrain world either immediately or asynchronously
    public void ApplyVoxelEdit<T>(T edit, bool immediate = false) where T : struct, IVoxelEdit
    {
        if (!terrain.Free || !terrain.VoxelGenerator.Free || !terrain.VoxelMesher.Free || !terrain.VoxelOctree.Free)       
            return;

        // Idk why we have to do this bruh this shit don't make no sense 
        float extentOffset = VoxelUtils.VoxelSizeFactor * 4.0F;

        // Get the edit's world AABB
        float3 extents = edit.GetWorldExtents() + math.float3(extentOffset);
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        // Find the chunks affected by the edit
        if (!terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output))
            return;

        // We don't support editing non LOD0 chunks atm
        if (output.Value.AsArray().AsReadOnlySpan().ToArray().Any(x => x.Depth != x.maxDepth))
        {
            Debug.LogError("Editing non LOD0 chunks is not supported");
            return;
        }

        for(int i = 0; i < output.Value.Length; i++)
        {
            // Fetch chunk offsets + scale (like for compute shader)
            VoxelChunk chunk = terrain.Chunks[output.Value[i]];
            Vector3 chunkOffset = chunk.transform.position;
            float scale = chunk.transform.localScale.x;

            // Begin the jobs for the affected chunks (synchronous)
            var job = new VoxelEditJob<T>
            {
                edit = edit,
                chunkOffset = new float3(chunkOffset.x, chunkOffset.y, chunkOffset.z),
                chunkScale = scale,
                voxelScale = VoxelUtils.VoxelSizeFactor,
                size = (float)VoxelUtils.Size,
                voxels = chunk.voxels,
                vertexScaling = VoxelUtils.VertexScaling,
            }.Schedule(VoxelUtils.Volume, 2048);

            if (immediate && i < maxImmediateMeshEditJobsPerEdit)
            {
                if(!terrain.VoxelMesher.TryGenerateMeshImmediate(chunk, chunk.AsContainer(), true, job))
                {
                    // Technically this should never happen but in case it does this is here
                    terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true, job);
                }
            } else
            {
                terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true, job);
            }
        }

        output.Value.Dispose();
    }
}

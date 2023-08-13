using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world

// Everytime we edit a chunk (LOD0), we keep a copy of the edited voxel data on the CPU
// Everytime we generate a new chunk, we look at the CPU voxel data and use it to overwrite the data if needed
// This makes it so that the data that needs to be serialized / deserialized scales with the number of chunks edited
// and not the number of edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Indirection texture that contains the index of the textures at specific points in space
    private Texture3D indirectionTexture;

    // List of texture "segments" that will be sparsely stored in the world
    private List<Voxel> editedVoxels;

    // Initialize the voxel edits handler
    internal override void Init()
    {
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the terrain world either immediately or asynchronously
    public void ApplyVoxelEdit<T>(T edit, bool immediate = false) where T : struct, IVoxelEdit
    {
        if (!terrain.Free)
        {
            // We can't really do much if the terrain is busy and we need an immediate edit
            Debug.LogWarning("Terrain currently active!");            
            return;
        }

        // Idk why we have to do this bruh this shit don't make no sense 
        float extentOffset = VoxelUtils.VoxelSize * 4.0F;

        // Get the edit's world AABB
        float3 extents = edit.GetWorldExtents() + math.float3(extentOffset);
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        // Find the chunks affected by the edit
        terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output);

        for(int i = 0; i < output.Value.Length; i++)
        {
            // Fetch chunk offsets + scale (like for compute shader)
            VoxelChunk chunk = terrain.Chunks[output.Value[i]];
            Vector3 offset = Vector3.one * (chunk.transform.localScale.x / ((float)VoxelUtils.Size)) * 0.5F;
            Vector3 chunkOffset = (chunk.transform.position / VoxelUtils.VoxelSize) / VoxelUtils.VertexScaling - offset;
            float scale = chunk.transform.localScale.x;

            // Begin the jobs for the affected chunks (synchronous)
            var job = new VoxelEditJob<T>
            {
                edit = edit,
                chunkOffset = new float3(chunkOffset.x, chunkOffset.y, chunkOffset.z),
                chunkScale = scale,
                voxelScale = VoxelUtils.VoxelSize,
                voxels = chunk.voxels,
                vertexScaling = VoxelUtils.VertexScaling,
            }.Schedule(VoxelUtils.Volume, 2048);

            if (immediate)
            {
                terrain.VoxelMesher.TryGenerateMeshImmediate(chunk, chunk.AsContainer(), true, job);
            } else
            {
                terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true, job);
            }
        }

        output.Value.Dispose();
    }
}

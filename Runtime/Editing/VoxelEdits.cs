using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
// We will assume that the player can only edits the LOD0 chunks
// Everytime we edit a chunk (LOD0), we make a separate voxel data "delta" voxel array
// This new voxel array will then be additively added ontop of chunks further away

public class VoxelEdits : VoxelBehaviour
{
    // Max number of chunks we should edit at the same time (should be less than or equal to max mesh jobs)
    [Range(0, 8)]
    public int maxImmediateMeshEditJobsPerEdit = 1;

    // Sparse array of voxel edits to be applied to new chunks
    private UnsafeList<NativeArray<Voxel>> sparseVoxelData;

    // Initialize the voxel edits handler
    internal override void Init()
    {
        sparseVoxelData = new UnsafeList<NativeArray<Voxel>>();
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
        Bounds bound = edit.GetBounds();
        bound.Expand(extentOffset);

        // Find LOD0 delta chunks that we can write to
        //    Initialize them if needed
    }
}

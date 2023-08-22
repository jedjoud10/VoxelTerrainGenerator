using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Sparse voxel data for a single edited chunk
// This will be stored in the voxel edits behavior and applied to the incoming voxel data sequentially
public struct SparseVoxelData
{
    // Voxel data that we must apply to the shunk
    public NativeArray<Voxel> voxels;

    // The author octree node (might be unsynced with the current tree)
    public OctreeNode author;
}
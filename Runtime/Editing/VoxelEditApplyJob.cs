using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Apply job that will take in a voxel data of a chunk, and the sparse voxel data array, and additively blend them together
// This will be executed for every new chunk that intersects the sparse voxel data array octree
[BurstCompile(CompileSynchronously = true)]
struct VoxelEditApplyJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit
{
    // Voxels of the current chunk
    public NativeArray<Voxel> voxels;

    // Sparse voxel data that we will check against
    [ReadOnly]
    public UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    // Voxel terrain region 
    [ReadOnly]
    public VoxelDeltaRegion region;

    // Octree node of the current chunk
    [ReadOnly]
    public OctreeNode node;

    [ReadOnly] public int maxSegments;
    [ReadOnly] public int size;

    public void Execute(int index)
    {
        // Get the world space position of this voxel
        uint3 localPos = VoxelUtils.IndexToPos(index);
        int3 worldSpacePos = math.int3(node.Position) + math.int3(localPos) * (int)node.ScalingFactor;

        /*
        int3 segmentsWorldSpacePos = worldSpacePos / (4 * size);
        uint3 offsettedSegmentsWorldSpacePos = math.uint3(segmentsWorldSpacePos + math.int3(maxSegments / 2));

        // Find the segment that contains this voxel
        int segmentIndex = VoxelUtils.PosToIndex(offsettedSegmentsWorldSpacePos, (uint)maxSegments);

        // Get the segment and fetch the chunk from it that we must read from
        ulong segment = segments[segmentIndex];
        */

        // somehow find the chunks we will need to apply to this voxel (worse case scenario, it's a a whole chunk)
        // loop though the other chunk's voxe data and apply it
    }
}
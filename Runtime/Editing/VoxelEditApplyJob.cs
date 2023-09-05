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
    public UnsafeList<NativeArray<Voxel>> sparseVoxelData;

    public void Execute(int index)
    {
    }
}
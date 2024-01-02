using System.Collections;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Apply job that will take in a voxel data of a chunk, and the sparse voxel data array, and additively blend them together
// This will be executed for every new chunk that intersects the sparse voxel data array octree but also on previously spawned chunks
// TODO: At higher LODs take an average of the chunk voxels instead of taking the nearest neighbor value
[BurstCompile(CompileSynchronously = true)]
public struct VoxelEditApplyJob : IJobParallelFor {
    // Voxels of the current chunk at gen (should NOT be modified)
    [ReadOnly]
    public NativeArray<Voxel> inputVoxels;

    // Output voxels that the mesher will use
    [WriteOnly]
    public NativeArray<Voxel> outputVoxels;

    // Octree node of the current chunk
    [ReadOnly]
    public OctreeNode node;

    // Dictionary to map chunk positions to sparseVoxelData indices
    [ReadOnly]
    public NativeArray<VoxelDeltaLookup> lookup;

    // All the chunks the user has modified for the corresponding LOD level
    [ReadOnly]
    public UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    [ReadOnly] public float vertexScaling;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int maxSegments;
    [ReadOnly] public int chunksPerSegment;
    [ReadOnly] public int segmentSize;
    [ReadOnly] public int size;

    public void Execute(int index) {
    }
}
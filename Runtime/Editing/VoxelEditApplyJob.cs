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
    // Output voxels that the mesher will use
    public NativeArray<Voxel> voxels;

    // Octree node of the current chunk
    [ReadOnly]
    public OctreeNode node;

    // Dictionary to map chunk positions to sparseVoxelData indices
    [ReadOnly]
    public NativeArray<VoxelDeltaLookup> lookup;

    // The highest quality chunks that the user has modified
    [ReadOnly]
    public UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    [ReadOnly] public float vertexScaling;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int maxSegments;
    [ReadOnly] public int chunksPerSegment;
    [ReadOnly] public int segmentSize;
    [ReadOnly] public int size;

    public void Execute(int index) {
        // Get the world space position of this voxel
        uint3 localPos = VoxelUtils.IndexToPos(index);
        float3 position = math.float3(localPos);
        //position -= 1.0f;
        //position *= vertexScaling;
        position *= node.ScalingFactor;
        //position += math.float3((node.Position - (node.Size / (size - 3.0f)) * 0.5f));
        //position += math.float3((node.Position - (size / (size - 3.0f)) * 0.5f));
        position += node.Position;

        if (node.ScalingFactor == 2f) {
            position -= 1;
        } else if (node.ScalingFactor == 4f) {
            position -= 5;
        }

        // Get the segment and chunk in which this voxel resides
        int3 worldSegment = (int3)math.floor(position / segmentSize);
        
        // Get the proper chunk for our desired LOD level
        int3 worldChunk = (int3)math.floor(position / size);
        uint3 segmentChunk = VoxelUtils.Mod(worldChunk, chunksPerSegment);

        // Convert to indices (must also shift to compensate for unsigned)
        uint3 unsignedWorldSegment = math.uint3(worldSegment + math.int3(maxSegments / 2));
        int segment = VoxelUtils.PosToIndex(unsignedWorldSegment, (uint)maxSegments);
        int chunkIndex = VoxelUtils.PosToIndex(segmentChunk, (uint)chunksPerSegment);

        // Get the index of the sparseVoxelData that we must read from
        VoxelDeltaLookup temp = lookup[math.clamp(segment, 0, maxSegments*maxSegments*maxSegments-1)];
        int sparseIndex = temp.startingIndex + chunkIndex;
        uint3 worldVoxelPositive = (uint3)VoxelUtils.Mod(position, size);
        int voxelIndex = VoxelUtils.PosToIndex(worldVoxelPositive);

        if (chunkIndex < temp.bitset.Length && temp.bitset.IsSet(chunkIndex)) {
            SparseVoxelDeltaData data = sparseVoxelData[sparseIndex];
            half deltaDensity = data.densities[voxelIndex];
            ushort deltaMaterial = data.materials[voxelIndex];

            // Voxel sparse offset in case we need to read from higher LOD
            Voxel cur = voxels[index];

            if (deltaMaterial != ushort.MaxValue) {
                cur.material = deltaMaterial;
            }

            cur.density += deltaDensity;
            voxels[index] = cur;
        }
    }
}
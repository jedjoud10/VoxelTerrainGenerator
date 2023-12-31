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

    // All the chunks the user has modified
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
        position -= 1.0f;
        position *= vertexScaling;
        position *= node.ScalingFactor;
        position += math.float3((node.Position - (size / (size - 3.0f)) * 0.5f));
        //position += math.float3((node.Position - (size / (size - 3.0f)) * 0.5f));
        //position += node.Position;
        //position += (node.ScalingFactor - 1) * -useAvg;

        // Get the segment and chunk in which this voxel resides
        int3 worldSegment = (int3)math.floor(position / segmentSize);
        int3 worldChunk = (int3)math.floor(position / size);
        uint3 segmentChunk = VoxelUtils.Mod(worldChunk, chunksPerSegment);

        // Convert to indices (must also shift to compensate for unsigned)
        uint3 unsignedWorldSegment = math.uint3(worldSegment + math.int3(maxSegments / 2));
        int segment = VoxelUtils.PosToIndex(unsignedWorldSegment, (uint)maxSegments);
        int chunkIndex = VoxelUtils.PosToIndex(segmentChunk, (uint)chunksPerSegment);

        // Get the index of the sparseVoxelData that we must read from
        VoxelDeltaLookup temp = lookup[math.clamp(segment, 0, maxSegments*maxSegments*maxSegments-1)];
        int sparseIndex = temp.startingIndex + chunkIndex;

        int avgVoxelSize = (int)node.ScalingFactor;
        int avgVoxelVolume = avgVoxelSize * avgVoxelSize * avgVoxelSize;

        // Apply the voxel delta change
        if (chunkIndex < temp.bitset.Length && temp.bitset.IsSet(chunkIndex)) {
            SparseVoxelDeltaData data = sparseVoxelData[math.max(sparseIndex, 0)];

            half summedDensityDelta = half.zero;
            /*
            for (int i = 0; i < avgVoxelVolume; i++) {
                 uint3 posOffset = VoxelUtils.IndexToPos(i, (uint)avgVoxelSize);
                 float3 newPosition = math.min(math.float3(position + posOffset), math.float3(size));
                 float3 worldVoxelPositive = VoxelUtils.Mod(newPosition, size);
                 //int voxelIndex = VoxelUtils.PosToIndex(worldVoxelPositive);
                 //summedDensityDelta += data.densities[math.max(voxelIndex, 0)];
                 summedDensityDelta += VoxelUtils.SampleGridInterpolated(worldVoxelPositive, ref data.densities, size);
            }
            summedDensityDelta = (half)(summedDensityDelta / avgVoxelVolume);
            */
            
            float3 worldVoxelPositive = VoxelUtils.Mod(position, size);
            int voxelIndex = VoxelUtils.PosToIndex((uint3)worldVoxelPositive);
            summedDensityDelta = data.densities[math.max(voxelIndex, 0)];
            //summedDensityDelta = VoxelUtils.SampleGridInterpolated(worldVoxelPositive, ref data.densities, size);
            /*
            half deltaDensity = data.densities[math.max(voxelIndex, 0)];
            ushort deltaMaterial = data.materials[math.max(voxelIndex, 0)];
            */
            Voxel cur = inputVoxels[index];
            cur.density += summedDensityDelta;
            
            /*
            if (deltaMaterial != ushort.MaxValue) {
                cur.material = deltaMaterial;
            }
            */

            outputVoxels[index] = cur;
        }
    }
}
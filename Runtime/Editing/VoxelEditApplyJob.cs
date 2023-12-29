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

        /*
            position -= 1.0;
    
            // Needed for voxel size reduction
            position *= voxelSize;

            // Chunk offsets + vertex scaling
            position *= vertexScaling;
            position *= chunkScale;
            position += (chunkOffset - ((chunkScale * size) / (size - 3.0)) * 0.5);
            position = round(position * 100) / 100;
            
            // World offset and scale
            position = position * worldScale + worldOffset;
        */

        float3 position = math.float3(localPos) + node.Position;
        /*
        position -= math.float3(1.0);

        // Needed for voxel size reduction
        position *= voxelScale;

        // Chunk offsets + vertex scaling
        position *= vertexScaling;
        position += math.float3((node.Position - (size / (size - 3.0f)) * 0.5f));
        */

        // Get the segment and chunk in which this voxel resides
        int3 worldSegment = (int3)math.floor(position / segmentSize);
        int3 worldChunk = (int3)math.floor(position / size);
        uint3 segmentChunk = VoxelUtils.Mod(worldChunk, chunksPerSegment);

        // Convert to indices (must also shift to compensate for unsigned)
        uint3 unsignedWorldSegment = math.uint3(worldSegment + math.int3(maxSegments / 2));
        int segment = VoxelUtils.PosToIndex(unsignedWorldSegment, (uint)maxSegments);
        int chunkIndex = VoxelUtils.PosToIndex(segmentChunk, (uint)chunksPerSegment);

        // Get the index of the sparseVoxelData that we must read from
        VoxelDeltaLookup temp = lookup[segment];
        int sparseIndex = temp.startingIndex + chunkIndex;
        uint3 worldVoxelPositive = (uint3)VoxelUtils.Mod(position, size);
        int voxelIndex = VoxelUtils.PosToIndex(worldVoxelPositive);

        // Apply the voxel delta change
        if (chunkIndex < temp.bitset.Length && temp.bitset.IsSet(chunkIndex)) {
            //voxels[index] = Voxel.Empty;
            SparseVoxelDeltaData data = sparseVoxelData[sparseIndex];
            half deltaDensity = data.densities[voxelIndex];
            ushort deltaMaterial = data.materials[voxelIndex];
            Voxel cur = inputVoxels[index];
            cur.density += deltaDensity;
            
            if (deltaMaterial != ushort.MaxValue) {
                cur.material = deltaMaterial;
            }

            outputVoxels[index] = cur;
        }
    }
}
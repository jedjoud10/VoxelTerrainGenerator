using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Edit job that will create the delta voxel data for each chunk
[BurstCompile(CompileSynchronously = true)]
struct VoxelEditJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit {
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public float size;
    [ReadOnly] public float vertexScaling;

    public T edit;

    // The chunk delta data that we actually must read from
    [ReadOnly]
    public int sparseVoxelDataChunkIndex;

    // All the sparse chunks currently stored
    public UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    public void Execute(int index) {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id));

        position -= math.float3(1.0);

        // Needed for voxel size reduction
        position *= voxelScale;

        // Chunk offsets + vertex scaling
        position *= vertexScaling;
        position += math.float3((chunkOffset - (size / (size - 3.0f)) * 0.5f));

        // Read, modify, write
        SparseVoxelDeltaData deltas = sparseVoxelData[sparseVoxelDataChunkIndex];
        ushort material = deltas.materials[index];
        half density = deltas.densities[index];
        Voxel output = edit.Modify(position, new Voxel { material = material, density = density });
        deltas.materials[index] = output.material;
        deltas.densities[index] = output.density;
    }
}
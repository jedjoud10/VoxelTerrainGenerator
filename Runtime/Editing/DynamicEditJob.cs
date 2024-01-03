using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Edit job for dynamic edits
[BurstCompile(CompileSynchronously = true)]
struct DynamicEditJob<T> : IJobParallelFor
    where T : struct, IDynamicEdit {
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScaling;

    public T dynamicEdit;
    public NativeArray<Voxel> voxels;

    public void Execute(int index) {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id));

        // Needed for voxel size reduction
        position -= math.float3(1);
        position *= voxelScale;
        position *= vertexScaling;
        //position += chunkOffset;

        // Chunk offsets + vertex scaling
        position += math.float3((chunkOffset - (size / (size - 3.0f)) * 0.5f));

        // Read, modify, write
        Voxel output = dynamicEdit.Modify(position, voxels[index]);
        voxels[index] = output;
    }
}
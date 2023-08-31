using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Edit job that will change the voxel data for a single chunk
[BurstCompile(CompileSynchronously = true)]
struct VoxelEditJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit
{
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float chunkScale;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public float size;
    [ReadOnly] public float vertexScaling;

    public T edit;

    public NativeArray<Voxel> voxels;

    public void Execute(int index)
    {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id));

        position -= math.float3(1.0);

        // Needed for voxel size reduction
        position *= voxelScale;

        // Chunk offsets + vertex scaling
        position *= chunkScale;
        position += (chunkOffset - ((chunkScale * size) / (size - 3.0F)) * 0.5F) / vertexScaling;
        position *= vertexScaling;
        voxels[index] = edit.Modify(voxels[index], position);
    }
}
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
    public float3 chunkOffset;
    public float chunkScale;
    public float worldScale;
    public float size;
    public float vertexScaling;

    public T edit;

    public NativeArray<Voxel> voxels;

    public void Execute(int index)
    {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id) * chunkScale + chunkOffset);
        position *= vertexScaling;
        position *= worldScale;
        voxels[index] = edit.Modify(voxels[index], position);
    }
}
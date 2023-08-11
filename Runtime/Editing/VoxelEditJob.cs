using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Edit job that will change the voxel data for a single chunk
struct VoxelEditJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit
{
    public float3 chunkOffset;
    public float scale;
    public float worldScale;

    public T edit;

    public NativeArray<Voxel> voxels;

    public void Execute(int index)
    {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id) * scale + chunkOffset) * worldScale;
        voxels[index] = edit.Modify(voxels[index], position);
    }
}
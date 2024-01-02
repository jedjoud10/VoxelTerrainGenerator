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
// This executes for VoxelUtils.DeltaVolume size instead of VoxelUtils.Volume
// Because we must also affect the higher LOD chunk values as well
[BurstCompile(CompileSynchronously = true)]
struct VoxelEditJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit {
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScaling;

    public T edit;
    public UnsafeList<half> densities;
    public UnsafeList<ushort> materials;

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
        ushort material = materials[index];
        half density = densities[index];
        Voxel output = edit.Modify(position, new Voxel { material = material, density = density });
        materials[index] = output.material;
        densities[index] = output.density;
    }
}
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Sphere edit job that will create update the voxel field
struct SphereEditJob : IJobParallelFor
{
    public float3 center;
    public float radius;
    public float3 chunkOffset;
    public float scale;
    public NativeArray<Voxel> voxels;

    public void Execute(int index)
    {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id) * scale + chunkOffset);

        if (math.length(position - center) < radius)
        {
            voxels[index] = new Voxel
            {
                density = math.half(100),
                material = 0
            };
        }
    }
}

// Simple sphere edit that edits the chunk in a specific radius
public struct SphereEdit : IVoxelEdit
{
    public Vector3 center;
    public float radius;

    JobHandle[] handles;

    public SphereEdit(Vector3 center, float radius)
    {
        this.center = center;
        this.radius = radius;
        handles = new JobHandle[0];
    }

    public void BeginEditJobs(VoxelChunk[] chunks)
    {
        handles = new JobHandle[chunks.Length];

        for (int i = 0; i < chunks.Length; i++)
        {
            VoxelChunk chunk = chunks[i];
            Vector3 offset = Vector3.one * (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) * 0.5F;
            Vector3 chunkOffset = (chunk.transform.position - offset) / VoxelUtils.VoxelSize;

            float scale = ((chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) / VoxelUtils.VoxelSize);

            handles[i] = new SphereEditJob
            {
                center = new float3(center.x, center.y, center.z),
                radius = radius,
                chunkOffset = new float3(chunkOffset.x, chunkOffset.y, chunkOffset.z),
                scale = scale,
                voxels = chunk.voxels,
            }.Schedule(VoxelUtils.Volume, 512);
        }

    }

    public JobHandle[] GetJobHandles()
    {
        return handles;
    }

    public Vector3 GetWorldCenter()
    {
        return center;
    }

    public Vector3 GetWorldExtents()
    {
        return new Vector3(radius, radius, radius) * 2.0F; 
    }
}
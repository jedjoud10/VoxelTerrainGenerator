using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<CuboidEdit>))]

// Simple cuboid edit that edits the chunk in a specific extent
public struct CuboidEdit : IVoxelEdit
{
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 extent;
    [ReadOnly] public bool add;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public float3 GetWorldCenter()
    {
        return center;
    }

    public float3 GetWorldExtents()
    {
        return extent;
    }

    public Voxel Modify(Voxel input, float3 position)
    {
        float3 q = math.abs(position - center) - (extent / 2.0F);
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        float added = math.half(math.min(input.density, density));
        float removed = math.half(math.max(input.density, -density));
        input.density = (half)math.select(removed, added, add);

        if (writeMaterial && density < 0)
        {
            input.material = material;
        }

        return input;
    }
}
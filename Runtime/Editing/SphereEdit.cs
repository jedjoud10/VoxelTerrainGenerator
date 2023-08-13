using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<SphereEdit>))]

// Simple sphere edit that edits the chunk in a specific radius
public struct SphereEdit : IVoxelEdit
{
    [ReadOnly] public float3 center;
    [ReadOnly] public float radius;
    [ReadOnly] public bool add;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public float3 GetWorldCenter()
    {
        return center;
    }

    public float3 GetWorldExtents()
    {
        return new Vector3(radius, radius, radius) * 2.0F; 
    }

    public Voxel Modify(Voxel input, float3 position)
    {
        float density = math.length(position - center) - radius;

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
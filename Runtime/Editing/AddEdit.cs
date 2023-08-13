using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<AddEdit>))]

// Will either add / remove matter from the terrain
public struct AddEdit : IVoxelEdit
{
    [ReadOnly] public float3 center;
    [ReadOnly] public float strength;
    [ReadOnly] public float radius;

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

        if (density < 0)
        {
            input.density += math.half(strength);
        }

        return input;
    }
}
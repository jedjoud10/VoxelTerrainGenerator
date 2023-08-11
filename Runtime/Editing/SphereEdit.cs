using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Simple sphere edit that edits the chunk in a specific radius
public struct SphereEdit : IVoxelEdit
{
    public float3 center;
    public float radius;

    public float3 GetWorldCenter()
    {
        return center;
    }

    public float3 GetWorldExtents()
    {
        return new Vector3(radius, radius, radius) * 3.0F; 
    }

    public Voxel Modify(Voxel input, float3 position)
    {
        if (math.length(position - center) < radius)
        {
            input.density = math.half(100);
            input.material = 0;
        }

        return input;
    }
}
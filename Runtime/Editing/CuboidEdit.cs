using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<CuboidEdit>))]

// Simple cuboid edit that edits the chunk in a specific extent
public struct CuboidEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public float strength;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = halfExtents * 2
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        float3 q = math.abs(position - center) - halfExtents;
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        Voxel voxel = Voxel.Empty;
        voxel.material = (density < 0.0F && writeMaterial) ? material : ushort.MaxValue;
        voxel.density = (density < 0.0F) ? (half)(-strength) : lastDelta.density;
        return voxel;
    }
}
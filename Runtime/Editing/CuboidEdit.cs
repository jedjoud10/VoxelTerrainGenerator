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
    [ReadOnly] public float3 extents;
    [ReadOnly] public bool add;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = extents
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        float3 q = math.abs(position - center) - (extents / 2.0F);
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        Voxel voxel = Voxel.Empty;
        float added = math.half(math.min(lastDelta.density, density));
        float removed = math.half(math.max(lastDelta.density, -density));
        voxel.density = (half)math.select(removed, added, add);

        if (writeMaterial && density < 0) {
            voxel.material = material;
        }

        return voxel;
    }
}
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(DynamicEditJob<CuboidDynamicEdit>))]

public struct CuboidDynamicEdit : IDynamicEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public float strength;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;
    public bool Enabled => true;

    public JobHandle Apply(VoxelChunk chunk) {
        return IDynamicEdit.ApplyGeneric(chunk, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = halfExtents * 2
        };
    }

    public Voxel Modify(float3 position, Voxel voxel) {
        float3 q = math.abs(position - center) - halfExtents;
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        if (density < 1.0 && writeMaterial) {
            voxel.material = material;
        }

        voxel.density = (half)math.min(voxel.density, density);
        return voxel;
    }
}
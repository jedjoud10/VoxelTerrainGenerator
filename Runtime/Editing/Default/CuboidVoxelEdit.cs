using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(RunLengthEncoder<CuboidVoxelEdit>))]

public struct CuboidVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public float strength;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public JobHandle Apply(SparseVoxelDeltaData data, int3 position) {
        return IVoxelEdit.ApplyGeneric(position, data.densities, data.materials, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = halfExtents * 2
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        float3 q = math.abs(position - center) - halfExtents;
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        Voxel voxel = lastDelta;
        voxel.material = (density < 1.0F && writeMaterial) ? material : voxel.material;
        voxel.density = (density < 0.0F) ? (half)(-strength) : lastDelta.density;
        return voxel;
    }
}
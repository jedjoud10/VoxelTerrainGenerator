using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<SphereVoxelEdit>))]

public struct SphereVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float radius;
    [ReadOnly] public float strength;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public JobHandle Apply(SparseVoxelDeltaData data, int3 position) {
        return IVoxelEdit.ApplyGeneric(position, data.densities, data.materials, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = new Vector3(radius, radius, radius) * 2.0F,
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        Voxel voxel = lastDelta;
        float density = math.length(position - center) - radius;
        voxel.material = (density < 1.0F && writeMaterial) ? material : voxel.material;
        voxel.density = (density < 0.0F) ? (half)(density * -strength) : lastDelta.density;
        return voxel;
    }
}
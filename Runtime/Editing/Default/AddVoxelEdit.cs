using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Windows;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<AddVoxelEdit>))]

// Will either add / remove matter from the terrain
public struct AddVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float strength;
    [ReadOnly] public float radius;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public JobHandle Apply(SparseVoxelDeltaData data, int3 position) {
        return IVoxelEdit.ApplyGeneric(position, data.densities, data.materials, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = new Vector3(radius, radius, radius) * 2.0F
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        Voxel voxel = Voxel.Empty;
        float density = math.length(position - center) - radius;
        voxel.density = (density < 0.0F) ? (half)(lastDelta.density + strength) : lastDelta.density;
        voxel.material = (density < 0.0F && writeMaterial) ? material : ushort.MaxValue;
        return voxel;
    }
}
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<AddVoxelEdit>))]

// Will either add / remove matter from the terrain
public struct AddVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float strength;
    [ReadOnly] public float radius;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public JobHandle Apply(SparseVoxelDeltaData data) {
        return IVoxelEdit.ApplyGeneric(data, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = new Vector3(radius, radius, radius) * 2.0F
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        Voxel voxel = lastDelta;
        float density = math.length(position - center) - radius;
        voxel.material = (density < 1.0F && writeMaterial && strength < 0) ? material : voxel.material;
        voxel.density = (density < 0.0F) ? (half)(lastDelta.density + strength) : lastDelta.density;
        return voxel;
    }
}
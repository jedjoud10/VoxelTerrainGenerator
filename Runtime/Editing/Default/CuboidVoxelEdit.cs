using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<CuboidVoxelEdit>))]
public struct CuboidVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public float strength;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;

    public JobHandle Apply(SparseVoxelDeltaData data) {
        return IVoxelEdit.ApplyGeneric(data, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = halfExtents
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        float3 q = math.abs(position - center) - halfExtents;
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        Voxel voxel = lastDelta;
        voxel.material = (density < 1.0F && writeMaterial) ? material : voxel.material;
        voxel.density = (density < 0.0F) ? (half)(strength) : lastDelta.density;
        return voxel;
    }
}
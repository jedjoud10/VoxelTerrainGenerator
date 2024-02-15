using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(VoxelEditJob<FlattenVoxelEdit>))]

// Flatten the terrain using the current normal and position
// Kinda like the flatten thing in astroneer
public struct FlattenVoxelEdit : IVoxelEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 normal;
    [ReadOnly] public float strength;
    [ReadOnly] public float radius;

    public JobHandle Apply(SparseVoxelDeltaData data) {
        return IVoxelEdit.ApplyGeneric(data, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = new Vector3(radius, radius, radius)
        };
    }

    public Voxel Modify(float3 position, Voxel lastDelta) {
        Voxel voxel = lastDelta;
        float density = math.length(position - center) - radius;
        float planeDensity = math.dot(normal, position - center);
        voxel.density = (density < 0.0F) ? (half)(lastDelta.density + strength * planeDensity) : lastDelta.density;
        
        
        return voxel;
    }
}
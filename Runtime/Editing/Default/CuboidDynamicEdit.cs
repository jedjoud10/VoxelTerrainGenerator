using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(DynamicEditJob<CuboidDynamicEdit>))]
public struct CuboidDynamicEdit : IDynamicEdit {
    [ReadOnly] public float3x3 rotation;
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public bool inverse;
    [ReadOnly] public byte material;
    [ReadOnly] public bool writeMaterial;
    public bool Enabled => true;

    public JobHandle Apply(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dep) {
        return IDynamicEdit.ApplyGeneric(chunk, ref voxels, dep, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = halfExtents * 2
        }.RotatedBy(rotation);
    }

    public Voxel Modify(float3 position, Voxel voxel) {
        position = math.mul(math.inverse(rotation), position - center);
        float3 q = math.abs(position) - halfExtents;
        float density = math.length(math.max(q, 0.0F)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0F);

        if (density < 1.0 && writeMaterial) {
            voxel.material = material;
        }

        if (inverse) {
            voxel.density = (half)(math.max(voxel.density, -density));
        } else {
            voxel.density = (half)math.min(voxel.density, density);
        }

        return voxel;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref center.x);
        serializer.SerializeValue(ref center.y);
        serializer.SerializeValue(ref center.z);
        serializer.SerializeValue(ref halfExtents.x);
        serializer.SerializeValue(ref halfExtents.y);
        serializer.SerializeValue(ref halfExtents.z);
        serializer.SerializeValue(ref material);
        serializer.SerializeValue(ref writeMaterial);
    }
}
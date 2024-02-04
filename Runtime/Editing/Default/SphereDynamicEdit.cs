using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(DynamicEditJob<SphereDynamicEdit>))]

public struct SphereDynamicEdit : IDynamicEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float radius;
    [ReadOnly] public byte material;
    [ReadOnly] public bool writeMaterial;
    public bool Enabled => true;

    public JobHandle Apply(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dep) {
        return IDynamicEdit.ApplyGeneric(chunk, ref voxels, dep, this);
    }

    public Bounds GetBounds() {
        return new Bounds {
            center = center,
            extents = new Vector3(radius, radius, radius) * 2.0F,
        };
    }

    public Voxel Modify(float3 position, Voxel voxel) {
        float density = math.length(position - center) - radius;

        if (density < 1.0 && writeMaterial) {
            voxel.material = material;
        }

        voxel.density = (half)math.min(voxel.density, density);
        return voxel;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref center.x);
        serializer.SerializeValue(ref center.y);
        serializer.SerializeValue(ref center.z);
        serializer.SerializeValue(ref radius);
        serializer.SerializeValue(ref material);
        serializer.SerializeValue(ref writeMaterial);
    }
}
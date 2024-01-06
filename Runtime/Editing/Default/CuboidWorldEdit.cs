using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(WorldEditJob<CuboidWorldEdit>))]
public struct CuboidWorldEdit : IWorldEdit {
    [ReadOnly] public float3 center;
    [ReadOnly] public float3 halfExtents;
    [ReadOnly] public ushort material;
    [ReadOnly] public bool writeMaterial;
    public bool Enabled => true;

    public JobHandle Apply(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dep) {
        return IWorldEdit.ApplyGeneric(chunk, ref voxels, dep, this);
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
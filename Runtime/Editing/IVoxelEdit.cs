using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Interface for voxel edits that has a unique job for creating the delta voxel diffs that we will serialize
public interface IVoxelEdit {
    // Create the delta voxel modifications (without having to read the inner voxel data)
    // The given input Voxel is the last delta value (for continuous edits)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel Modify(float3 position, Voxel lastDelta);

    // Get the AABB bounds of this voxel edit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bounds GetBounds();

    // MUST CALL THE "ApplyGeneric" function because we can't hide away generics
    public JobHandle Apply(SparseVoxelDeltaData data, int3 position);

    // Apply any generic voxel edit onto oncoming data
    internal static JobHandle ApplyGeneric<T>(int3 position, UnsafeList<half> densities, UnsafeList<ushort> materials, T edit) where T : struct, IVoxelEdit {
        RunLengthEncoder<T> job = new RunLengthEncoder<T> {
            chunkOffset = math.float3(position) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor,
            voxelScale = VoxelUtils.VoxelSizeFactor,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            edit = edit,
            densities = densities,
            materials = materials,
        };
        return job.Schedule(VoxelUtils.Volume, 2048);
    }
}

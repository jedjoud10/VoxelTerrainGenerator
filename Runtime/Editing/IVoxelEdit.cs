using System.Runtime.CompilerServices;
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
    public JobHandle Apply(SparseVoxelDeltaData data);

    // Apply any generic voxel edit onto oncoming data
    internal static JobHandle ApplyGeneric<T>(SparseVoxelDeltaData data, T edit) where T : struct, IVoxelEdit {
        VoxelEditJob<T> job = new VoxelEditJob<T> {
            chunkOffset = data.position,
            scalingFactor = data.scalingFactor,
            voxelScale = VoxelUtils.VoxelSizeFactor,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            edit = edit,
            densities = data.densities,
            materials = data.materials,
        };
        return job.Schedule(VoxelUtils.Volume, 2048);
    }
}

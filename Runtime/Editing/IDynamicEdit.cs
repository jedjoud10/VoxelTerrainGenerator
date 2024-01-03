using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Interface for dynamic edits that we can disable / toggle / move around
public interface IDynamicEdit {
    // Is the dynamic edit even enabled
    public bool Enabled { get; }

    // Create the delta voxel modifications (without having to read the inner voxel data)
    // The given input Voxel is the last delta value (for continuous edits)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel Modify(float3 position, Voxel voxel);

    // Get the AABB bounds of this voxel edit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bounds GetBounds();

    // MUST CALL THE "ApplyGeneric" function because we can't hide away generics
    public JobHandle Apply(VoxelChunk chunk);

    // Apply any generic dynamic edit onto oncoming data
    internal static JobHandle ApplyGeneric<T>(VoxelChunk chunk, T edit) where T: struct, IDynamicEdit {
        DynamicEditJob<T> job = new DynamicEditJob<T> {
            chunkOffset = math.float3(chunk.node.Position) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor,
            voxelScale = VoxelUtils.VoxelSizeFactor,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            dynamicEdit = edit,
            voxels = chunk.container.voxels,
        };
        return job.Schedule(VoxelUtils.Volume, 2048);
    }
}
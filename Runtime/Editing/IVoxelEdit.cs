using System.Collections;
using System.Collections.Generic;
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
}

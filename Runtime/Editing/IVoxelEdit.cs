using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Interface for voxel edits that has a unique job for modifying currently stored voxel data
public interface IVoxelEdit
{
    // Modify the given voxels
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel Modify(Voxel input, float3 position);

    // Get the center of the voxel edit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float3 GetWorldCenter();

    // Get the extent of the voxel edit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float3 GetWorldExtents();
}

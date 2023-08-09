using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Interface for voxel edits that has a unique job for modifying currently stored voxel data
public interface IVoxelEdit
{
    // Get the currently stored voxel edit job handles
    public List<JobHandle> GetJobHandles();

    // Begin the voxel edit job for the given voxel data 
    public void BeginEditJobs(VoxelChunk[] chunks);

    // Get the center of the voxel edit
    public Vector3 GetWorldCenter();

    // Get the extent of the voxel edit
    public Vector3 GetWorldExtents();

    // Check if a node is affected by this voxel edit
    public bool IntersectNode(OctreeNode node)
    {
        throw new System.NotImplementedException();
    }
}

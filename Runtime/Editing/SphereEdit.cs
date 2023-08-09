using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Sphere edit job that will create update the voxel field
struct SphereEditJob : IJobParallelFor
{
    public float3 center;
    public float radius;
    public OctreeNode node;
    public NativeArray<Voxel> voxels;

    public void Execute(int index)
    {
    }
}

// Simple sphere edit that edits the chunk in a specific radius
public struct SphereEdit : IVoxelEdit
{
    public Vector3 center;
    public float radius;
    List<JobHandle> handles;

    public SphereEdit(Vector3 center, float radius)
    {
        this.center = center;
        this.radius = radius;
        handles = new List<JobHandle>();
    }

    public void BeginEditJobs(VoxelChunk[] chunks)
    {
    }

    public List<JobHandle> GetJobHandles()
    {
        return handles;
    }

    public Vector3 GetWorldCenter()
    {
        return center;
    }

    public Vector3 GetWorldExtents()
    {
        return new Vector3(radius, radius, radius) * 2.0F; 
    }
}
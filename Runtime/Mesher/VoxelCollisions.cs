using System.Runtime.InteropServices;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;
using System.Threading;


// Responsible for creating and executing the mesh baking jobs
// Can also be used to check for collisions based on the stored voxel data (needed for props)
public class VoxelCollisions : VoxelBehaviour
{
    public bool generateCollisions = false;

    // Called when a chunk's mesh gets its collision data
    public delegate void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnCollisionBakingComplete onCollisionBakingComplete;

    // Used for collision
    private List<(JobHandle, VoxelChunk, VoxelMesh)> ongoingBakeJobs;

    // Checks if the voxel collision baker has completed all the baking work
    public bool Free
    {
        get
        {
            bool bakeJobs = ongoingBakeJobs.Count == 0;
            return bakeJobs;
        }
    }

    // Initialize the voxel mesher
    internal override void Init()
    {
        ongoingBakeJobs = new List<(JobHandle, VoxelChunk, VoxelMesh)>();
        terrain.VoxelMesher.onVoxelMeshingComplete += HandleVoxelMeshCollision;
    }

    private void HandleVoxelMeshCollision(VoxelChunk chunk, VoxelMesh voxelMesh)
    {
        if (voxelMesh.mesh.vertexCount > 0 && voxelMesh.mesh.triangles.Length > 0 && voxelMesh.computeCollisions)
        {
            BakeJob bakeJob = new BakeJob
            {
                meshId = voxelMesh.mesh.GetInstanceID(),
            };

            var handle = bakeJob.Schedule();
            ongoingBakeJobs.Add((handle, chunk, voxelMesh));
        }
        else
        {
            onCollisionBakingComplete?.Invoke(chunk, VoxelMesh.Empty);
        }
    }

    void Update()
    {
        foreach (var (handle, chunk, mesh) in ongoingBakeJobs)
        {
            if (handle.IsCompleted)
            {
                handle.Complete();
                onCollisionBakingComplete?.Invoke(chunk, mesh);
            }
        }
        ongoingBakeJobs.RemoveAll(item => item.Item1.IsCompleted);
    }

    internal override void Dispose()
    {
    }
}

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

// Responsible for creating and executing the mesh generation jobs
public class VoxelMesher : VoxelBehaviour
{
    // Number of simultaneous mesh generation tasks that happen during one frame
    [Range(1, 8)]
    public int meshJobsPerFrame = 1;

    public bool generateCollisions = false;
    public Material[] voxelMaterials;

    // List of persistently allocated mesh data
    internal List<MeshJobHandler> handlers;

    // Called when a chunk finishes generating its voxel data
    public delegate void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnVoxelMeshingComplete onVoxelMeshingComplete;

    // Called when a chunk's mesh gets its collision data
    public delegate void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnCollisionBakingComplete onCollisionBakingComplete;

    // Used for collision
    private List<(JobHandle, VoxelChunk, VoxelMesh)> ongoingBakeJobs;
    private int reservedEditingMeshJobs;
    Queue<(VoxelChunk, VoxelTempContainer, bool)> pendingMeshGenerationChunks;

    // Checks if the voxel mesher has completed all the work
    public bool Free
    {
        get
        {
            bool bakeJobs = ongoingBakeJobs.Count == 0;
            bool pending = pendingMeshGenerationChunks.Count == 0;
            bool handlersFree = handlers.All(x => x.Free);
            return bakeJobs && pending && handlersFree;
        }
    }

    // Initialize the voxel mesher
    internal override void Init()
    {
        reservedEditingMeshJobs = terrain.VoxelEdits.reservedMeshJobs;
        handlers = new List<MeshJobHandler>(meshJobsPerFrame + reservedEditingMeshJobs);
        pendingMeshGenerationChunks = new Queue<(VoxelChunk, VoxelTempContainer, bool)>();
        ongoingBakeJobs = new List<(JobHandle, VoxelChunk, VoxelMesh)>();

        for (int i = 0; i < meshJobsPerFrame + reservedEditingMeshJobs; i++)
        {
            handlers.Add(new MeshJobHandler());
        }
    }

    // Begin generating the mesh data using the given chunk and readback requeset
    public void GenerateMesh(VoxelChunk chunk, VoxelTempContainer container, bool computeCollisions)
    {
        pendingMeshGenerationChunks.Enqueue((chunk, container, computeCollisions && generateCollisions));
    }

    // Generate the mesh data immediately without putting the mesh through the queue
    public void GenerateMeshImmediate(VoxelChunk chunk, VoxelTempContainer container, JobHandle jobHandle)
    {

    }

    void Update()
    {
        // Complete the jobs that finished and create the meshes
        foreach(var handler in handlers) 
        {
            if (handler.quadJobHandle.IsCompleted && !handler.Free)
            {
                VoxelChunk chunk = handler.chunk;
                VoxelMesh voxelMesh = handler.Complete(voxelMaterials);
                onVoxelMeshingComplete?.Invoke(chunk, voxelMesh);
            
                if (handler.computeCollisions && voxelMesh.mesh.vertexCount > 0 && voxelMesh.mesh.triangles.Length > 0)
                {
                    BakeJob bakeJob = new BakeJob {
                        meshId = voxelMesh.mesh.GetInstanceID(),
                    };

                    var handle = bakeJob.Schedule();
                    ongoingBakeJobs.Add((handle, chunk, voxelMesh));
                }
            }
        }

        // Begin the jobs for the meshes
        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            (VoxelChunk, VoxelTempContainer, bool) output = (null, null, false);
            if (pendingMeshGenerationChunks.TryDequeue(out output))
            {
                if (!handlers[i].Free) {
                    pendingMeshGenerationChunks.Enqueue(output);
                    continue;
                }

                MeshJobHandler handler = handlers[i];
                handler.chunk = output.Item1;
                handler.voxels = output.Item2;
                handler.computeCollisions = output.Item3;
                handler.BeginJob(new JobHandle());
            }
        }

        // Complete the baking jobs
        foreach (var (handle, chunk, mesh) in ongoingBakeJobs) 
        {
            if (handle.IsCompleted) {
                handle.Complete();
                onCollisionBakingComplete?.Invoke(chunk, mesh);
            }
        }
        ongoingBakeJobs.RemoveAll(item => item.Item1.IsCompleted);
    }

    internal override void Dispose()
    {
        foreach (MeshJobHandler handler in handlers) 
        {
            handler.Complete(voxelMaterials);
            handler.Dispose();
        }
    }
}

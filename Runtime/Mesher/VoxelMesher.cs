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

    public bool smoothing = true;
    public Material[] voxelMaterials;

    // List of persistently allocated mesh data
    internal List<MeshJobHandler> handlers;

    // Called when a chunk finishes generating its voxel data
    public delegate void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnVoxelMeshingComplete onVoxelMeshingComplete;

    // Used for collision
    Queue<PendingMeshJob> pendingMeshJobs;

    // Checks if the voxel mesher has completed all the work
    public bool Free
    {
        get
        {
            bool pending = pendingMeshJobs.Count == 0;
            bool handlersFree = handlers.All(x => x.Free);
            return pending && handlersFree;
        }
    }

    // Initialize the voxel mesher
    internal override void Init()
    {
        handlers = new List<MeshJobHandler>(meshJobsPerFrame);
        pendingMeshJobs = new Queue<PendingMeshJob>();

        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            handlers.Add(new MeshJobHandler());
        }
    }

    // Begin generating the mesh data using the given chunk and voxel container
    public void GenerateMesh(VoxelChunk chunk, VoxelTempContainer container, bool computeCollisions, JobHandle dependency = new JobHandle())
    {
        pendingMeshJobs.Enqueue(new PendingMeshJob
        {
            chunk = chunk,
            container = container,
            computeCollisions = computeCollisions,
            dependency = dependency,
        });
    }

    // Begin generating the mesh data immediately without putting the mesh through the queue
    // Might fail in case there aren't enough free handlers to handle the job
    public bool TryGenerateMeshImmediate(VoxelChunk chunk, VoxelTempContainer container, bool computeCollisions, JobHandle dependency = new JobHandle())
    {
        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            if (handlers[i].Free)
            {
                MeshJobHandler handler = handlers[i];
                handler.chunk = chunk;
                handler.voxels = container;
                handler.computeCollisions = computeCollisions;
                var job = handler.BeginJob(dependency, smoothing);

                VoxelMesh voxelMesh = handler.Complete(voxelMaterials);

                onVoxelMeshingComplete?.Invoke(chunk, voxelMesh);

                return true;
            }
        }

        return false;
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
            }
        }

        // Begin the jobs for the meshes
        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            PendingMeshJob output = PendingMeshJob.Empty;
            if (pendingMeshJobs.TryDequeue(out output))
            {
                if (!handlers[i].Free) {
                    pendingMeshJobs.Enqueue(output);
                    continue;
                }

                MeshJobHandler handler = handlers[i];
                handler.chunk = output.chunk;
                handler.voxels = output.container;
                handler.computeCollisions = output.computeCollisions;
                handler.BeginJob(output.dependency, smoothing);
            }
        }
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

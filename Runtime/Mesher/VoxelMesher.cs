using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using System.Linq;

// Responsible for creating and executing the mesh generation jobs
public class VoxelMesher : VoxelBehaviour {
    // Number of simultaneous mesh generation tasks that happen during one frame
    [Range(1, 8)]
    public int meshJobsPerFrame = 1;

    public float minSkirtDensityThreshold = -10.0F;

    public bool smoothing = true;
    public bool skirts = true;
    public Material[] voxelMaterials;

    // List of persistently allocated mesh data
    internal List<MeshJobHandler> handlers;

    // Called when a chunk finishes generating its voxel data
    public delegate void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnVoxelMeshingComplete onVoxelMeshingComplete;

    // Used for collision
    internal Queue<PendingMeshJob> pendingMeshJobs;
    internal HashSet<VoxelChunk> dedupe;

    // Checks if the voxel mesher has completed all the work
    public bool Free {
        get {
            bool pending = pendingMeshJobs.Count == 0;
            bool handlersFree = handlers.All(x => x.Free);
            return pending && handlersFree;
        }
    }

    // Initialize the voxel mesher
    internal override void Init() {
        handlers = new List<MeshJobHandler>(meshJobsPerFrame);
        pendingMeshJobs = new Queue<PendingMeshJob>();
        dedupe = new HashSet<VoxelChunk>();
        VoxelUtils.MinSkirtDensityThreshold = minSkirtDensityThreshold;
        VoxelUtils.Smoothing = smoothing;
        VoxelUtils.Skirts = skirts;

        for (int i = 0; i < meshJobsPerFrame; i++) {
            handlers.Add(new MeshJobHandler());
        }
    }

    // Begin generating the mesh data using the given chunk and voxel container
    // Automatically creates a dependency from the editing system if it is editing modified chunks
    public void GenerateMesh(VoxelChunk chunk, bool computeCollisions) {
        if (chunk.container == null || dedupe.Contains(chunk)) {
            return;
        }

        //dedupe.Add(chunk);
        pendingMeshJobs.Enqueue(new PendingMeshJob {
            chunk = chunk,
            computeCollisions = computeCollisions,
        });
    }

    // Begin generating the mesh data immediately without putting the mesh through the queue
    // Might fail in case there aren't enough free handlers to handle the job
    /*
    public bool TryGenerateMeshImmediate(VoxelChunk voxelChunk, bool computeCollisions) {
        for (int i = 0; i < meshJobsPerFrame; i++) {
            if (handlers[i].Free) {
                MeshJobHandler handler = handlers[i];
                handler.chunk = voxelChunk;
                handler.computeCollisions = computeCollisions;
                var job = handler.BeginJob(new JobHandle(), voxelChunk.node);

                VoxelMesh voxelMesh = handler.Complete(voxelChunk.sharedMesh, voxelMaterials);
                onVoxelMeshingComplete?.Invoke(voxelChunk, voxelMesh);
                return true;
            }
        }

        return false;
    }
    */

    void Update() {
        // Complete the jobs that finished and create the meshes
        foreach (var handler in handlers) {
            if (handler.finalJobHandle.IsCompleted && !handler.Free) {
                VoxelChunk voxelChunk = handler.chunk;
                var stats = handler.Complete(voxelChunk.sharedMesh, voxelMaterials);
                onVoxelMeshingComplete?.Invoke(voxelChunk, stats);
            }
        }

        // Begin the jobs for the meshes
        for (int i = 0; i < meshJobsPerFrame; i++) {
            PendingMeshJob output = PendingMeshJob.Empty;
            if (pendingMeshJobs.TryDequeue(out output)) {
                if (!handlers[i].Free) {
                    pendingMeshJobs.Enqueue(output);
                    continue;
                }

                // Create a mesh job handler for handling the meshing job
                MeshJobHandler handler = handlers[i];
                handler.chunk = output.chunk;
                handler.voxels.CopyFrom(output.chunk.container.voxels);
                handler.computeCollisions = output.computeCollisions;

                // Pass through the edit system for any chunks that should be modifiable
                JobHandle dynamicEdit = terrain.VoxelEdits.TryGetApplyDynamicEditJobDependency(output.chunk, ref handler.voxels);
                JobHandle voxelEdit = terrain.VoxelEdits.TryGetApplyVoxelEditJobDependency(output.chunk, ref handler.voxels, dynamicEdit);
                handler.BeginJob(voxelEdit, output.chunk.node);
                dedupe.Remove(output.chunk);
            }
        }
    }

    internal override void Dispose() {
        foreach (MeshJobHandler handler in handlers) {
            handler.Complete(new Mesh(), voxelMaterials);
            handler.Dispose();
        }
    }
}

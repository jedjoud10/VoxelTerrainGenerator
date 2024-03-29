using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using System.Linq;
using Unity.Mathematics;

// Responsible for creating and executing the mesh generation jobs
public class VoxelMesher : VoxelBehaviour {
    // Number of simultaneous mesh generation tasks that happen during one frame
    [Range(1, 8)]
    public int meshJobsPerFrame = 1;

    public float minSkirtDensityThreshold = -10.0F;

    [Header("Mesh Mode & Settings")]
    public bool smoothing = true;
    public bool skirts = true;
    public bool perVertexNormals = true;
    public bool perVertexUvs = true;

    [Header("Mesh Ambient Occlusion")]
    public float ambientOcclusionOffset = 0.4f;
    public float ambientOcclusionPower = 2f;
    public float ambientOcclusionSpread = 0.4f;
    public float ambientOcclusionGlobalOffset = 0f;

    [Header("Mesh Materials")]
    public Material[] voxelMaterials;

    // List of persistently allocated mesh data
    internal List<MeshJobHandler> handlers;

    // Called when a chunk finishes generating its voxel data
    public delegate void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh);
    public event OnVoxelMeshingComplete onVoxelMeshingComplete;

    // Used for collision
    internal Queue<PendingMeshJob> pendingMeshJobs;

    // Checks if the voxel mesher has completed all the work
    public bool Free {
        get {
            bool pending = pendingMeshJobs.Count == 0;
            bool handlersFree = handlers.All(x => x.Free);
            return pending && handlersFree;
        }
    }

    private void UpdateParams() {
        VoxelUtils.MinSkirtDensityThreshold = minSkirtDensityThreshold;
        VoxelUtils.Smoothing = smoothing;
        VoxelUtils.PerVertexUvs = perVertexUvs;
        VoxelUtils.PerVertexNormals = perVertexNormals;
        VoxelUtils.Skirts = skirts;
        VoxelUtils.AmbientOcclusionOffset = ambientOcclusionOffset;
        VoxelUtils.AmbientOcclusionPower = ambientOcclusionPower;
        VoxelUtils.AmbientOcclusionSpread = ambientOcclusionSpread;
        VoxelUtils.AmbientOcclusionGlobalOffset = ambientOcclusionGlobalOffset;
    }

    private void OnValidate() {
        UpdateParams();
    }

    // Initialize the voxel mesher
    internal override void Init() {
        handlers = new List<MeshJobHandler>(meshJobsPerFrame);
        pendingMeshJobs = new Queue<PendingMeshJob>();
        UpdateParams();

        for (int i = 0; i < meshJobsPerFrame; i++) {
            handlers.Add(new MeshJobHandler());
        }
    }

    // Begin generating the mesh data using the given chunk and voxel container
    // Automatically creates a dependency from the editing system if it is editing modified chunks
    public void GenerateMesh(VoxelChunk chunk, bool computeCollisions) {
        if (chunk.container == null)
            return;

        var job = new PendingMeshJob {
            chunk = chunk,
            computeCollisions = computeCollisions,
        };

        if (pendingMeshJobs.Contains(job))
            return;

        pendingMeshJobs.Enqueue(job);
    }

    void Update() {
        // Complete the jobs that finished and create the meshes
        foreach (var handler in handlers) {
            if (handler.finalJobHandle.IsCompleted && !handler.Free) {
                VoxelChunk voxelChunk = handler.chunk;
                var stats = handler.Complete(voxelChunk.sharedMesh, voxelMaterials);

                if (voxelChunk.voxelCountersHandle != null)
                    terrain.VoxelEdits.UpdateCounters(handler, voxelChunk);

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
                handler.voxelCounter.Count = 0;
                JobHandle dynamicEdit = terrain.VoxelEdits.TryGetApplyDynamicEditJobDependency(output.chunk, ref handler.voxels);
                JobHandle voxelEdit = terrain.VoxelEdits.TryGetApplyVoxelEditJobDependency(output.chunk, ref handler.voxels, handler.voxelCounter, dynamicEdit);
                handler.BeginJob(voxelEdit, output.chunk.node);
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

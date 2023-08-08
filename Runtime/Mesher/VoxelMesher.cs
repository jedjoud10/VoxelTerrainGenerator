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


// Contains the allocation data for a single job
// There are multiple instances of this class stored inside the voxel mesher to saturate the other threads
class MeshJobHandler {
    public NativeArray<int> indices;
    public NativeArray<float3> vertices;
    public NativeArray<float4> uvs;
    public NativeCounter counter;
    public NativeCounter counterQuad;
    public NativeArray<int> triangles;
    public JobHandle vertexJobHandle;
    public JobHandle quadJobHandle;
    public VoxelReadbackRequest voxels;
    public VoxelChunk chunk;
    public bool computeCollisions = false;

    public MeshJobHandler()
    {
        indices = new NativeArray<int>(VoxelUtils.Total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        vertices = new NativeArray<float3>(VoxelUtils.Total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        uvs = new NativeArray<float4>(VoxelUtils.Total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        counter = new NativeCounter(Allocator.Persistent);
        counterQuad = new NativeCounter(Allocator.Persistent);
        triangles = new NativeArray<int>(VoxelUtils.Total * 6 * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    public bool Free { get; private set; } = true;

    // Begin the vertex + quad job that will generate the mesh
    public void BeginJob() {
        counter.Count = 0;
        counterQuad.Count = 0;
        Free = false;

        VertexJob vertexJob = new VertexJob {
            voxelized = voxels.voxelized,
            indices = indices,
            vertices = vertices,
            uvs = uvs,
            counter = counter,
            voxelScale = VoxelUtils.VoxelSize,
            vertexScale = VoxelUtils.VertexScaling,
            size = VoxelUtils.Size,
        };

        QuadJob quadJob = new QuadJob {
            voxelized = voxels.voxelized,
            vertexIndices = indices,
            counter = counterQuad,
            triangles = triangles,
            size = VoxelUtils.Size,
        };

        JobHandle vertexJobHandle = vertexJob.Schedule(VoxelUtils.Total, 512);
        JobHandle quadJobHandle = quadJob.Schedule(VoxelUtils.Total, 512, vertexJobHandle);

        this.vertexJobHandle = vertexJobHandle;
        this.quadJobHandle = quadJobHandle;     
    }

    // Complete the jobs and return a mesh
    public Mesh Complete() {
        quadJobHandle.Complete();
        Free = true;

        int maxVertices = counter.Count;
        int maxIndices = counterQuad.Count * 6;

        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices.Reinterpret<Vector3>(), 0, maxVertices);
        mesh.SetIndices(triangles, 0, maxIndices, MeshTopology.Triangles, 0);
        mesh.SetUVs(0, uvs.Reinterpret<Vector4>(), 0, maxVertices);
        voxels.Dispose();
        voxels = null;
        chunk = null;
        return mesh;
    }

    // Dispose of the underlying memory allocations
    public void Dispose() {
        indices.Dispose();
        vertices.Dispose();
        uvs.Dispose();
        counter.Dispose();
        counterQuad.Dispose();
        triangles.Dispose();
    }
}

// Responsible for creating and executing the mesh generation jobs
public class VoxelMesher : VoxelBehaviour
{
    // Number of simultaneous mesh generation tasks that happen during one frame
    [Range(1, 8)]
    public int meshJobsPerFrame = 1;

    // List of persistently allocated mesh data
    internal List<MeshJobHandler> handlers;

    // Called when a chunk finishes generating its voxel data
    public delegate void OnVoxelMeshingComplete(VoxelChunk chunk, Mesh mesh);
    public event OnVoxelMeshingComplete onVoxelMeshingComplete;

    // Called when a chunk's mesh gets its collision data
    public delegate void OnCollisionBakingComplete(VoxelChunk chunk, Mesh mesh);
    public event OnCollisionBakingComplete onCollisionBakingComplete;

    // Used for collision
    private List<(JobHandle, VoxelChunk, Mesh)> ongoingBakeJobs;

    Queue<(VoxelChunk, VoxelReadbackRequest, bool)> pendingMeshGenerationChunks;

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

    /*
    // Get the number of mesh generation tasks remaining
    public int MeshGenerationTasksRemaining
    {
        get
        {
            if (pendingMeshGenerationChunks != null)
            {
                return pendingMeshGenerationChunks.Count;
            }
            else
            {
                return 0;
            }
        }
    }
    */

    // Get the number of collision baking tasks remaining
    public int CollisionBakingTasksRemaining
    {
        get
        {
            if (ongoingBakeJobs != null)
            {
                return ongoingBakeJobs.Count;
            }
            else
            {
                return 0;
            }
        }
    }



    // Initialize the voxel mesher
    internal override void Init()
    {
        handlers = new List<MeshJobHandler>(meshJobsPerFrame);
        pendingMeshGenerationChunks = new Queue<(VoxelChunk, VoxelReadbackRequest, bool)>();
        ongoingBakeJobs = new List<(JobHandle, VoxelChunk, Mesh)>();

        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            handlers.Add(new MeshJobHandler());
        }
    }

    // Begin generating the mesh data using the given chunk and readback requeset
    public void GenerateMesh(VoxelChunk chunk, VoxelReadbackRequest request, bool computeCollisions = false)
    {
        pendingMeshGenerationChunks.Enqueue((chunk, request, computeCollisions));
    }

    void Update()
    {
        // Complete the jobs that finished and create the meshes
        foreach(var handler in handlers) 
        {
            if (handler.quadJobHandle.IsCompleted && !handler.Free)
            {
                VoxelChunk chunk = handler.chunk;
                Mesh mesh = handler.Complete();
                onVoxelMeshingComplete?.Invoke(chunk, mesh);
            
                if (handler.computeCollisions && mesh.vertexCount > 0 && mesh.triangles.Length > 0)
                {
                    BakeJob bakeJob = new BakeJob {
                        meshId = mesh.GetInstanceID(),
                    };

                    var handle = bakeJob.Schedule();
                    ongoingBakeJobs.Add((handle, chunk, mesh));
                }
            }
        }

        // Begin the jobs for the meshes
        for (int i = 0; i < meshJobsPerFrame; i++)
        {
            (VoxelChunk, VoxelReadbackRequest, bool) output = (null, null, false);
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
                handler.BeginJob();
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
            handler.Dispose();
        }
    }
}

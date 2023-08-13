using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Contains the allocation data for a single job
// There are multiple instances of this class stored inside the voxel mesher to saturate the other threads
internal class MeshJobHandler
{
    public NativeArray<int> indices;
    public NativeArray<float3> vertices;
    public NativeArray<byte> enabled;
    public NativeCounter counter;
    public NativeMultiCounter countersQuad;
    public NativeCounter materialCounter;
    public NativeArray<int> triangles;
    public JobHandle vertexJobHandle;
    public JobHandle quadJobHandle;
    public NativeParallelHashMap<ushort, int> materialHashMap;
    public NativeParallelHashSet<ushort> materialHashSet;
    public VoxelTempContainer voxels;
    public VoxelChunk chunk;
    public bool computeCollisions = false;

    internal MeshJobHandler()
    {
        indices = new NativeArray<int>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        vertices = new NativeArray<float3>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        enabled = new NativeArray<byte>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        counter = new NativeCounter(Allocator.Persistent);
        countersQuad = new NativeMultiCounter(256, Allocator.Persistent);
        triangles = new NativeArray<int>(VoxelUtils.Volume * 6 * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        materialHashMap = new NativeParallelHashMap<ushort, int>(256, Allocator.Persistent);
        materialHashSet = new NativeParallelHashSet<ushort>(256, Allocator.Persistent);
        materialCounter = new NativeCounter(Allocator.Persistent);
    }
    public bool Free { get; private set; } = true;

    // Begin the vertex + quad job that will generate the mesh
    internal JobHandle BeginJob(JobHandle dependency, bool smoothing)
    {
        countersQuad.Reset();
        counter.Count = 0;
        materialCounter.Count = 0;
        materialHashSet.Clear();
        materialHashMap.Clear();
        Free = false;

        // Calculates the number of materials within the mesh
        MaterialJob materialJob = new MaterialJob
        {
            voxels = voxels.voxels,
            materialHashSet = materialHashSet.AsParallelWriter(),
            materialHashMap = materialHashMap.AsParallelWriter(),
            materialCounter = materialCounter,
        };

        // Handles fetching MC corners for the SN edges
        CornerJob cornerJob = new CornerJob
        {
            voxels = voxels.voxels,
            enabled = enabled,
            size = VoxelUtils.Size,
        };

        // Generate the vertices of the mesh
        // Executed only onces, and shared by multiple submeshes
        VertexJob vertexJob = new VertexJob
        {
            enabled = enabled,
            voxels = voxels.voxels,
            indices = indices,
            vertices = vertices,
            counter = counter,
            voxelScale = VoxelUtils.VoxelSize,
            vertexScale = VoxelUtils.VertexScaling,
            size = VoxelUtils.Size,
            smoothing = smoothing,
        };

        // Generate the quads of the mesh
        // Executed for EACH material in the mesh
        QuadJob quadJob = new QuadJob
        {
            enabled = enabled,
            voxels = voxels.voxels,
            vertexIndices = indices,
            counters = countersQuad,
            triangles = triangles,
            materialHashMap = materialHashMap.AsReadOnly(),
            materialCounter = materialCounter,
            size = VoxelUtils.Size,
        };

        // Start the material + filter job
        JobHandle materialJobHandle = materialJob.Schedule(VoxelUtils.Volume, 2048, dependency);
        JobHandle cornerJobHandle = cornerJob.Schedule(VoxelUtils.Volume, 2048, dependency);

        // Start the vertex job
        JobHandle vertexDep = JobHandle.CombineDependencies(cornerJobHandle, dependency);
        JobHandle vertexJobHandle = vertexJob.Schedule(VoxelUtils.Volume, 2048, vertexDep);

        // Start the quad job
        JobHandle merged = JobHandle.CombineDependencies(materialJobHandle, vertexJobHandle, cornerJobHandle);
        JobHandle quadJobHandle = quadJob.Schedule(VoxelUtils.Volume, 2048, merged);

        this.vertexJobHandle = vertexJobHandle;
        this.quadJobHandle = quadJobHandle;
        return quadJobHandle;
    }

    // Complete the jobs and return a mesh
    internal VoxelMesh Complete(Material[] orderedMaterials)
    {
        if (voxels == null || chunk == null)
        {
            return VoxelMesh.Empty;
        }

        quadJobHandle.Complete();
        Free = true;

        int maxVertices = counter.Count;

        Mesh mesh = new Mesh();
        mesh.subMeshCount = materialCounter.Count;
        mesh.SetVertices(vertices.Reinterpret<Vector3>(), 0, maxVertices);

        Material[] materials = new Material[materialCounter.Count];

        foreach (var item in materialHashMap)
        {
            materials[item.Value] = orderedMaterials[item.Key];
        }

        for (int i = 0; i < materialCounter.Count; i++)
        {
            int countIndices = countersQuad[i] * 6;
            int segmentOffset = (triangles.Length / materialCounter.Count) * i;
            mesh.SetIndices(triangles, segmentOffset, countIndices, MeshTopology.Triangles, i);
        }

        voxels.TempDispose();
        voxels = null;
        chunk = null;
        return new VoxelMesh
        {
            mesh = mesh,
            materials = materials,
        };
    }

    // Dispose of the underlying memory allocations
    internal void Dispose()
    {
        indices.Dispose();
        vertices.Dispose();
        counter.Dispose();
        countersQuad.Dispose();
        triangles.Dispose();
        materialCounter.Dispose();
        materialHashMap.Dispose();
        materialHashSet.Dispose();
        enabled.Dispose();
    }
}
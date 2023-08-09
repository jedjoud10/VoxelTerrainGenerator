using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Contains the allocation data for a single job
// There are multiple instances of this class stored inside the voxel mesher to saturate the other threads
class MeshJobHandler {
    public NativeArray<int> indices;
    public NativeArray<float3> vertices;
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

    public MeshJobHandler()
    {
        indices = new NativeArray<int>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        vertices = new NativeArray<float3>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        counter = new NativeCounter(Allocator.Persistent);
        countersQuad = new NativeMultiCounter(256, Allocator.Persistent);
        triangles = new NativeArray<int>(VoxelUtils.Volume * 6 * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        materialHashMap = new NativeParallelHashMap<ushort, int>(256, Allocator.Persistent);
        materialHashSet = new NativeParallelHashSet<ushort>(256, Allocator.Persistent);
        materialCounter = new NativeCounter(Allocator.Persistent);
    }

    public bool Free { get; private set; } = true;

    // Begin the vertex + quad job that will generate the mesh
    public void BeginJob() {
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

        // Generate the vertices of the mesh
        // Executed only onces, and shared by multiple submeshes
        VertexJob vertexJob = new VertexJob {
            voxels = voxels.voxels,
            indices = indices,
            vertices = vertices,
            counter = counter,
            voxelScale = VoxelUtils.VoxelSize,
            vertexScale = VoxelUtils.VertexScaling,
            size = VoxelUtils.Size,
        };

        // Generate the quads of the mesh
        // Executed for EACH material in the mesh
        QuadJob quadJob = new QuadJob {
            voxels = voxels.voxels,
            vertexIndices = indices,
            counters = countersQuad,
            triangles = triangles,
            materialHashMap = materialHashMap.AsReadOnly(),
            materialCounter = materialCounter,
            size = VoxelUtils.Size,
        };

        JobHandle materialJobHandle = materialJob.Schedule(VoxelUtils.Volume, 512);
        JobHandle vertexJobHandle = vertexJob.Schedule(VoxelUtils.Volume, 512);
        JobHandle merged = JobHandle.CombineDependencies(materialJobHandle, vertexJobHandle);
        JobHandle quadJobHandle = quadJob.Schedule(VoxelUtils.Volume, 512, merged);

        this.vertexJobHandle = vertexJobHandle;
        this.quadJobHandle = quadJobHandle;     
    }

    // Complete the jobs and return a mesh
    public VoxelMesh Complete(Material[] orderedMaterials) {
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
    public void Dispose() {
        indices.Dispose();
        vertices.Dispose();
        counter.Dispose();
        countersQuad.Dispose();
        triangles.Dispose();
        materialCounter.Dispose();
        materialHashMap.Dispose();
        materialHashSet.Dispose();
    }
}
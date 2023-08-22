using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Contains the allocation data for a single job
// There are multiple instances of this class stored inside the voxel mesher to saturate the other threads
internal class MeshJobHandler
{
    public NativeArray<int> indices;
    public NativeArray<float3> vertices;
    public NativeArray<byte> enabled;
    public NativeCounter counter;
    public NativeMultiCounter countersQuad;
    public NativeMultiCounter materialQuadCounter;
    public NativeArray<int> materialSegmentOffsets;
    public NativeCounter materialCounter;
    public NativeArray<int> triangles;
    public JobHandle finalJobHandle;
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
        countersQuad = new NativeMultiCounter(VoxelUtils.MAX_MATERIAL_COUNT, Allocator.Persistent);
        materialQuadCounter = new NativeMultiCounter(VoxelUtils.MAX_MATERIAL_COUNT, Allocator.Persistent);
        triangles = new NativeArray<int>(VoxelUtils.Volume * 6 * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        materialHashMap = new NativeParallelHashMap<ushort, int>(VoxelUtils.MAX_MATERIAL_COUNT, Allocator.Persistent);
        materialHashSet = new NativeParallelHashSet<ushort>(VoxelUtils.MAX_MATERIAL_COUNT, Allocator.Persistent);
        materialSegmentOffsets = new NativeArray<int>(VoxelUtils.MAX_MATERIAL_COUNT, Allocator.Persistent);
        materialCounter = new NativeCounter(Allocator.Persistent);
    }
    public bool Free { get; private set; } = true;

    // Begin the vertex + quad job that will generate the mesh
    internal JobHandle BeginJob(JobHandle dependency, OctreeNode node)
    {
        countersQuad.Reset();
        counter.Count = 0;
        materialCounter.Count = 0;
        materialHashSet.Clear();
        materialHashMap.Clear();
        Free = false;

        //bool3 skirtsBase = math.bool3((node.Skirts & 1) == 1, ((node.Skirts >> 1) & 1) == 1, ((node.Skirts >> 2) & 1) == 1) & VoxelUtils.Skirts;
        //bool3 skirtsEnd = math.bool3(((node.Skirts >> 3) & 1) == 1, ((node.Skirts >> 4) & 1) == 1, ((node.Skirts >> 5) & 1) == 1) & VoxelUtils.Skirts;
        bool3 skirtsBase = math.bool3(true, false, false);
        bool3 skirtsEnd = math.bool3(true, false, false);


        // Handles fetching MC corners for the SN edges
        CornerJob cornerJob = new CornerJob
        {
            voxels = voxels.voxels,
            enabled = enabled,
            size = VoxelUtils.Size,
        };

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
        VertexJob vertexJob = new VertexJob
        {
            enabled = enabled,
            voxels = voxels.voxels,
            indices = indices,
            vertices = vertices,
            counter = counter,
            voxelScale = VoxelUtils.VoxelSizeFactor,
            vertexScale = VoxelUtils.VertexScaling,
            size = VoxelUtils.Size,
            smoothing = VoxelUtils.Smoothing,
            skirtsBase = skirtsBase,
            skirtsEnd = skirtsEnd,
        };

        // Generate the quads of the mesh (handles materials internally)
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
            skirtsBase = skirtsBase,
            skirtsEnd = skirtsEnd,
            minSkirtDensityThreshold = VoxelUtils.MinSkirtDensityThreshold
        };

        // Create sum job to calculate offsets for each material type 
        SumJob sumJob = new SumJob
        {
            materialCounter = materialCounter,
            materialSegmentOffsets = materialSegmentOffsets,
            materialQuadCounter = materialQuadCounter
        };

        // Create a copy job that will copy temp memory to perm memory
        CopyJob copyJob = new CopyJob
        {

        };

        // Start the corner job
        JobHandle cornerJobHandle = cornerJob.Schedule(VoxelUtils.Volume, 2048, dependency);

        // Start the material job
        JobHandle materialJobHandle = materialJob.Schedule(VoxelUtils.Volume, 2048, dependency);

        // Start the vertex job
        JobHandle vertexDep = JobHandle.CombineDependencies(cornerJobHandle, dependency);
        JobHandle vertexJobHandle = vertexJob.Schedule(VoxelUtils.Volume, 2048, vertexDep);

        // Start the quad job
        JobHandle merged = JobHandle.CombineDependencies(materialJobHandle, vertexJobHandle, cornerJobHandle);
        JobHandle quadJobHandle = quadJob.Schedule(VoxelUtils.Volume, 2048, merged);

        // Start the sum job 
        JobHandle sumJobHandle = sumJob.Schedule(VoxelUtils.MAX_MATERIAL_COUNT, 32, quadJobHandle);

        // Start the copy job
        JobHandle copyJobHandle = copyJob.Schedule(triangles.Length, 2048, sumJobHandle);

        finalJobHandle = copyJobHandle;
        return finalJobHandle;
    }

    // Complete the jobs and return a mesh
    internal VoxelMesh Complete(Material[] orderedMaterials)
    {
        if (voxels == null || chunk == null)
        {
            return VoxelMesh.Empty;
        }

        finalJobHandle.Complete();
        Free = true;

        /*
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
        */

        int maxVertices = counter.Count;

        int maxIndices = 0;

        for (int i = 0; i < materialCounter.Count; i++)
        {
            maxIndices += countersQuad[i] * 6;
        }

        Mesh mesh = new Mesh();
        float max = VoxelUtils.VoxelSizeFactor * VoxelUtils.Size;
        mesh.bounds = new Bounds
        {
            min = Vector3.zero,
            max = new Vector3(max, max, max)
        };

        // TODO: Pool these mesh objects to reduce garbage collection memory
        mesh.SetVertexBufferParams(maxVertices, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
        mesh.SetVertexBufferData(vertices.Reinterpret<Vector3>(), 0, 0, maxVertices);
        mesh.SetIndexBufferParams(maxIndices, IndexFormat.UInt32);
        mesh.SetIndexBufferData(triangles, 0, 0, maxIndices);
        mesh.subMeshCount = materialCounter.Count;

        Material[] materials = new Material[materialCounter.Count];

        // Convert material index to material *count* index
        foreach (var item in materialHashMap)
        {
            materials[item.Value] = orderedMaterials[item.Key];
        }

        // Set the indices of the multiple submeshes
        for (int i = 0; i < materialCounter.Count; i++)
        {
            int countIndices = countersQuad[i] * 6;
            int segmentOffset = (triangles.Length / materialCounter.Count) * i;
            // triangles, segmentOffset, countIndices, MeshTopology.Triangles, i

            mesh.SetSubMesh(i, new SubMeshDescriptor
            {
                indexStart = segmentOffset,
                indexCount = countIndices,
                topology = MeshTopology.Triangles,
            });
        }

        voxels.TempDispose();
        voxels = null;
        chunk = null;
        return new VoxelMesh
        {
            Mesh = mesh,
            Materials = materials,
            ComputeCollisions = computeCollisions,
            VertexCount = maxVertices,
            TriangleCount = maxIndices / 2,
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
        materialSegmentOffsets.Dispose();
        materialQuadCounter.Dispose();
        enabled.Dispose();
    }
}
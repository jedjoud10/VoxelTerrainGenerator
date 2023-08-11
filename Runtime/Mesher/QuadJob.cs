using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;


// Surface mesh job that will generate the isosurface quads, and thus, the triangles
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
public struct QuadJob : IJobParallelFor
{
    // Voxel native array
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    // Contains 3D data of the indices of the vertices
    [ReadOnly]
    public NativeArray<int> vertexIndices;

    // Triangles that we generated
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<int> triangles;

    // Forward direction of each quad
    [ReadOnly]
    static readonly uint3[] quadForwardDirection = new uint3[3] {
        new uint3(1, 0, 0),
        new uint3(0, 1, 0),
        new uint3(0, 0, 1),
    };

    // Quad vertices offsets based on direction
    [ReadOnly]
    static readonly uint3[] quadPerpendicularOffsets = new uint3[12] {
        new uint3(0, 0, 0),
        new uint3(0, 1, 0),
        new uint3(0, 1, 1),
        new uint3(0, 0, 1),

        new uint3(0, 0, 0),
        new uint3(0, 0, 1),
        new uint3(1, 0, 1),
        new uint3(1, 0, 0),

        new uint3(0, 0, 0),
        new uint3(1, 0, 0),
        new uint3(1, 1, 0),
        new uint3(0, 1, 0)
    };

    // Used for fast traversal
    [ReadOnly]
    public NativeArray<byte> enabled;

    // Quad Counter for each material
    [WriteOnly]
    public NativeMultiCounter.Concurrent counters;

    // Material counter to keep track of divido
    [ReadOnly]
    public NativeCounter materialCounter;

    // HashMap that converts the material index to submesh index
    [ReadOnly]
    public NativeParallelHashMap<ushort, int>.ReadOnly materialHashMap;

    // Static settings
    public int size;

    // Check and edge and check if we must generate a quad in it's forward facing direction
    void CheckEdge(uint3 basePosition, int index)
    {
        uint3 forward = quadForwardDirection[index];
        int baseIndex = VoxelUtils.PosToIndex(basePosition);
        int endIndex = VoxelUtils.PosToIndex(basePosition + forward);

        Voxel endVoxel = voxels[endIndex];
        Voxel startVoxel = voxels[baseIndex];

        if (endVoxel.density < 0.0 ^ startVoxel.density < 0.0)
        {
            bool flip = (endVoxel.density > 0.0);
            ushort material = flip ? startVoxel.material : endVoxel.material;

            // Fetch the indices of the vertex positions
            int index0 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4]);
            int index1 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 1]);
            int index2 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 2]);
            int index3 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 3]);

            // Fetch the actual indices of the vertices
            int vertex0 = vertexIndices[index0];
            int vertex1 = vertexIndices[index1];
            int vertex2 = vertexIndices[index2];
            int vertex3 = vertexIndices[index3];

            // Get the triangle index base
            int packedMaterialIndex = materialHashMap[material];
            int segmentOffset = triangles.Length / materialCounter.Count;
            int triIndex = counters.Increment(packedMaterialIndex) * 6;
            triIndex += segmentOffset * packedMaterialIndex;

            // Set the first tri
            triangles[triIndex + (flip ? 0 : 2)] = vertex0;
            triangles[triIndex + 1] = vertex1;
            triangles[triIndex + (flip ? 2 : 0)] = vertex2;

            // Set the second tri
            triangles[triIndex + (flip ? 3 : 5)] = vertex2;
            triangles[triIndex + 4] = vertex3;
            triangles[triIndex + (flip ? 5 : 3)] = vertex0;
        }
    }

    // Excuted for each cell within the grid
    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index);

        if (math.any((position > new uint3(size-2))) || math.any((position == new uint3(0))))
            return;

        ushort enabledEdges = VoxelUtils.EdgeMasks[enabled[index]];

        // 0
        if ((enabledEdges & 1) == 1)
            CheckEdge(position, 0);

        // 3
        if (((enabledEdges >> 3) & 1) == 1) 
            CheckEdge(position, 1);

        // 8
        if (((enabledEdges >> 8) & 1) == 1)
            CheckEdge(position, 2);
    }
}
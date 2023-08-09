using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Surface mesh job that will generate the isosurface mesh vertices
[BurstCompile(CompileSynchronously = true)]
public struct VertexJob : IJobParallelFor
{
    // Positions of the first vertex in edges
    [ReadOnly]
    static readonly uint3[] edgePositions0 = new uint3[12] {
        new uint3(0, 1, 0),
        new uint3(1, 1, 0),
        new uint3(1, 0, 0),
        new uint3(1, 0, 0),
        new uint3(0, 1, 1),
        new uint3(1, 1, 1),
        new uint3(1, 0, 1),
        new uint3(0, 0, 1),
        new uint3(0, 0, 1),
        new uint3(0, 1, 1),
        new uint3(1, 1, 1),
        new uint3(1, 0, 1),
    };

    // Positions of the second vertex in edges
    [ReadOnly]
    static readonly uint3[] edgePositions1 = new uint3[12] {
        new uint3(0, 0, 0),
        new uint3(0, 1, 0),
        new uint3(1, 1, 0),
        new uint3(0, 0, 0),
        new uint3(0, 0, 1),
        new uint3(0, 1, 1),
        new uint3(1, 1, 1),
        new uint3(1, 0, 1),
        new uint3(0, 0, 0),
        new uint3(0, 1, 0),
        new uint3(1, 1, 0),
        new uint3(1, 0, 0),
    };

    // Voxel native array
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    // Contains 3D data of the indices of the vertices
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<int> indices;

    // Vertices that we generated
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float3> vertices;

    // Vertex Counter
    public NativeCounter.Concurrent counter;

    // Static settings
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScale;
    [ReadOnly] public float voxelScale;

    // Excuted for each cell within the grid
    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index, size);

        if (math.any(position > math.uint3(size - 2)))
        {
            return;
        }

        float3 vertex = float3.zero;
        uint count = 0;

        // Iterate over the edges in the cube
        for (int edge = 0; edge < 12; edge++)
        {
            uint3 startOffset = edgePositions0[edge];
            uint3 endOffset = edgePositions1[edge];

            int startIndex = VoxelUtils.PosToIndex(startOffset + position, size);
            int endIndex = VoxelUtils.PosToIndex(endOffset + position, size);

            // Get the Voxels of the edge
            Voxel startVoxel = voxels[startIndex];
            Voxel endVoxel = voxels[endIndex];

            // Create a vertex on the line of the edge
            if (((startVoxel.density > 0.0) ^ (endVoxel.density > 0.0)))
            {
                float value = math.unlerp(startVoxel.density, endVoxel.density, 0);
                vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
                count += 1;
            }
        }

        // Add the vertex into the native array if needed
        if (count > 0)
        {
            // Must be offset by vec3(1, 1, 1)
            int indexOffset = size * size + size + 1;

            int vertexIndex = counter.Increment();
            indices[index + indexOffset] = vertexIndex;

            // Output vertex in object space
            float3 outputVertex = (vertex / (float)count) + position;

            vertices[vertexIndex] = outputVertex * vertexScale * voxelScale;
        }
    }
}
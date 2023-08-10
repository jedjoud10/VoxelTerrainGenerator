using Codice.Client.BaseCommands;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Surface mesh job that will generate the isosurface mesh vertices
// Tables used are from here https://gist.github.com/dwilliamson/c041e3454a713e58baf6e4f8e5fffecd
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
public struct VertexJob : IJobParallelFor
{
    // Positions of the first vertex in edges
    [ReadOnly]
    static readonly uint3[] edgePositions0 = new uint3[] {
        new uint3(0, 0, 0),
        new uint3(1, 0, 0),
        new uint3(1, 1, 0),
        new uint3(0, 1, 0),
        new uint3(0, 0, 1),
        new uint3(1, 0, 1),
        new uint3(1, 1, 1),
        new uint3(0, 1, 1),
        new uint3(0, 0, 0),
        new uint3(1, 0, 0),
        new uint3(1, 1, 0),
        new uint3(0, 1, 0),
    };

    // Positions of the second vertex in edges
    [ReadOnly]
    static readonly uint3[] edgePositions1 = new uint3[] {
        new uint3(1, 0, 0),
        new uint3(1, 1, 0),
        new uint3(0, 1, 0),
        new uint3(0, 0, 0),
        new uint3(1, 0, 1),
        new uint3(1, 1, 1),
        new uint3(0, 1, 1),
        new uint3(0, 0, 1),
        new uint3(0, 0, 1),
        new uint3(1, 0, 1),
        new uint3(1, 1, 1),
        new uint3(0, 1, 1),
    };

    // Edge masks telling us what edges will be active
    [ReadOnly]
    static readonly ushort[] edgeMasks = new ushort[] {
        0x0, 0x109, 0x203, 0x30a, 0x80c, 0x905, 0xa0f, 0xb06,
        0x406, 0x50f, 0x605, 0x70c, 0xc0a, 0xd03, 0xe09, 0xf00,
        0x190, 0x99, 0x393, 0x29a, 0x99c, 0x895, 0xb9f, 0xa96,
        0x596, 0x49f, 0x795, 0x69c, 0xd9a, 0xc93, 0xf99, 0xe90,
        0x230, 0x339, 0x33, 0x13a, 0xa3c, 0xb35, 0x83f, 0x936,
        0x636, 0x73f, 0x435, 0x53c, 0xe3a, 0xf33, 0xc39, 0xd30,
        0x3a0, 0x2a9, 0x1a3, 0xaa, 0xbac, 0xaa5, 0x9af, 0x8a6,
        0x7a6, 0x6af, 0x5a5, 0x4ac, 0xfaa, 0xea3, 0xda9, 0xca0,
        0x8c0, 0x9c9, 0xac3, 0xbca, 0xcc, 0x1c5, 0x2cf, 0x3c6,
        0xcc6, 0xdcf, 0xec5, 0xfcc, 0x4ca, 0x5c3, 0x6c9, 0x7c0,
        0x950, 0x859, 0xb53, 0xa5a, 0x15c, 0x55, 0x35f, 0x256,
        0xd56, 0xc5f, 0xf55, 0xe5c, 0x55a, 0x453, 0x759, 0x650,
        0xaf0, 0xbf9, 0x8f3, 0x9fa, 0x2fc, 0x3f5, 0xff, 0x1f6,
        0xef6, 0xfff, 0xcf5, 0xdfc, 0x6fa, 0x7f3, 0x4f9, 0x5f0,
        0xb60, 0xa69, 0x963, 0x86a, 0x36c, 0x265, 0x16f, 0x66,
        0xf66, 0xe6f, 0xd65, 0xc6c, 0x76a, 0x663, 0x569, 0x460,
        0x460, 0x569, 0x663, 0x76a, 0xc6c, 0xd65, 0xe6f, 0xf66,
        0x66, 0x16f, 0x265, 0x36c, 0x86a, 0x963, 0xa69, 0xb60,
        0x5f0, 0x4f9, 0x7f3, 0x6fa, 0xdfc, 0xcf5, 0xfff, 0xef6,
        0x1f6, 0xff, 0x3f5, 0x2fc, 0x9fa, 0x8f3, 0xbf9, 0xaf0,
        0x650, 0x759, 0x453, 0x55a, 0xe5c, 0xf55, 0xc5f, 0xd56,
        0x256, 0x35f, 0x55, 0x15c, 0xa5a, 0xb53, 0x859, 0x950,
        0x7c0, 0x6c9, 0x5c3, 0x4ca, 0xfcc, 0xec5, 0xdcf, 0xcc6,
        0x3c6, 0x2cf, 0x1c5, 0xcc, 0xbca, 0xac3, 0x9c9, 0x8c0,
        0xca0, 0xda9, 0xea3, 0xfaa, 0x4ac, 0x5a5, 0x6af, 0x7a6,
        0x8a6, 0x9af, 0xaa5, 0xbac, 0xaa, 0x1a3, 0x2a9, 0x3a0,
        0xd30, 0xc39, 0xf33, 0xe3a, 0x53c, 0x435, 0x73f, 0x636,
        0x936, 0x83f, 0xb35, 0xa3c, 0x13a, 0x33, 0x339, 0x230,
        0xe90, 0xf99, 0xc93, 0xd9a, 0x69c, 0x795, 0x49f, 0x596,
        0xa96, 0xb9f, 0x895, 0x99c, 0x29a, 0x393, 0x99, 0x190,
        0xf00, 0xe09, 0xd03, 0xc0a, 0x70c, 0x605, 0x50f, 0x406,
        0xb06, 0xa0f, 0x905, 0x80c, 0x30a, 0x203, 0x109, 0x0,
    };

    // Voxel native array
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    // Used for fast traversal
    [ReadOnly]
    public NativeArray<byte> enabled;

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

        // Fetch the byte that contains the number of corners active
        uint enabledCorners = enabled[index];

        // Early check to quit if the cell if full / empty
        if (enabledCorners == 0 || enabledCorners == 255) return;

        // Doing some marching cube shit here
        ushort code = edgeMasks[enabledCorners];
        int count = 0;

        for (int edge = 0; edge < 12; edge++)
        {
            // Continue if the edge isn't inside
            if (((code >> edge) & 1) == 0) continue;

            uint3 startOffset = edgePositions0[edge];
            uint3 endOffset = edgePositions1[edge];

            int startIndex = VoxelUtils.PosToIndex(startOffset + position, size);
            int endIndex = VoxelUtils.PosToIndex(endOffset + position, size);

            // Get the Voxels of the edge
            Voxel startVoxel = voxels[startIndex];
            Voxel endVoxel = voxels[endIndex];

            // Create a vertex on the line of the edge
            float value = math.unlerp(startVoxel.density, endVoxel.density, 0);
            vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
            count += 1;
        }

        // Must be offset by vec3(1, 1, 1)
        int indexOffset = size * size + size + 1;

        int vertexIndex = counter.Increment();
        indices[VoxelUtils.PosToIndex(position + 1, size)] = vertexIndex;

        // Output vertex in object space
        float3 outputVertex = (vertex / (float)count) + position;
        vertices[vertexIndex] = outputVertex * vertexScale * voxelScale;
    }
}
using System.Buffers.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.UIElements;


// Surface mesh job that will generate the isosurface mesh vertices
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
public struct VertexJob : IJobParallelFor {
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

    // Voxel native array
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    // Used for fast traversal
    [ReadOnly]
    public NativeArray<byte> enabled;

    // Contains 3D data of the indices of the vertices
    [WriteOnly]
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
    [ReadOnly] public bool smoothing;
    [ReadOnly] public float chunkScale;
    [ReadOnly] public bool3 skirtsBase;
    [ReadOnly] public bool3 skirtsEnd;
    [ReadOnly] public float minSkirtDensityThreshold;

    // Excuted for each cell within the grid
    public void Execute(int index) {
        uint3 position = VoxelUtils.IndexToPos(index, 65);
        indices[index] = int.MaxValue;

        // Idk bruh
        float3 vertex = float3.zero;
        int count = 0;

        // Create the smoothed vertex
        // TODO: Test out QEF or other methods for smoothing here
        for (int edge = 0; edge < 12; edge++) {
            uint3 startOffset = edgePositions0[edge];
            uint3 endOffset = edgePositions1[edge];

            int startIndex = VoxelUtils.PosToIndex(startOffset + position, 66);
            int endIndex = VoxelUtils.PosToIndex(endOffset + position, 66);

            // Get the Voxels of the edge
            Voxel startVoxel = voxels[startIndex];
            Voxel endVoxel = voxels[endIndex];

            if (startVoxel.density <= 0.0 ^ endVoxel.density <= 0.0) {
                // Create a vertex on the line of the edge
                float value = math.unlerp(startVoxel.density, endVoxel.density, 0);
                vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
                count += 1;
            }
        }

        if (count == 0) {
            return;
        } 

        // Must be offset by vec3(1, 1, 1)
        int vertexIndex = counter.Increment();
        indices[index] = vertexIndex;

        // Output vertex in object space
        float3 offset = (vertex / count);
        float3 outputVertex = (offset) + position;
        vertices[vertexIndex] = outputVertex * voxelScale;
    }
}
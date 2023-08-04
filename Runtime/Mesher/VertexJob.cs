using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;


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

    // Voxelized readback data that we will generate
    [ReadOnly]
    public NativeArray<float> voxelized;

    // Contains 3D data of the indices of the vertices
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<int> indices;

    // Vertices that we generated
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float3> vertices;

    // Custom UV data to store material, color, and other stuff
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float4> uvs;

    // Counter
    public NativeCounter.Concurrent counter;

    // Excuted for each cell within the grid
    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index);

        if (position.x > 61 || position.y > 61 || position.z > 61) {
            return;
        }

        float3 vertex = float3.zero;
        uint count = 0;

        // Iterate over the edges in the cube
        for (int edge = 0; edge < 12; edge++) {
            uint3 startOffset = edgePositions0[edge];
            uint3 endOffset = edgePositions1[edge];

            int startIndex = VoxelUtils.PosToIndex(startOffset + position);
            int endIndex = VoxelUtils.PosToIndex(endOffset + position);

            // Get the densities of the edge
            float startDensity = voxelized[startIndex];
            float endDensity = voxelized[endIndex];

            // Create a vertex on the line of the edge
            if (((startDensity > 0.0) ^ (endDensity > 0.0))) {
                float value = math.unlerp(startDensity, endDensity, 0);
                vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
                count += 1;
            }
        }

        // Add the vertex into the native array if needed
        if (count > 0) {
            // Must be offset by vec3(1, 1, 1)
            const int INDEX_OFFSET = 64*64 + 64 + 1;
            
            int vertexIndex = counter.Increment();
            indices[index + INDEX_OFFSET] = vertexIndex;

            // Output vertex in object space
            float3 outputVertex = (vertex / (float)count) + position;

            // UVs contain the AO data and extra data
            float ao = VoxelUtils.CalculatePerVertexAmbientOcclusion(outputVertex, ref voxelized);
            float4 outputUVs = new float4(ao);

            vertices[vertexIndex] = outputVertex * VoxelUtils.VERTEX_SCALING * VoxelUtils.VOXEL_SIZE;
            uvs[vertexIndex] = outputUVs;
        }
    }
}
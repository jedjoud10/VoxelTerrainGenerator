using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;


// Surface mesh job that will generate the isosurface quads, and thus, the triangles
[BurstCompile(CompileSynchronously = true)]
public struct QuadJob : IJobParallelFor
{
    // Voxel densities as halfs
    [ReadOnly]
    public NativeArray<half> densities;

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

    // Quad Counter
    public NativeCounter.Concurrent counter;

    // Static settings
    public int size;
    
    // Check and edge and check if we must generate a quad in it's forward facing direction
    void CheckEdge(uint3 basePosition, int index)
    {
        uint3 forward = quadForwardDirection[index];

        int baseIndex = VoxelUtils.PosToIndex(basePosition, size);
        int endIndex = VoxelUtils.PosToIndex(basePosition + forward, size);
    
        if ((densities[baseIndex] > 0.0) ^ (densities[endIndex] > 0.0)) {
            bool flip = (densities[endIndex] > 0.0);

            int index0 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4], size);
            int index1 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 1], size);
            int index2 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 2], size);
            int index3 = VoxelUtils.PosToIndex(basePosition + forward + quadPerpendicularOffsets[index * 4 + 3], size);
            
            int vertex0 = vertexIndices[index0];
            int vertex1 = vertexIndices[index1];
            int vertex2 = vertexIndices[index2];
            int vertex3 = vertexIndices[index3];
        
            int test = counter.Increment() * 6;

            triangles[test + (flip ? 0 : 2)] = vertex0;
            triangles[test + 1] = vertex1;
            triangles[test + (flip ? 2 : 0)] = vertex2;

            triangles[test + (flip ? 3 : 5)] = vertex2;
            triangles[test + 4] = vertex3;
            triangles[test + (flip ? 5 : 3)] = vertex0;
        }
    }

    // Excuted for each cell within the grid
    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index, size);

        if (math.any((position > new uint3(size-2))) || math.any((position == new uint3(0)))) {
            return;
        }

        CheckEdge(position, 0);
        CheckEdge(position, 1);
        CheckEdge(position, 2);
    }
}
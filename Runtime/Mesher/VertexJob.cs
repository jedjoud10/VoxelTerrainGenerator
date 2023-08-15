using System.Buffers.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.UIElements;


// Surface mesh job that will generate the isosurface mesh vertices
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
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
    [ReadOnly] public bool3 skirtsBase;
    [ReadOnly] public bool3 skirtsEnd;

    // Excuted for each cell within the grid
    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index);
        indices[index] = int.MaxValue;

        // Idk bruh
        if (math.any(position > math.uint3(size - 2)))
            return;

        float3 vertex = float3.zero;

        // Check if we will use this vertex for skirting purposes
        bool3 base_ = (position == math.uint3(0)) & skirtsBase;
        bool3 end_ = (position == math.uint3(size - 2)) & skirtsEnd;
        
        // Love me some cute femboys in skirts >.<
        bool3 skirts = base_ | end_;

        // Fetch the byte that contains the number of corners active
        uint enabledCorners = enabled[index];

        // Early check to quit if the cell if full / empty
        if ((enabledCorners == 0 || enabledCorners == 255) && !math.any(skirts)) return;

        // Doing some marching cube shit here
        uint code = VoxelUtils.EdgeMasks[enabledCorners];
        int count = math.countbits(code);

        if (smoothing && !math.any(skirts))
        {
            for (int edge = 0; edge < 12; edge++)
            {
                // Continue if the edge isn't inside
                if (((code >> edge) & 1) == 0) continue;

                uint3 startOffset = edgePositions0[edge];
                uint3 endOffset = edgePositions1[edge];

                int startIndex = VoxelUtils.PosToIndex(startOffset + position);
                int endIndex = VoxelUtils.PosToIndex(endOffset + position);

                // Get the Voxels of the edge
                Voxel startVoxel = voxels[startIndex];
                Voxel endVoxel = voxels[endIndex];

                // Create a vertex on the line of the edge
                float value = math.unlerp(startVoxel.density, endVoxel.density, 0);
                vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
            }
        }
        else
        {
            count = 1;
        }

        // Must be offset by vec3(1, 1, 1)
        int vertexIndex = counter.Increment();
        indices[index] = vertexIndex;

        // Output vertex in object space
        float3 offset = (vertex / (float)count);

        // Handle constricting the vertices in the axii
        offset = math.select(offset, 0.0F, skirts);

        float3 outputVertex = offset + position;
        vertices[vertexIndex] = outputVertex * vertexScale * voxelScale;
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


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

    // Normals in case we have a shader that requires them
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float3> normals;

    // Extra data passed to the shader for per vertex ambient occlusion
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float2> uvs;

    // GYATT
    public bool perVertexNormals;
    public bool perVertexUvs;
    public float aoOffset;
    public float aoPower;
    public float aoSpread;
    public float aoGlobalOffset;

    // Vertex Counter
    public NativeCounter.Concurrent counter;

    // Static settings
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScale;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public bool smoothing;
    [ReadOnly] public bool3 skirtsBase;
    [ReadOnly] public bool3 skirtsEnd;
    [ReadOnly] public float minSkirtDensityThreshold;

    // Excuted for each cell within the grid
    public void Execute(int index) {
        uint3 position = VoxelUtils.IndexToPos(index);
        indices[index] = int.MaxValue;

        // Idk bruh
        if (math.any(position > math.uint3(size - 2)))
            return;

        float3 vertex = float3.zero;
        float3 normal = float3.zero;

        // Check if we will use this vertex for skirting purposes
        bool3 base_ = (position <= math.uint3(1)) & skirtsBase;
        bool3 end_ = (position == math.uint3(size - 2)) & skirtsEnd;

        // Love me some cute femboys in skirts >.<
        bool3 skirts = base_ | end_;

        // Fetch the byte that contains the number of corners active
        uint enabledCorners = enabled[index];
        bool empty = enabledCorners == 0 || enabledCorners == 255;

        // Early check to quit if the cell if full / empty
        if (empty && !math.any(skirts)) return;

        // Doing some marching cube shit here
        uint code = VoxelUtils.EdgeMasks[enabledCorners];
        int count = math.countbits(code);

        // Use linear interpolation when smoothing
        if (!empty) {
            if (smoothing) {
                // Create the smoothed vertex
                // TODO: Test out QEF or other methods for smoothing here
                for (int edge = 0; edge < 12; edge++) {
                    // Continue if the edge isn't inside
                    if (((code >> edge) & 1) == 0) continue;

                    uint3 startOffset = edgePositions0[edge];
                    uint3 endOffset = edgePositions1[edge];

                    int startIndex = VoxelUtils.PosToIndex(startOffset + position);
                    int endIndex = VoxelUtils.PosToIndex(endOffset + position);

                    float3 startNormal = VoxelUtils.SampleGridNormal(startOffset + position, ref voxels, size);
                    float3 endNormal = VoxelUtils.SampleGridNormal(endOffset + position, ref voxels, size);

                    // Get the Voxels of the edge
                    Voxel startVoxel = voxels[startIndex];
                    Voxel endVoxel = voxels[endIndex];

                    // Create a vertex on the line of the edge
                    float value = math.unlerp(startVoxel.density, endVoxel.density, 0);
                    vertex += math.lerp(startOffset, endOffset, value) - math.float3(0.5);
                    normal += math.lerp(startNormal, endNormal, value);
                }
            } else {
                // Don't do any smoothing
                count = 1;
                normal += VoxelUtils.SampleGridNormal(position, ref voxels, size);
            }
        }

        // Handle skirt vertex keko
        if (math.any(skirts) && empty) {
            if (voxels[index].density < 0.0 && voxels[index].density > minSkirtDensityThreshold) {
                count = 1;
            } else {
                return;
            }
        }

        // Must be offset by vec3(1, 1, 1)
        int vertexIndex = counter.Increment();
        indices[index] = vertexIndex;

        // Output vertex in object space
        float3 offset = (vertex / (float)count);

        // Handle constricting the vertices in the axii
        offset = math.select(offset, math.float3(0.0F), skirts);

        float3 outputVertex = (offset - 1.0F) + position;
        vertices[vertexIndex] = outputVertex * vertexScale * voxelScale;

        // Calculate per vertex normals and apply it
        normal = perVertexNormals ? -math.normalizesafe(normal, new float3(0, 1, 0)) : new float3(0, 1, 0);
        normals[vertexIndex] = normal;


        // Calculate per vertex ambient occlusion and apply it
        float ambientOcclusion = perVertexUvs ? VoxelUtils.CalculateVertexAmbientOcclusion(outputVertex, ref voxels, size, aoOffset, aoPower, aoSpread, aoGlobalOffset) : 1.0f;
        
        if (float.IsNaN(ambientOcclusion)) {
            ambientOcclusion = 1;
        }

        uvs[vertexIndex] = new float2(ambientOcclusion, 0.0f);
    }
}
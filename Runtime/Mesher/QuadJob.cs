using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Surface mesh job that will generate the isosurface quads, and thus, the triangles
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public struct QuadJob : IJobParallelFor {
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
    static readonly uint3[] quadForwardDirection = new uint3[3]
    {
        new uint3(1, 0, 0),
        new uint3(0, 1, 0),
        new uint3(0, 0, 1),
    };

    // Quad vertices offsets based on direction
    [ReadOnly]
    static readonly uint3[] quadPerpendicularOffsets = new uint3[12]
    {
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

    // Bit shift used to check for edges
    [ReadOnly]
    static readonly int[] shifts = new int[3]
    {
        0, 3, 8
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
    [ReadOnly] public int size;
    [ReadOnly] public bool3 skirtsBase;
    [ReadOnly] public bool3 skirtsEnd;

    // Check and edge and check if we must generate a quad in it's forward facing direction
    void CheckEdge(uint3 basePosition, int index, bool skirts, bool skirtsForceDir) {
        uint3 forward = quadForwardDirection[index];

        int baseIndex = VoxelUtils.PosToIndex(basePosition);
        int endIndex = VoxelUtils.PosToIndex(basePosition + forward);

        Voxel endVoxel = voxels[endIndex];
        Voxel startVoxel = voxels[baseIndex];

        bool flip = (endVoxel.density >= 0.0);

        // Force direction if we are skirting
        if (skirts)
            flip = skirtsForceDir;

        ushort material = flip ? startVoxel.material : endVoxel.material;
        uint3 offset = basePosition + forward - math.uint3(1);

        // Fetch the indices of the vertex positions
        int index0 = VoxelUtils.PosToIndex(offset + quadPerpendicularOffsets[index * 4]);
        int index1 = VoxelUtils.PosToIndex(offset + quadPerpendicularOffsets[index * 4 + 1]);
        int index2 = VoxelUtils.PosToIndex(offset + quadPerpendicularOffsets[index * 4 + 2]);
        int index3 = VoxelUtils.PosToIndex(offset + quadPerpendicularOffsets[index * 4 + 3]);

        // Fetch the actual indices of the vertices
        int vertex0 = vertexIndices[index0];
        int vertex1 = vertexIndices[index1];
        int vertex2 = vertexIndices[index2];
        int vertex3 = vertexIndices[index3];

        // Don't make a quad if the vertices are invalid
        if ((vertex0 | vertex1 | vertex2 | vertex3) == int.MaxValue)
            return;

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

    // Excuted for each cell within the grid
    public void Execute(int index) {
        uint3 position = VoxelUtils.IndexToPos(index);

        // Allows us to save two voxel fetches (very important)
        ushort enabledEdges = VoxelUtils.EdgeMasks[enabled[index]];

        // Used for skirts
        bool3 base_ = (position == math.uint3(1)) & skirtsBase;
        bool3 end_ = (position == math.uint3(size - 2)) & skirtsEnd;
        bool3 skirt = base_ | end_;


        bool3 valPos = (position < math.uint3(size - 1)) & (position > math.uint3(1));
        for (int i = 0; i < 3; i++) {
            // Handle creating the quad normally
            if (((enabledEdges >> shifts[i]) & 1) == 1 && math.all(valPos)) {
                CheckEdge(position, i, false, false);
            }

            // Mane idfk what I'm doing any more
            bool limiter = math.all(valPos | math.bool3(i == 0, i == 1, i == 2));

            // Handle creating the skirt 
            if (skirt[i] && limiter) {
                CheckEdge(position, i, true, end_[i]);
            }
        }
    }
}
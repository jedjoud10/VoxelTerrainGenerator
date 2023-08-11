using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Fiter job that will store the locations of completely empty / filled segments in the mesh to speed up meshing
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
public struct FilterJob : IJobParallelFor
{
    // List of enabled corners like in MC
    [WriteOnly]
    public NativeArray<byte> enabled;

    // Voxel native array
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    [ReadOnly]
    static readonly uint4x3[] offsets =
    {
        new uint4x3(
            new uint4(0, 1, 0, 1),
            new uint4(0, 0, 1, 1),
            new uint4(0, 0, 0, 0)
        ),

        new uint4x3(
            new uint4(0, 1, 0, 1),
            new uint4(0, 0, 1, 1),
            new uint4(1, 1, 1, 1)
        )
    };

    // Static settings
    [ReadOnly] public int size;

    public void Execute(int index)
    {
        uint3 position = VoxelUtils.IndexToPos(index);

        if (math.any(position > math.uint3(size - 2)))
            return;


        int4 indices = math.int4(Morton.EncodeMorton32(offsets[0].c0 + position.x, offsets[0].c1 + position.y, offsets[0].c2 + position.z));
        float4 test = math.float4(0.0F);
        test.x = voxels[indices.x].density;
        test.y = voxels[indices.y].density;
        test.z = voxels[indices.z].density;
        test.w = voxels[indices.w].density;

        int4 indices2 = math.int4(Morton.EncodeMorton32(offsets[1].c0 + position.x, offsets[1].c1 + position.y, offsets[1].c2 + position.z));
        float4 test2 = math.float4(0.0F);
        test2.x = voxels[indices2.x].density;
        test2.y = voxels[indices2.y].density;
        test2.z = voxels[indices2.z].density;
        test2.w = voxels[indices2.w].density;

        bool4 check1 = test < math.float4(0.0);
        bool4 check2 = test2 < math.float4(0.0);

        int value = math.bitmask(check1) | (math.bitmask(check2) << 4);

        enabled[index] = (byte)value;
    }
}
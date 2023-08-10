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

    /*
    // Offsets of the corners
    [ReadOnly]
    static readonly uint3[] offsets =
    {
        new uint3(0, 0, 0),
        new uint3(1, 0, 0),
        new uint3(0, 1, 0),
        new uint3(1, 1, 0),

        new uint3(0, 0, 1),
        new uint3(1, 0, 1),
        new uint3(0, 1, 1),
        new uint3(1, 1, 1),
    };
    */

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
        uint3 position = VoxelUtils.IndexToPos(index, size);

        if (math.any(position > math.uint3(size - 2)))
            return;


        int4 indices = math.int4(Morton.EncodeMorton32(offsets[0].c0, offsets[0].c1, offsets[0].c2));
        float4 test = math.float4(0.0F);

        test.x = voxels[indices.x].density;
        test.y = voxels[indices.y].density;
        test.z = voxels[indices.z].density;
        test.w = voxels[indices.w].density;

        int4 indices2 = math.int4(Morton.EncodeMorton32(offsets[1].c0, offsets[1].c1, offsets[1].c2));
        float4 test2 = math.float4(0.0F);

        test2.x = voxels[indices2.x].density;
        test2.y = voxels[indices2.y].density;
        test2.z = voxels[indices2.z].density;
        test2.w = voxels[indices2.w].density;

        int value = 0;

        for (int i = 0; i < 4; i++)
        {
            value |= ((test[i] < 0.0) ? 1 : 0) << i;
        }

        for (int i = 0; i < 4; i++)
        {
            value |= ((test2[i] < 0.0) ? 1 : 0) << (i + 4);
        }

        enabled[index] = (byte)value;
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Fiter job that will store the locations of completely empty / filled segments in the mesh to speed up meshing
[BurstCompile(CompileSynchronously = true)]
public struct FilterJob : IJobParallelFor
{
    // List of enabled corners like in MC
    [WriteOnly]
    public NativeArray<byte> enabled;

    // Voxel native array (x4 for bursting (ambatakum))
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    /*
    // Offsets of the corners
    [ReadOnly]
    static readonly int3x4[] offsets =
    {
        new int3x4(new int3(0, 0, 0), new int3(0, 0, 1), new int3(1, 0, 0), new int3(1, 0, 1)),
        new int3x4(new int3(0, 1, 0), new int3(0, 1, 1), new int3(1, 1, 0), new int3(1, 1, 1)),
    };
    */

    // Offsets of the corners
    [ReadOnly]
    static readonly int3[] offsets =
    {
        new int3(0, 0, 0), new int3(0, 0, 1), new int3(1, 0, 0), new int3(1, 0, 1),
        new int3(0, 1, 0), new int3(0, 1, 1), new int3(1, 1, 0), new int3(1, 1, 1),
    };

    public void Execute(int index)
    {
        enabled[index] = byte.MinValue;
    }
}
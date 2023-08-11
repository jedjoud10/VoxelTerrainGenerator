using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Fiter job that will store the locations of completely empty / filled segments in the mesh to speed up meshing
// Segment size are specified by the batch size. They should be powers of two to make us of z-curve cache locality
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
public struct CoarseFilterJob : IJobParallelFor
{
    [ReadOnly]
    public int segmentSize;

    [ReadOnly]
    public NativeArray<Voxel> voxels;

    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<bool> enabledSegments;

    public void Execute(int batch)
    {
        int internalPositive = 0;
        int internalNegative = 0;

        for (int i = 0; i < segmentSize; i++)
        {
            int index = batch * segmentSize + i;
            Voxel voxel = voxels[index];

            if (voxel.density > 0)
            {
                internalPositive += 1;
            } else
            {
                internalNegative += 1;
            }

            if (internalPositive > 0 && internalNegative > 0)
            {
                enabledSegments[batch] = true;
                break;
            }
        }
    }
}
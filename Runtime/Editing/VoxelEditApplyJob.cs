using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct VoxelEditApplyJob : IJobParallelFor {
    public NativeArray<Voxel> voxels;
    [ReadOnly] public SparseVoxelDeltaData data;

    public NativeCounter.Concurrent counter;

    public void Execute(int index) {
        half deltaDensity = data.densities[index];
        byte deltaMaterial = data.materials[index];
        Voxel cur = voxels[index];

        if (deltaMaterial != byte.MaxValue) {
            cur.material = deltaMaterial;
        }

        half oldDensity = cur.density;
        cur.density += VoxelUtils.NormalizeHalf(deltaDensity);
        half newDensity = cur.density;

        if (newDensity > 0.0f && oldDensity < 0.0f) {
            counter.Increment();
        } else if (newDensity < 0.0f && oldDensity > 0.0f) {
            counter.Decrement();
        }
        
        voxels[index] = cur;
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct VoxelEditApplyJob : IJobParallelFor {
    public NativeArray<Voxel> voxels;
    [ReadOnly] public SparseVoxelDeltaData data;

    public void Execute(int index) {
        half deltaDensity = data.densities[index];
        ushort deltaMaterial = data.materials[index];
        Voxel cur = voxels[index];

        if (deltaMaterial != ushort.MaxValue) {
            cur.material = deltaMaterial;
        }

        cur.density += VoxelUtils.NormalizeHalf(deltaDensity);
        voxels[index] = cur;
    }
}
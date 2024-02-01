using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;


// Material job that will count the number of unique materials for the mesh
[BurstCompile(CompileSynchronously = true)]
public struct MaterialJob : IJobParallelFor {
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    public NativeParallelHashSet<ushort>.ParallelWriter materialHashSet;
    public NativeParallelHashMap<ushort, int>.ParallelWriter materialHashMap;


    // Global material counter
    public NativeCounter.Concurrent materialCounter;

    public void Execute(int index) {
        Voxel voxel = voxels[index];

        // Don't care about the material for non-solid terrain
        if (voxel.density < 0.0) {
            if (materialHashSet.Add(voxel.material)) {
                materialHashMap.TryAdd(voxel.material, materialCounter.Increment());
            }
        }
    }
}
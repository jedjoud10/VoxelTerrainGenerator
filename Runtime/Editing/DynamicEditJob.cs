using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Edit job for dynamic edits
[BurstCompile(CompileSynchronously = true)]
struct DynamicEditJob<T> : IJobParallelFor
    where T : struct, IDynamicEdit {
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScaling;
    [ReadOnly] public float scalingFactor;

    public T dynamicEdit;
    public NativeArray<Voxel> voxels;

    public void Execute(int index) {
        uint3 id = VoxelUtils.IndexToPos(index);
        float3 position = (math.float3(id));

        // Needed for voxel size reduction
        position *= voxelScale;
        position -= 1.5f * voxelScale;

        //position -= math.float3(1);
        position *= vertexScaling;
        position *= scalingFactor;
        position += chunkOffset;

        // Read, modify, write
        voxels[index] = dynamicEdit.Modify(position, voxels[index]);
        //voxels[index] = Voxel.Empty;
    }
}
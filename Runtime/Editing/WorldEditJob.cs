using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Edit job for dynamic edits
[BurstCompile(CompileSynchronously = true)]
struct WorldEditJob<T> : IJobParallelFor
    where T : struct, IWorldEdit {
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
        position -= math.float3(1);
        position *= voxelScale;
        position *= vertexScaling;
        position *= scalingFactor;
        //position += chunkOffset;

        // Chunk offsets + vertex scaling
        position += math.float3((chunkOffset - ((size * scalingFactor) / (size - 3.0f)) * 0.5f));

        // Read, modify, write
        voxels[index] = dynamicEdit.Modify(position, voxels[index]);
        //voxels[index] = Voxel.Empty;
    }
}
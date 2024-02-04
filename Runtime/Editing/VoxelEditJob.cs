using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Edit job that will create the delta voxel data for each chunk
// This executes for VoxelUtils.DeltaVolume size instead of VoxelUtils.Volume
// Because we must also affect the higher LOD chunk values as well
[BurstCompile(CompileSynchronously = true)]
struct VoxelEditJob<T> : IJobParallelFor
    where T : struct, IVoxelEdit {
    [ReadOnly] public float3 chunkOffset;
    [ReadOnly] public float scalingFactor;
    [ReadOnly] public float voxelScale;
    [ReadOnly] public int size;
    [ReadOnly] public float vertexScaling;

    public T edit;
    public NativeArray<half> densities;
    public NativeArray<byte> materials;

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

        // Chunk offsets + vertex scaling
        //position += math.float3((chunkOffset - (scalingFactor * size / (size - 3.0f)) * 0.5f));

        // Read, modify, write
        byte material = materials[index];
        half density = VoxelUtils.NormalizeHalf(densities[index]);
        Voxel output = edit.Modify(position, new Voxel { material = material, density = density });
        materials[index] = output.material;
        densities[index] = VoxelUtils.NormalizeHalf(output.density);
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(CompileSynchronously = true)]
struct RunLengthDecoder : IJob {
    [WriteOnly]
    public NativeArray<ushort> uncompressed;

    // First 3 bytes: count
    // Last byte: material type
    [ReadOnly]
    public NativeArray<uint> compressed;
    public void Execute() {
        int offset = 0;
        for (int i = 0; i < compressed.Length; i++) {
            uint packed = compressed[i];
            (uint targetCount, ushort material) = VoxelUtils.UnpackRLE(packed);

            for (int k = 0; k < targetCount; k++) {
                uncompressed[k+offset] = material;
            }

            offset += (int)targetCount;
        }
    }
}
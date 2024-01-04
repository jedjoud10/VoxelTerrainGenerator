using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(CompileSynchronously = true)]
struct RunLengthEncoder : IJob {
    [ReadOnly]
    public NativeArray<ushort> uncompressed;

    // First 3 bytes: count
    // Last byte: material type
    [WriteOnly]
    public NativeArray<uint> compressed;
    public void Execute() {
        ushort last = ushort.MaxValue;
        uint matCounts = 0;
        int incr = 0;
        for (int i = 0; i < uncompressed.Length; i++) {
            ushort cur = uncompressed[i];
            if (cur == last) {
                matCounts += 1;
            } else {
                compressed[incr] = VoxelUtils.PackRLE(matCounts, last);
                last = cur;
                incr++;
            }
        }
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(CompileSynchronously = true)]
internal struct RleCompressionJob : IJob {
    [ReadOnly]
    public NativeArray<byte> bytesIn;
    [WriteOnly]
    public NativeList<uint> uintsOut;

    // 1 BYTES FOR DATA, 3 BYTES FOR COUNT
    // MAX COUNT: 16 MIL
    public void Execute() {
        byte lastByte = bytesIn[0];
        int byteCount = 0;
        for (int i = 0; i < bytesIn.Length; i++) {
            byte cur = bytesIn[i];
            if (cur == lastByte) {
                byteCount++;
            } else {
                uintsOut.Add((uint)byteCount << 8 | (uint)lastByte);
                lastByte = cur;
                byteCount = 1;
            }
        }
    }
}
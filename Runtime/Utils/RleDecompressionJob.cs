using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile(CompileSynchronously = true)]
internal struct RleDecompressionJob : IJob {
    public byte defaultValue;
    [WriteOnly]
    public NativeArray<byte> bytesOut;
    [ReadOnly]
    public NativeArray<uint> uintsIn;

    // 1 BYTES FOR DATA, 3 BYTES FOR COUNT
    // MAX COUNT: 16 MIL
    public void Execute() {
        int byteOffset = 0;
        for (int j = 0; j < uintsIn.Length; j++) {
            uint compressed = uintsIn[j];
            int count = (int)(compressed & ~0xFF) >> 8;
            byte value = (byte)(compressed & 0xFF);

            for (int k = 0; k < count; k++) {
                bytesOut[k + byteOffset] = value;
            }

            byteOffset += count;
        }

        for (int i = byteOffset; i < bytesOut.Length; i++) {
            bytesOut[i] = defaultValue;
        }
    }
}
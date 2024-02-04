using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Compression job that we will apply over each voxel chunk to compress its data for serialization
[BurstCompile(CompileSynchronously = true)]
internal struct VoxelCompressionJob : IJob {
    [ReadOnly]
    public NativeArray<half> densitiesIn;
    [WriteOnly]
    public NativeList<byte> densitiesOut;

    public void Execute() {
        ushort lastDensity = VoxelUtils.AsUshort(densitiesIn[0]);
        int densityCount = 0;
        bool rlePerfect = true;

        AddUshort(0);
        AddUshort(lastDensity);

        for (int i = 0; i < densitiesIn.Length; i++) {
            ushort newDensity = VoxelUtils.AsUshort(densitiesIn[i]);

            if (newDensity == lastDensity) {
                densityCount++;
            } else if (VoxelUtils.CouldDelta(lastDensity, newDensity)) {
                if (rlePerfect) {
                    AddUshort((ushort)densityCount);
                    AddUshort(newDensity);
                    lastDensity = newDensity;
                    densityCount = 0;
                    rlePerfect = false;
                }

                densitiesOut.Add(VoxelUtils.EncodeDelta(newDensity));
                densityCount++;
            } else {
                rlePerfect = true;
                AddUshort((ushort)densityCount);
                AddUshort(newDensity);
                lastDensity = newDensity;
                densityCount = 1;
            }

            if (densityCount == 32767) {
                rlePerfect = true;
                AddUshort((ushort)densityCount);
                AddUshort(newDensity);
                lastDensity = newDensity;
                densityCount = 0;
            }
        }
    }

    private void AddInt(int packed) {
        densitiesOut.Add((byte)(packed >> 24));
        densitiesOut.Add((byte)(packed >> 16));
        densitiesOut.Add((byte)(packed >> 8));
        densitiesOut.Add((byte)packed);
    }

    private void AddUshort(ushort packed) {
        densitiesOut.Add((byte)(packed >> 8));
        densitiesOut.Add((byte)packed);
    }
}
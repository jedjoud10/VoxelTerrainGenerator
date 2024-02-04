using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Decompression job that we will apply over each voxel chunk to decompress its data for loading
[BurstCompile(CompileSynchronously = true)]
internal struct VoxelDecompressionJob : IJob {
    [WriteOnly]
    public NativeArray<half> densitiesOut;
    [ReadOnly]
    public NativeList<byte> densitiesIn;

    public void Execute() {
        int densityOffset = 0;
        int i = 0;
        ushort newDensity = ushort.MaxValue;
        bool wasDeltaCompressed = false;
        int startOfDeltaPart = 0;

        while (i < densitiesIn.Length) {
            int currentByte = densitiesIn[i];
            if ((currentByte & (1 << 7)) == 0) {
                ushort lastDensity = newDensity;
                ushort lastDensityCount = VoxelUtils.BytesToUshort(densitiesIn[i], densitiesIn[i + 1]);
                newDensity = VoxelUtils.BytesToUshort(densitiesIn[i+2], densitiesIn[i + 3]);

                /*
                Debug.Log("Byte1: " + byte1);
                Debug.Log("Byte2: " + byte2);
                Debug.Log("Byte3: " + byte3);
                Debug.Log("Byte4: " + byte4);
                */

                //Debug.Log("Dens: " + newDensity);
                //Debug.Log("Cnt: " + lastDensityCount);

                if (wasDeltaCompressed) {
                    for (int k = 0; k < lastDensityCount; k++) {
                        ushort lsb = VoxelUtils.DecodeDelta(densitiesIn[k + startOfDeltaPart]);
                        ushort packedDensity = (ushort)((lastDensity & 0xFF80) | lsb);
                        densitiesOut[k + densityOffset] = VoxelUtils.AsHalf(packedDensity);
                    }
                } else {
                    for (int k = 0; k < lastDensityCount; k++) {
                        densitiesOut[k + densityOffset] = VoxelUtils.AsHalf(lastDensity);
                    }
                }

                if (densityOffset + lastDensityCount >= 262144) {
                    Debug.LogWarning("VERY BAD");
                }

                wasDeltaCompressed = false;
                startOfDeltaPart = -1;
                densityOffset += lastDensityCount;
                i += 4;
            } else {
                if (!wasDeltaCompressed) {
                    //wasDeltaCompressed = true;
                    //startOfDeltaPart = i;
                }

                i++;
            }
        }
    }
}
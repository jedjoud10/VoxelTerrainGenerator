using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Decompression job that we will apply over each voxel chunk to decompress its data for loading
//[BurstCompile(CompileSynchronously = true)]
struct DecompressionJob : IJob {
    [WriteOnly]
    public UnsafeList<half> densitiesOut;
    [WriteOnly]
    public UnsafeList<ushort> materialsOut;

    [ReadOnly]
    public NativeList<uint> materialsIn;
    [ReadOnly]
    public NativeList<byte> densitiesIn;



    public void Execute() {
        int materialOffset = 0;
        for (int j = 0; j < materialsIn.Length; j++) {
            uint packed = materialsIn[j];
            (int targetCount, ushort material) = VoxelUtils.UnpackMaterialRLE(packed);

            for (int k = 0; k < targetCount; k++) {
                materialsOut[k + materialOffset] = material;
            }

            materialOffset += targetCount;
        }

        int densityOffset = 0;
        int i = 0;
        ushort newDensityMSB = ushort.MaxValue;
        Debug.Log("What did he mean by this");
        while (i < densitiesIn.Length) {
            byte currentByte = densitiesIn[i];

            if ((currentByte & (1 << 7)) == 0) {
                byte byte1 = currentByte;
                byte byte2 = densitiesIn[i+1];
                byte byte3 = densitiesIn[i+2];
                byte byte4 = densitiesIn[i+3];

                int packed = byte1 << 24 | byte2 << 16 | byte3 << 8 | byte4;
                ushort lastDensityMSB = newDensityMSB;
                VoxelUtils.UnpackRLEBatch(packed, out newDensityMSB, out int lastDensityCount);

                // do delta overwriting over here
                for (int k = 0; k < lastDensityCount; k++) {
                    /*
                    ushort lsb = VoxelUtils.DecodeDelta(densitiesIn[k + i]);
                    ushort packedDensity = (ushort)(lastDensityMSB << 7 | lsb);
                    densitiesOut[k + densityOffset] = VoxelUtils.AsHalf(packedDensity);
                    */
                    densitiesOut[k + densityOffset] = half.zero;
                }

                densityOffset += lastDensityCount;
                i += lastDensityCount;
                i += 4;
            } else {
                i++;
            }
        }
    }
}
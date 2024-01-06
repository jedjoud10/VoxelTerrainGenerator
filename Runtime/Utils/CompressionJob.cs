using Codice.CM.Common;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

// Compression job that we will apply over each voxel chunk to compress its data for serialization
[BurstCompile(CompileSynchronously = true)]
struct CompressionJob : IJob {
    [ReadOnly]
    public UnsafeList<half> densitiesIn;
    [ReadOnly]
    public UnsafeList<ushort> materialsIn;

    [WriteOnly]
    public NativeList<uint> materialsOut;
    [WriteOnly]
    public NativeList<byte> densitiesOut;

    public void Execute() {
        ushort lastMaterial = materialsIn[0];
        int materialCount = 0;
        for (int i = 0; i < materialsIn.Length; i++) {
            ushort cur = materialsIn[i];
            if (cur == lastMaterial) {
                materialCount++;
            } else {
                materialsOut.Add(VoxelUtils.PackMaterialRle(materialCount, lastMaterial));
                lastMaterial = cur;
                materialCount = 1;
            }
        }

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
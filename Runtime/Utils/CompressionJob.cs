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
                materialsOut.Add(VoxelUtils.PackMaterialRLE(materialCount, lastMaterial));
                lastMaterial = cur;
                materialCount = 1;
            }
        }

        /*
        ushort lastDensity = VoxelUtils.AsUshort(densitiesIn[0]);
        int densityCount = 0;
        bool 
        AddInt(VoxelUtils.PackRLEBatch(lastDensity, 0));
        for (int i = 0; i < densitiesIn.Length; i++) {
            ushort newDensity = VoxelUtils.AsUshort(densitiesIn[i]);
            if (newDensity == lastDensity) {
                densitiesOut.Add(VoxelUtils.EncodeDelta(newDensity));
                densityCount++;
            } else if (VoxelUtils.CouldDelta(lastDensity, newDensity)) {
                densitiesOut.Add(VoxelUtils.EncodeDelta(newDensity));
                densityCount++;
            } else {
                AddInt(VoxelUtils.PackRLEBatch(newDensity, densityCount));
                lastDensity = newDensity;
                densityCount = 1;
            }
        }
        */
    }

    private void AddInt(int packed) {
        densitiesOut.Add((byte)(packed >> 24));
        densitiesOut.Add((byte)(packed >> 16));
        densitiesOut.Add((byte)(packed >> 8));
        densitiesOut.Add((byte)(packed));
    }

}
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

[BurstCompile(CompileSynchronously = true)]
struct RunLengthDecoder : IJobParallelFor {
    [WriteOnly]
    public NativeArray<ushort> uncompressed;

    // First 3 bytes: count
    // Last byte: material type
    [ReadOnly]
    public NativeArray<uint> compressed;
    public void Execute(int index) {
    }
}
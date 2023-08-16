using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;

// Sums up the material count indices to make proper offsets for each material segment shiz
[BurstCompile(CompileSynchronously = true)]
public struct SumJob : IJobParallelFor
{
    // Offsets for each material type 
    [WriteOnly]
    public NativeArray<int> materialSegmentOffsets;

    [ReadOnly]
    // Multiple counters for each material type
    public NativeMultiCounter materialQuadCounter;

    [ReadOnly]
    // Global material counter
    public NativeCounter materialCounter;

    public void Execute(int index)
    {
        if (index > materialCounter.Count)
            return;

        int sum = 0;

        for (int i = 0; i < index; i++)
        {
            sum += materialQuadCounter[i];
        }

        materialSegmentOffsets[index] = sum;
    }
}
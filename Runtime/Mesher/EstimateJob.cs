using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;

// Estimate job that will try to estimate the number of quads we will have per material type
[BurstCompile(CompileSynchronously = true)]
public struct EstimateJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<Voxel> voxels;

    // List of enabled corners like in MC
    [ReadOnly]
    public NativeArray<byte> enabled;

    // Best coder NA trust
    [ReadOnly]
    public NativeParallelHashMap<ushort, int> materialHashMap;

    // Multiple counters for each material type
    public NativeMultiCounter.Concurrent materialQuadCounter;

    public void Execute(int index)
    {
        ushort enabledEdges = VoxelUtils.EdgeMasks[enabled[index]];
        ushort voxelMaterial = voxels[index].material;

        // Checks if the edges 0, 3, and 8 will be activated
        if ((enabledEdges & 0x109) != 0)
        {
            int materialLocalIndex = materialHashMap[voxelMaterial];
            materialQuadCounter.Increment(materialLocalIndex);
        }
    }
}
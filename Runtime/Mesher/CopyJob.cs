using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

// Copies the temp triangulation data to the permanent location where we store the offsets too
[BurstCompile(CompileSynchronously = true)]
public struct CopyJob : IJobParallelFor
{
    // Offsets for each material type 
    [ReadOnly]
    public NativeArray<int> materialSegmentOffsets;

    // Sparse triangles based on the material segment offsets
    [ReadOnly]
    public NativeArray<int> tempTriangles;

    // Packed triangles based on material segment offsets
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<int> permTriangles;

    // Quad Counter for each material
    [ReadOnly]
    public NativeMultiCounter counters;

    // Global material counter
    [ReadOnly]
    public NativeCounter materialCounter;

    public void Execute(int index)
    {
        if (materialCounter.Count == 0 || index >= materialCounter.Count)
            return;

        int segmentOffset = tempTriangles.Length / materialCounter.Count;

        // Segment offset for the temp index buffer, not the perm one
        // Segment offset in the temp buffer is constant, so each sub-mesh contains the same amount of indices
        // (not goodo, which is why we have the perm one in the first hand)
        int material = index;
        int readOffset = segmentOffset * material;
        int offset = materialSegmentOffsets[material];

        for (int i = 0; i < counters[material] * 6; i++)
        {
            int val = tempTriangles[i + readOffset];
            permTriangles[offset + i] = val;
        }
    }
}
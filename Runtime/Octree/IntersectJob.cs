using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using static UnityEngine.GraphicsBuffer;

// Job that's going to detect what intersected the octree using an AABB
[BurstCompile(CompileSynchronously = true)]
public struct IntersectJob : IJob
{
    // The input AABB (in octree space)
    public float3 min;
    public float3 max;

    // Leaf nodes that intersected the AABB
    NativeList<OctreeNode> intersectLeafs;

    // Currently pending nodes for generation
    public NativeQueue<OctreeNode> pending;

    public void Execute()
    {
        intersectLeafs.Clear();
        while (pending.TryDequeue(out OctreeNode node))
        {
            if (node.IntersectsAABB(min, max))
            {
            }
        }
    }
}
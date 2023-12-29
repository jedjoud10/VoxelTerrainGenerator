using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using static UnityEngine.GraphicsBuffer;

// Job that's going to detect what intersected the octree using an AABB
[BurstCompile(CompileSynchronously = true)]
public struct IntersectJob : IJob {
    // The input AABB (in octree space)
    [ReadOnly] public float3 min;
    [ReadOnly] public float3 max;

    // Nodes currently stored in the octree
    [ReadOnly]
    public NativeList<OctreeNode> nodes;

    // Leaf nodes that intersected the AABB
    [WriteOnly]
    public NativeList<OctreeNode> intersectLeafs;

    // Currently pending nodes for generation
    public NativeQueue<int> pending;

    public void Execute() {
        while (pending.TryDequeue(out int index)) {
            var node = nodes[index];
            if (node.IntersectsAABB(min, max)) {
                if (node.ChildBaseIndex != -1) {
                    for (int i = 0; i < 8; i++) {
                        pending.Enqueue(node.ChildBaseIndex + i);
                    }
                } else {
                    intersectLeafs.Add(node);
                }
            }
        }
    }
}
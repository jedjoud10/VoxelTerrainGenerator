using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

// Job that's going to detect what intersected the octree using an AABB
[BurstCompile(CompileSynchronously = true)]
public struct IntersectJob : IJob {
    // The input AABBs
    [ReadOnly]
    public NativeArray<Bounds> bounds;

    // Nodes currently stored in the octree
    [ReadOnly]
    public NativeList<OctreeNode> nodes;

    // Leaf nodes that intersected the AABB
    [WriteOnly]
    public NativeList<OctreeNode> intersectLeafs;

    // Currently pending nodes for generation
    public NativeQueue<int> pending;

    public void Execute() {
        // Most optimized jed code (jed try to make optimized code challenge (IMPOSSIBLE))
        while (pending.TryDequeue(out int index)) {
            var node = nodes[index];

            bool any = false;
            for (int i = 0; i < bounds.Length; i++) {
                if (node.IntersectsAABB(bounds[i].min, bounds[i].max)) {
                    any = true;
                    break;
                }
            }

            if (any) {
                if (node.childBaseIndex != -1) {
                    for (int i = 0; i < 8; i++) {
                        pending.Enqueue(node.childBaseIndex + i);
                    }
                } else {
                    intersectLeafs.Add(node);
                }
            }
        }
    }
}
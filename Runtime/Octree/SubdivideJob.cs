using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;

// This will handle generating the nodes for one of the starting nodes
[BurstCompile(CompileSynchronously = true)]
public struct SubdivideJob : IJob {
    // The total nodes that where generated
    public NativeList<OctreeNode> nodes;

    // Currently pending nodes for generation
    public NativeQueue<OctreeNode> pending;

    // The octree chunk targets 
    [ReadOnly]
    public NativeArray<OctreeTarget> targets;

    [ReadOnly] public int maxDepth;
    [ReadOnly] public int segmentSize;

    public void Execute() {
        while (pending.TryDequeue(out OctreeNode node)) {
            TrySubdivide(ref node);
        }
    }

    // Check if we can subdivide this node. This is the part that we can tune to our liking
    public bool ShouldSubdivide(ref OctreeNode node) {
        bool subdivide = false;

        foreach (var target in targets) {
            float3 minBounds = math.float3(node.Position);
            float3 maxBounds = math.float3(node.Position) + math.float3(node.Size);
            float3 clamped = math.clamp(target.center, minBounds, maxBounds);
            bool local = math.distance(clamped, target.center) < target.radius * node.ScalingFactor;
            subdivide |= local;
        }

        return node.Size > segmentSize;
    }

    // Position offsets for creating the nodes
    public static readonly int3[] offsets = {
        new int3(0, 0, 0),
        new int3(0, 0, 1),
        new int3(1, 0, 0),
        new int3(1, 0, 1),
        new int3(0, 1, 0),
        new int3(0, 1, 1),
        new int3(1, 1, 0),
        new int3(1, 1, 1),
    };

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref OctreeNode node) {
        if (ShouldSubdivide(ref node) && node.Depth < maxDepth) {
            node.ChildBaseIndex = nodes.Length;

            for (int i = 0; i < 8; i++) {
                float3 offset = math.float3(offsets[i]);
                OctreeNode child = new OctreeNode {
                    Position = offset * (node.Size / 2.0F) + node.Position,
                    Depth = node.Depth + 1,
                    Size = node.Size / 2,
                    ParentIndex = node.Index,
                    Index = node.ChildBaseIndex + i,
                    ChildBaseIndex = -1,
                    Skirts = 0,
                    ScalingFactor = node.ScalingFactor / 2.0F,
                };

                pending.Enqueue(child);
                nodes.Add(child);
            }

            nodes[node.Index] = node;
        }
    }
}
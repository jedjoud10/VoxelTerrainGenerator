using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

// This will handle generating the nodes for one of the starting nodes
[BurstCompile(CompileSynchronously = true)]
public struct SubdivideJob<T> : IJob where T: struct, IOctreeSubdivider {
    // The total nodes that where generated
    public NativeList<OctreeNode> nodes;

    // Currently pending nodes for generation
    public NativeQueue<OctreeNode> pending;

    // The octree chunk targets 
    [ReadOnly]
    public NativeArray<OctreeTarget> targets;

    [ReadOnly] public int maxDepth;
    [ReadOnly] public int segmentSize;
    public T subdivider;

    public void Execute() {
        while (pending.TryDequeue(out OctreeNode node)) {
            TrySubdivide(ref node);
        }

        /*
        // Check all nodes to subdivide them again if they have parent siblings 
        // Should stop lower depth nodes being so close to higher depth nodes
        for (int i = 1; i < nodes.Length; i++) {
            // Check if siblings are parents
            int parentIndex = nodes[i].ParentIndex;
            OctreeNode parent = nodes[parentIndex];
            bool grandparent = false;

            for (int k = 0; k < 8; k++) {
                grandparent |= nodes[parent.ChildBaseIndex + k].ChildBaseIndex != -1;
            }

            // second check shouldn't even matter since it would be impossible for last depth nodes to have a parent who is a grandparent but wtv
            if (grandparent && nodes[i].Depth < maxDepth && nodes[i].ChildBaseIndex == -1) {
                ForceSubdivide(nodes[i]);
            }
        }
        */
        

        pending.Clear();
    }

    // Check if we can subdivide this node. This is the part that we can tune to our liking
    public bool ShouldSubdivide(ref OctreeNode node) {
        return subdivider.ShouldSubdivide(ref node, ref targets);
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

    // Force the subdivision of the current node, even though it might not be valid
    public void ForceSubdivide(OctreeNode node) {
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

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref OctreeNode node) {
        if (ShouldSubdivide(ref node) && node.Depth < maxDepth)
            ForceSubdivide(node);
    }
}
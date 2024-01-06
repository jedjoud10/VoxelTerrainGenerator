using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

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
    public T subdivider;

    public void Execute() {
        while (pending.TryDequeue(out OctreeNode node)) {
            TrySubdivide(ref node);
        }
    }

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref OctreeNode node) {
        if (subdivider.ShouldSubdivide(ref node, ref targets) && node.depth < maxDepth) {
            node.ChildBaseIndex = nodes.Length;

            for (int i = 0; i < 8; i++) {
                float3 offset = math.float3(VoxelUtils.OctreeChildOffset[i]);
                OctreeNode child = new OctreeNode {
                    position = offset * (node.size / 2.0F) + node.position,
                    depth = node.depth + 1,
                    size = node.size / 2,
                    parentIndex = node.index,
                    index = node.ChildBaseIndex + i,
                    ChildBaseIndex = -1,
                    Skirts = 0,
                    ScalingFactor = node.ScalingFactor / 2.0F,
                };

                pending.Enqueue(child);
                nodes.Add(child);
            }

            nodes[node.index] = node;
        }
    }
}
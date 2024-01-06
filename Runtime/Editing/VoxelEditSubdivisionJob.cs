using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
internal struct VoxelEditSubdivisionJob : IJob {
    public NativeHashMap<OctreeNode, int> chunkLookup;
    public NativeList<VoxelEditOctreeNode> nodes;
    public NativeHashMap<int, int> lookup;
    public int sparseVoxelCountOffset;
    public NativeList<PosScale> addedNodes;
    public NativeList<int> chunksToUpdate;
    public Bounds voxelEditBounds;

    public NativeQueue<VoxelEditOctreeNode> pending;
    [ReadOnly] public int maxDepth;

    public void Execute() {
        while (pending.TryDequeue(out VoxelEditOctreeNode node)) {
            TrySubdivide(ref node);
        }        
    }

    public void TrySubdivide(ref VoxelEditOctreeNode node) {
        if (node.Bounds.Intersects(voxelEditBounds) && node.depth <= maxDepth) {
            if (!lookup.ContainsKey(node.index)) {
                int lookupIndex = sparseVoxelCountOffset + addedNodes.Length;
                node.lookup = lookupIndex;
                addedNodes.Add(new PosScale { position = node.position, scalingFactor = node.scalingFactor });
                chunkLookup.Add(new OctreeNode {
                    Position = node.position,
                    Depth = node.depth,
                    Size = node.size,
                    ChildBaseIndex = -1
                }, lookupIndex);
                lookup.Add(node.index, lookupIndex);
            }

            if (node.lookup != -1) {
                chunksToUpdate.Add(node.lookup);
            }

            if (node.depth < maxDepth) {
                node.childBaseIndex = nodes.Length;
                nodes[node.index] = node;

                for (int i = 0; i < 8; i++) {
                    float3 offset = math.float3(VoxelUtils.OctreeChildOffset[i]);
                    VoxelEditOctreeNode child = new VoxelEditOctreeNode {
                        position = offset * (node.size / 2.0F) + node.position,
                        depth = node.depth + 1,
                        lookup = -1,
                        size = node.size / 2,
                        index = node.childBaseIndex + i,
                        childBaseIndex = -1,
                        scalingFactor = node.scalingFactor / 2.0F,
                    };

                    pending.Enqueue(child);
                    nodes.Add(child);
                }
            }
        }
    }
}
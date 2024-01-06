using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
internal struct VoxelEditSubdivisionJob : IJob {
    public NativeHashMap<VoxelEditOctreeNode, int> lookup;
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
        if (node.Bounds.Intersects(voxelEditBounds) && node.Depth <= maxDepth) {
            int lookupIndex = sparseVoxelCountOffset + addedNodes.Length;

            if (!lookup.ContainsKey(node)) {
                addedNodes.Add(new PosScale { position = node.Position, scalingFactor = node.ScalingFactor });
                lookup.Add(node, lookupIndex);
                chunksToUpdate.Add(lookupIndex);
            }

            if (node.Depth < maxDepth) {
                lookup.Remove(node);
                node.Parent = true;
                lookup.Add(node, lookupIndex);

                for (int i = 0; i < 8; i++) {
                    float3 offset = math.float3(VoxelUtils.OctreeChildOffset[i]);
                    VoxelEditOctreeNode child = new VoxelEditOctreeNode {
                        Position = offset * (node.Size / 2.0F) + node.Position,
                        Depth = node.Depth + 1,
                        Size = node.Size / 2,
                        Parent = false,
                        ScalingFactor = node.ScalingFactor / 2.0F,
                    };

                    pending.Enqueue(child);
                }
            }
        }
    }
}
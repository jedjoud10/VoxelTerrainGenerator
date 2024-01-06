using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

// Two of these jobs put out in parallel to handle diffing
[BurstCompile(CompileSynchronously = true)]
public struct DiffJob : IJob {
    [ReadOnly]
    public NativeHashSet<OctreeNode> oldNodesHashSet;

    [ReadOnly]
    public NativeHashSet<OctreeNode> newNodesHashSet;

    [WriteOnly]
    public NativeList<OctreeNode> diffedNodes;

    public void Execute() {
        diffedNodes.Clear();

        foreach (var node in oldNodesHashSet) {
            if (!newNodesHashSet.Contains(node)) {
                diffedNodes.Add(node);
            }
        }
    }
}
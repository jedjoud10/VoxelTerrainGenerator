using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

// Two of these jobs put out in parallel to handle diffing
[BurstCompile(CompileSynchronously = true)]
public struct ToHashSetJob : IJob
{
    [ReadOnly]
    public NativeList<OctreeNode> oldNodesList;
    [WriteOnly]
    public NativeHashSet<OctreeNode> oldNodesHashSet;

    [ReadOnly]
    public NativeList<OctreeNode> newNodesList;
    [WriteOnly]
    public NativeHashSet<OctreeNode> newNodesHashSet;

    public void Execute()
    {
        oldNodesHashSet.Clear();
        newNodesHashSet.Clear();

        foreach (var node in oldNodesList)
        {
            oldNodesHashSet.Add(node);
        }

        foreach (var node in newNodesList)
        {
            newNodesHashSet.Add(node);
        }
    }
}
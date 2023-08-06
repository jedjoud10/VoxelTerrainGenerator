using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

// Two of these jobs put out in parallel to handle diffing
[BurstCompile(CompileSynchronously = true)]
public struct DiffJob : IJob
{
    // The old nodes that where generated
    [ReadOnly]
    public NativeHashSet<OctreeNode> oldNodes;

    // The total nodes that where generated
    [ReadOnly]
    public NativeHashSet<OctreeNode> nodes;

    [WriteOnly]
    public NativeList<OctreeNode> diffedNodes;

    public bool direction;

    public void Execute()
    {
        diffedNodes.Clear();

        if (direction)
        {
            foreach (var node in oldNodes)
            {
                if (!nodes.Contains(node))
                {
                    diffedNodes.Add(node);
                }
            }
        }
        else
        {
            foreach (var node in nodes)
            {
                if (!oldNodes.Contains(node))
                {
                    diffedNodes.Add(node);
                }
            }
        }
    }
}
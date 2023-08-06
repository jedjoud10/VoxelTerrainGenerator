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
    public NativeHashMap<int3, OctreeNode> oldNodes;

    // The total nodes that where generated
    [ReadOnly]
    public NativeHashMap<int3, OctreeNode> nodes;

    [WriteOnly]
    public NativeList<OctreeNode> diffedNodes;

    public bool direction;

    public void Execute()
    {
        diffedNodes.Clear();

        if (direction)
        {
            foreach (var pair in oldNodes)
            {
                if (!nodes.ContainsKey(pair.Key))
                {
                    diffedNodes.Add(pair.Value);
                }
            }
        }
        else
        {
            foreach (var pair in nodes)
            {
                if (!oldNodes.ContainsKey(pair.Key))
                {
                    diffedNodes.Add(pair.Value);
                }
            }
        }
    }
}
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

// This will handle generating the nodes for one of the starting nodes
[BurstCompile(CompileSynchronously = true)]
public struct SubdivideJob : IJob
{
    // The total nodes that where generated
    [WriteOnly]
    public NativeHashSet<OctreeNode> nodes;

    // Currently pending nodes for generation
    public NativeQueue<OctreeNode> pending;

    // The octree chunk targets 
    [ReadOnly]
    public NativeArray<OctreeTarget> targets;

    // Used for distance scaling
    public float globalLodMultiplier;

    public void Execute()
    {
        nodes.Clear();
        while (pending.TryDequeue(out OctreeNode node)) {
            node.TrySubdivide(ref targets, ref nodes, ref pending, globalLodMultiplier);
        }
    }
}
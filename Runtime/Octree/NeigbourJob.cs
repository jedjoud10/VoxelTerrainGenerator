using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;
using System;

// Checks if the node has neighbours of different sizes or not
// Used for creating the appropriate skirts
[BurstCompile(CompileSynchronously = true)]
public struct NeighbourJob : IJobParallelFor
{
    // Copy of input data so we can execute this in parallel
    [ReadOnly]
    public NativeArray<OctreeNode> inputNodes;

    // Output nodees that we will set the update data for
    public NativeArray<OctreeNode> outputNodes;

    // Direction data
    static readonly float3[] directions = new float3[]
    {
        new float3(1.0F, 0, 0),
        new float3(0, 1.0F, 0),
        new float3(0, 0, 1.0F),
        new float3(-1.0F, 0, 0),
        new float3(0, -1.0F, 0),
        new float3(0, 0, -1.0F),
    };

    public void Execute(int index)
    {
        // Find node (current)
        OctreeNode node = inputNodes[index];

        // Only do this for leaf nodes
        if (node.Depth < node.maxDepth)
            return;

        // Check neighbours in each direction
        for (int i = 0; i < 6; i++)
        {
            // Go up the tree until we reach the root or a node in our direction
            int parentIndex = node.ParentIndex;
            while (true)
            {
                OctreeNode parent = inputNodes[parentIndex];
                float projectedVal = (node.Center - parent.Center)[i % 3];

                // Check if we found a node which faces in the needed direction
                if ((i < 3) ? (projectedVal > 0) : (projectedVal < 0))
                {
                    // We found a parent, start going down the tree
                    break;
                } else
                {
                    // If not, keep going up the tree (updating root as well)
                    parentIndex = parent.ParentIndex;
                }
            }

            // Start going down the tree base on the parent index
            OctreeNode selectedParent = inputNodes[parentIndex];
            float3 estimatedNeighbouringNodeCenter = math.float3(node.Position) + math.float3(node.Size) / 2.0F + math.float3(node.Size * directions[i]);
            int nextChildIndex = -1;

            // Recursively pick the child that intersects the "neigbouring node" estimated center
            while (true)
            {
                for (int k = 0; k < 8; k++)
                {
                    if (inputNodes[nextChildIndex].ContainsPoint(estimatedNeighbouringNodeCenter))
                    {
                        nextChildIndex = selectedParent.ChildBaseIndex + k;
                    }
                }
            }

        }
    }
}
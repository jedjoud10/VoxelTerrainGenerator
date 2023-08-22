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
    // This contains the nodes for the whole tree
    [ReadOnly]
    public NativeArray<OctreeNode> inputNodes;

    // Output nodees that we will set the update data for
    // These are the added diffed nodes
    public NativeArray<OctreeNode> outputNodes;

    // Direction data
    static readonly float3[] directions = new float3[]
    {
        new float3(-1.0F, 0, 0),
        new float3(0, -1.0F, 0),
        new float3(0, 0, -1.0F),
        new float3(1.0F, 0, 0),
        new float3(0, 1.0F, 0),
        new float3(0, 0, 1.0F),
    };


    public void Execute(int index)
    {
        // Find node (current)
        OctreeNode node = outputNodes[index];

        // Skip if it's not a leaf
        if (node.ChildBaseIndex != -1)
            return;

        // Skirts that we must apply to the nodes
        int skirts = 0x3F;

        for (int i = 0; i < 6; i++)
        {
            float3 estimatedNeighbouringNodeCenter = math.float3(node.Position) + math.float3(node.Size) / 2.0F + math.float3(node.Size * directions[i]);
            int nextChildIndex = 0;
            bool breakOuter = false;
            int iter = 0;

            // Recursively pick the child that intersects the "neigbouring node" estimated center
            while (!breakOuter)
            {
                if (iter > 10000)
                    break;

                for (int k = 0; k < 8; k++)
                {
                    OctreeNode cur = inputNodes[nextChildIndex + k];

                    if (cur.ContainsPoint(estimatedNeighbouringNodeCenter))
                    {
                        nextChildIndex = cur.ChildBaseIndex;

                        // We reached a node with the same depth as ours
                        if (cur.Depth == node.Depth)
                        {
                           breakOuter = true;

                            
                            // Enable skirts if said node goes deeper
                            if (cur.ChildBaseIndex != -1)
                                skirts |= 1 << i;
                            else
                                skirts &= ~(1 << i);
                            
                        
                            break;
                        }

                        // We reached the bottom of the tree (or too deep), break out
                        if (nextChildIndex == -1 || cur.Depth > node.Depth)
                        {
                            breakOuter = true;
                            break;
                        }

                        break;
                    }
                }

                iter++;
            }
        }

        node.Skirts = skirts;
        outputNodes[index] = node;
    }
}
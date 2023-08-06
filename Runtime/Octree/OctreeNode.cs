using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

// A singular octree node stored within the octree
// Stored as a struct for performance reasons and to be able to use it within jobs
public struct OctreeNode: IEquatable<OctreeNode>
{
    // Position offsets
    public static readonly int3[] offsets = 
    {
        new int3(0, 0, 0),
        new int3(0, 0, 1),
        new int3(1, 0, 0),
        new int3(1, 0, 1),
        new int3(0, 1, 0),
        new int3(0, 1, 1),
        new int3(1, 1, 0),
        new int3(1, 1, 1),
    };

    // Start position (0, 0, 0) of the octree node
    public int3 position;

    // Inverse Depth of the node starting from maxDepth
    public int invDepth;

    // The full size of the node
    public int size;

    // The center of the node
    public int3 center;

    // Create the root node
    public static OctreeNode RootNode(int maxDepth)
    {
        int size = (int)(math.pow(2.0F, (float)(maxDepth)));
        OctreeNode node = new OctreeNode();
        node.position = -math.int3(size / 2);
        node.invDepth = maxDepth;
        node.center = int3.zero;
        node.size = size;
        return node;
    }

    // Calculate the scaling factor that must be applied
    public float ScalingFactor()
    {
        int size = (int)(math.pow(2.0F, (float)(invDepth)));
        return size;
    }

    public bool Equals(OctreeNode other)
    {
        return math.all(this.position == other.position) &&
            this.invDepth == other.invDepth &&
            this.size == other.size;
    }

    // Check if we can subdivide this node
    public bool ShouldSubdivide(ref NativeArray<OctreeTarget> targets, float globalLodMultiplier = 1.0F)
    {
        bool subdivide = false;

        foreach (var target in targets)
        {
            float3 minBounds = math.float3(position);
            float3 maxBounds = math.float3(position) + math.float3(size);
            float3 clamped = math.clamp(target.center, minBounds, maxBounds);
            subdivide |= math.distance(clamped, target.center) < target.radius;
        }

        return subdivide;
    }

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref NativeArray<OctreeTarget> targets, ref NativeHashMap<int3, OctreeNode> nodes, ref NativeQueue<OctreeNode> pending, float globalLodMultiplier = 1.0F)
    {
        if (ShouldSubdivide(ref targets, globalLodMultiplier) && invDepth > 0)
        {
            for (int i = 0; i < 8; i++)
            {
                int3 offset = offsets[i];
                OctreeNode node = new OctreeNode
                {
                    position = offset * (size / 2) + this.position,
                    invDepth = invDepth - 1,
                    size = size / 2
                };

                node.center = node.position + node.size / 2;

                pending.Enqueue(node);
            }

        } else
        {
            nodes.TryAdd(center, this);
        }
    }
}

using Codice.Client.BaseCommands;
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
    // Invalid type octree node
    public static OctreeNode Invalid = new OctreeNode
    {
        ParentIndex = -1,
        Index = -1,
        Depth = -1,
        maxDepth = -1,
        Position = float3.zero,
        Size = 0,
        ChildBaseIndex = -1,
        Skirts = -1,
    };

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
    public float3 Position { get; private set; }

    // Inverse Depth of the node starting from maxDepth
    public int Depth { get; private set; }

    // Max depth propagated from the main octree node
    internal int maxDepth;

    // The full size of the node
    public float Size { get; private set; }

    // Should we generate collisions from this node
    public bool GenerateCollisions { get; internal set; }

    // Index of the current node inside the array
    public int Index { get; internal set; }

    // Index of the parent node (-1 if root)
    public int ParentIndex { get; internal set; }

    // Index of the children nodes
    public int ChildBaseIndex { get; internal set; }

    // Center of the node
    public float3 Center => math.float3(Position) + math.float3(Size) / 2.0F;

    // Scaling factor applied to chunks 
    public float ScalingFactor => math.pow(2.0F, (float)maxDepth - Depth);

    // Directions in which skirts should be enabled
    // First 3 bits depict the "base" skirts (x = 0, etc...)
    // Next 3 bits depict the "end" skirts (x = size - 2, etc...)
    public int Skirts { get; internal set; }

    // Create the root node
    public static OctreeNode RootNode(int maxDepth)
    {
        float size = (int)(math.pow(2.0F, (float)(maxDepth))) * VoxelUtils.Size * VoxelUtils.VoxelSize;
        OctreeNode node = new OctreeNode();
        node.Position = -math.int3(size / 2);
        node.Depth = 0;
        node.maxDepth = maxDepth;
        node.Size = size;
        node.Index = 0;
        node.ParentIndex = -1;
        node.ChildBaseIndex = 1;
        return node;
    }

    public bool Equals(OctreeNode other)
    {
        return math.all(this.Position == other.Position) &&
            this.Depth == other.Depth &&
            this.Size == other.Size &&
            (this.ChildBaseIndex == -1) == (other.ChildBaseIndex == -1);
    }

    // https://forum.unity.com/threads/burst-error-bc1091-external-and-internal-calls-are-not-allowed-inside-static-constructors.1347293/
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + Position.GetHashCode();
            hash = hash * 23 + Depth.GetHashCode();
            hash = hash * 23 + ChildBaseIndex.GetHashCode();
            hash = hash * 23 + Size.GetHashCode();
            return hash;
        }
    }

    // Check if this node intersects the given AABB
    public bool IntersectsAABB(float3 min, float3 max)
    {
        float3 nodeMin = math.float3(Position);
        float3 nodeMax = math.float3(Position) + math.float3(Size);
        return math.all(min <= nodeMax) && math.all(nodeMin <= max);
    }

    // Check if this node contains a point
    internal bool ContainsPoint(float3 point)
    {
        float3 nodeMin = math.float3(Position);
        float3 nodeMax = math.float3(Position) + math.float3(Size);
        return math.all(nodeMin <= point) && math.all(point <= nodeMax);
    }

    // Check if we can subdivide this node (and also if we should generate collisions)
    public bool ShouldSubdivide(ref NativeArray<OctreeTarget> targets, ref NativeArray<float> qualityPoints, out bool generateCollisions)
    {
        bool subdivide = false;
        generateCollisions = false;

        foreach (var target in targets)
        {
            float3 minBounds = math.float3(Position);
            float3 maxBounds = math.float3(Position) + math.float3(Size);
            float3 clamped = math.clamp(target.center, minBounds, maxBounds);

            bool local = math.distance(clamped, target.center) < target.radius * ScalingFactor * qualityPoints[Depth];
            subdivide |= local;
            generateCollisions |= local && target.generateCollisions;
        }

        return subdivide;
    }

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref NativeArray<OctreeTarget> targets, ref NativeList<OctreeNode> nodes, ref NativeQueue<OctreeNode> pending, ref NativeArray<float> qualityPoints)
    {
        if (ShouldSubdivide(ref targets, ref qualityPoints, out bool generateChildrenCollisions) && Depth < maxDepth)
        {
            ChildBaseIndex = nodes.Length;

            for (int i = 0; i < 8; i++)
            {
                float3 offset = math.float3(offsets[i]);
                OctreeNode node = new OctreeNode
                {
                    Position = offset * (Size / 2.0F) + this.Position,
                    Depth = Depth + 1,
                    Size = Size / 2,
                    maxDepth = maxDepth,
                    ParentIndex = this.Index,
                    Index = ChildBaseIndex + i,
                    ChildBaseIndex = -1,
                    GenerateCollisions = generateChildrenCollisions && Depth == (maxDepth-1),
                };

                pending.Enqueue(node);
                nodes.Add(node);
            }

            nodes[this.Index] = this;
        }
    }
}

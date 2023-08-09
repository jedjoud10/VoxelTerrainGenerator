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
    public int depth;

    // Max depth propagated from the main octree node
    public int maxDepth;

    // The full size of the node
    public int size;

    // Is this node a leaf node or not
    public bool leaf;

    // Should we generate collisions from this node
    public bool generateCollisions;

    // Create the root node
    public static OctreeNode RootNode(int maxDepth)
    {
        int size = (int)(math.pow(2.0F, (float)(maxDepth))) * 1;
        OctreeNode node = new OctreeNode();
        node.position = -math.int3(size / 2);
        node.depth = 0;
        node.maxDepth = maxDepth;
        node.size = size;
        return node;
    }

    // Calculate the world center position of the octree node
    public Vector3 WorldCenter()
    {
        float scaling = (float)VoxelUtils.Size * VoxelUtils.VoxelSize;
        float3 center = math.float3(position) + math.float3((float)size / 2.0F);
        return new Vector3(center.x, center.y, center.z) * scaling;
    }

    // Calculate the world position of the octree node
    public Vector3 WorldPosition()
    {
        float scaling = (float)VoxelUtils.Size * VoxelUtils.VoxelSize;
        float3 _position = math.float3(position);
        return new Vector3(_position.x, _position.y, _position.z) * scaling;
    }

    // Calculate the scaling factor that must be applied to the chunks
    public float ScalingFactor()
    {
        return (math.pow(2.0F, (float)(maxDepth - depth)));
    }

    // Calculate the world size of the octree node
    public Vector3 WorldSize()
    {
        float scaling = (float)VoxelUtils.Size * VoxelUtils.VoxelSize;
        return new Vector3((float)size * scaling, (float)size * scaling, (float)size * scaling);
    }

    public bool Equals(OctreeNode other)
    {
        return math.all(this.position == other.position) &&
            this.depth == other.depth &&
            this.size == other.size &&
            this.leaf == other.leaf;
    }

    // https://forum.unity.com/threads/burst-error-bc1091-external-and-internal-calls-are-not-allowed-inside-static-constructors.1347293/
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + position.GetHashCode();
            hash = hash * 23 + depth.GetHashCode();
            hash = hash * 23 + leaf.GetHashCode();
            hash = hash * 23 + size.GetHashCode();
            return hash;
        }
    }

    // Check if we can subdivide this node (and also if we should generate collisions)
    public bool ShouldSubdivide(ref NativeArray<OctreeTarget> targets, ref NativeArray<float> qualityPoints, out bool generateCollisions)
    {
        bool subdivide = false;
        generateCollisions = false;

        foreach (var target in targets)
        {
            float3 minBounds = math.float3(position);
            float3 maxBounds = math.float3(position) + math.float3(size);
            float3 clamped = math.clamp(target.center, minBounds, maxBounds);

            bool local = math.distance(clamped, target.center) < target.radius * ScalingFactor() * qualityPoints[depth];
            subdivide |= local;
            generateCollisions |= local && target.generateCollisions;
        }

        return subdivide;
    }

    // Try to subdivide the current node into 8 octants
    public void TrySubdivide(ref NativeArray<OctreeTarget> targets, ref NativeHashSet<OctreeNode> nodes, ref NativeQueue<OctreeNode> pending, ref NativeArray<float> qualityPoints)
    {
        if (ShouldSubdivide(ref targets, ref qualityPoints, out bool generateChildrenCollisions) && depth < maxDepth)
        {
            for (int i = 0; i < 8; i++)
            {
                int3 offset = offsets[i];
                OctreeNode node = new OctreeNode
                {
                    position = offset * (size / 2) + this.position,
                    depth = depth + 1,
                    size = size / 2,
                    maxDepth = maxDepth,
                    generateCollisions = generateChildrenCollisions && depth == (maxDepth-1),
                };

                pending.Enqueue(node);
            }
            leaf = false;
            //nodes[center] = this;
        } else
        {
            leaf = true;
            //nodes[center] = this;
        }

        nodes.Add(this);
    }
}

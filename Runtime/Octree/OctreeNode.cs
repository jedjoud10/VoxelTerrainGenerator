using System;
using Unity.Mathematics;

// A singular octree node stored within the octree
// Stored as a struct for performance reasons and to be able to use it within jobs
public struct OctreeNode : IEquatable<OctreeNode> {
    // Invalid type octree node
    public static OctreeNode Invalid = new OctreeNode {
        parentIndex = -1,
        index = -1,
        depth = -1,
        position = float3.zero,
        size = 0,
        ChildBaseIndex = -1,
        Skirts = -1,
    };

    // Start position (0, 0, 0) of the octree node
    public float3 position { get; internal set; }

    // Inverse Depth of the node starting from maxDepth
    public int depth { get; internal set; }

    // The full size of the node
    public float size { get; internal set; }

    // Index of the current node inside the array
    public int index { get; internal set; }

    // Index of the parent node (-1 if root)
    public int parentIndex { get; internal set; }

    // Index of the children nodes
    public int ChildBaseIndex { get; internal set; }

    // Center of the node
    public float3 Center => math.float3(position) + math.float3(size) / 2.0F;

    // Scaling factor applied to chunks 
    public float ScalingFactor { get; internal set; }

    // Directions in which skirts should be enabled
    // First 3 bits depict the "base" skirts (x = 0, etc...)
    // Next 3 bits depict the "end" skirts (x = size - 2, etc...)
    public int Skirts { get; internal set; }

    // Create the root node
    public static OctreeNode RootNode(int maxDepth) {
        float size = (int)(math.pow(2.0F, (float)(maxDepth))) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor;
        OctreeNode node = new OctreeNode();
        node.position = -math.int3(size / 2);
        node.depth = 0;
        node.size = size;
        node.index = 0;
        node.parentIndex = -1;
        node.Skirts = 0;
        node.ChildBaseIndex = 1;
        node.ScalingFactor = math.pow(2.0f, (float)(maxDepth));
        return node;
    }

    public bool Equals(OctreeNode other) {
        return math.all(this.position == other.position) &&
            this.depth == other.depth &&
            this.size == other.size &&
            this.Skirts == other.Skirts &&
            (this.ChildBaseIndex == -1) == (other.ChildBaseIndex == -1);
    }

    // https://forum.unity.com/threads/burst-error-bc1091-external-and-internal-calls-are-not-allowed-inside-static-constructors.1347293/
    public override int GetHashCode() {
        unchecked {
            int hash = 17;
            hash = hash * 23 + position.GetHashCode();
            hash = hash * 23 + depth.GetHashCode();
            hash = hash * 23 + ChildBaseIndex.GetHashCode();
            hash = hash * 23 + size.GetHashCode();
            hash = hash * 23 + Skirts.GetHashCode();
            return hash;
        }
    }

    // Check if this node intersects the given AABB
    public bool IntersectsAABB(float3 min, float3 max) {
        float3 nodeMin = math.float3(position);
        float3 nodeMax = math.float3(position) + math.float3(size);
        return math.all(min <= nodeMax) && math.all(nodeMin <= max);
    }

    // Check if this node contains a point
    internal bool ContainsPoint(float3 point) {
        float3 nodeMin = math.float3(position);
        float3 nodeMax = math.float3(position) + math.float3(size);
        return math.all(nodeMin <= point) && math.all(point <= nodeMax);
    }
}

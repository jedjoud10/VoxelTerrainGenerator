using System;
using Unity.Mathematics;
using UnityEngine;

// Heavily simplified octree node for voxel edits
public struct VoxelEditOctreeNode : IEquatable<VoxelEditOctreeNode> {
    // Start position (0, 0, 0) of the octree node
    public float3 Position { get; internal set; }

    // Inverse Depth of the node starting from maxDepth
    public int Depth { get; internal set; }

    // The full size of the node
    public float Size { get; internal set; }

    // Is the node a parent?
    public bool Parent { get; internal set; }

    // Center of the node
    public float3 Center => math.float3(Position) + math.float3(Size) / 2.0F;

    // Scaling factor applied to chunks 
    public float ScalingFactor { get; internal set; }

    // Bounds of the octree node
    public Bounds Bounds { get => new Bounds { min = Position, max = Position + Size }; }

    // Create the root node
    public static VoxelEditOctreeNode RootNode(int maxDepth) {
        float size = (int)(math.pow(2.0F, (float)(maxDepth))) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor;
        VoxelEditOctreeNode node = new VoxelEditOctreeNode();
        node.Position = -math.int3(size / 2);
        node.Depth = 0;
        node.Size = size;
        node.Parent = false;
        node.ScalingFactor = math.pow(2.0f, (float)(maxDepth));
        return node;
    }

    public bool Equals(VoxelEditOctreeNode other) {
        return math.all(this.Position == other.Position) &&
            this.Depth == other.Depth &&
            this.Size == other.Size;
    }

    // https://forum.unity.com/threads/burst-error-bc1091-external-and-internal-calls-are-not-allowed-inside-static-constructors.1347293/
    public override int GetHashCode() {
        unchecked {
            int hash = 17;
            hash = hash * 23 + Position.GetHashCode();
            hash = hash * 23 + Depth.GetHashCode();
            hash = hash * 23 + Size.GetHashCode();
            return hash;
        }
    }
}

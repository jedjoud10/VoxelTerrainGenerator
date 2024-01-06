using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// Heavily simplified octree node for voxel edits
internal struct VoxelEditOctreeNode : IEquatable<VoxelEditOctreeNode>, IEquatable<OctreeNode>, INetworkSerializeByMemcpy {
    internal struct RawNode : IEquatable<RawNode>, INetworkSerializeByMemcpy {
        public float3 position;
        public int depth;
        public float size;

        public bool Equals(RawNode other) {
            return math.all(this.position == other.position) &&
                this.depth == other.depth &&
                this.size == other.size;
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 23 + position.GetHashCode();
                hash = hash * 23 + depth.GetHashCode();
                hash = hash * 23 + size.GetHashCode();
                return hash;
            }
        }
    }

    public float3 position;
    public int depth;
    public float size;
    public int index;
    public int childBaseIndex;
    public int lookup;
    public float scalingFactor;
    public float3 Center => math.float3(position) + math.float3(size) / 2.0F;
    public Bounds Bounds { get => new Bounds { min = position, max = position + size }; }
    public static VoxelEditOctreeNode RootNode(int maxDepth) {
        float size = (int)(math.pow(2.0F, (float)(maxDepth))) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor;
        VoxelEditOctreeNode node = new VoxelEditOctreeNode();
        node.position = -math.int3(size / 2);
        node.depth = 0;
        node.size = size;
        node.childBaseIndex = -1;
        node.lookup = -1;
        node.scalingFactor = math.pow(2.0f, (float)(maxDepth));
        return node;
    }

    public bool Equals(VoxelEditOctreeNode other) {
        return math.all(this.position == other.position) &&
            this.depth == other.depth &&
            this.size == other.size;
    }

    public bool Equals(OctreeNode other) {
        return math.all(this.position == other.position) &&
            this.depth == other.depth &&
            this.size == other.size &&
            (this.childBaseIndex == -1) == (other.ChildBaseIndex == -1);
    }

    // https://forum.unity.com/threads/burst-error-bc1091-external-and-internal-calls-are-not-allowed-inside-static-constructors.1347293/
    public override int GetHashCode() {
        unchecked {
            int hash = 17;
            hash = hash * 23 + position.GetHashCode();
            hash = hash * 23 + depth.GetHashCode();
            hash = hash * 23 + size.GetHashCode();
            return hash;
        }
    }
}


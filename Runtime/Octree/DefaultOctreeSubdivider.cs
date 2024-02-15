using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Default implementation of the octree subdivider
[assembly: RegisterGenericJobType(typeof(SubdivideJob<DefaultOctreeSubdivider>))]
public struct DefaultOctreeSubdivider : IOctreeSubdivider {
    public float propSegmentWorldSize;
    public float2 yPositionBounds;

    public JobHandle Apply(TerrainLoader.Target target, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending) {
        return IOctreeSubdivider.ApplyGeneric(target, nodes, pending, this);
    }

    public bool ShouldSubdivide(ref OctreeNode node, ref TerrainLoader.Target target) {
        float3 minBounds = math.float3(node.position);
        float3 maxBounds = math.float3(node.position) + math.float3(node.size);
        bool intersects = maxBounds.y > yPositionBounds.x && minBounds.y < yPositionBounds.y;

        float3 clamped = math.clamp(target.center, minBounds, maxBounds);
        bool subdivide = math.distance(clamped, target.center) < target.radius * node.scalingFactor;

        return subdivide && intersects;
    }
}
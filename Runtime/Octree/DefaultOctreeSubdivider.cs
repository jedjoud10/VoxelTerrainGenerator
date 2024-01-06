using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Default implementation of the octree subdivider
[assembly: RegisterGenericJobType(typeof(SubdivideJob<DefaultOctreeSubdivider>))]
public struct DefaultOctreeSubdivider : IOctreeSubdivider {
    public JobHandle Apply(NativeArray<OctreeTarget> targets, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending) {
        return IOctreeSubdivider.ApplyGeneric(targets, nodes, pending, this);
    }

    public bool ShouldSubdivide(ref OctreeNode node, ref NativeArray<OctreeTarget> targets) {
        bool subdivide = false;

        foreach (var target in targets) {
            float3 minBounds = math.float3(node.Position);
            float3 maxBounds = math.float3(node.Position) + math.float3(node.Size);
            float3 clamped = math.clamp(target.center, minBounds, maxBounds);
            bool local = math.distance(clamped, target.center) < target.radius * node.ScalingFactor;
            subdivide |= local;
        }

        return subdivide;
    }
}
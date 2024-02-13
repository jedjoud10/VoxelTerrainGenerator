using Unity.Collections;
using Unity.Jobs;
using System.Runtime.CompilerServices;

// Custom octree subdivier to allow end users to handle custom octree subdivision logic
public interface IOctreeSubdivider {
    // Should we subdivide the given node?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldSubdivide(ref OctreeNode node, ref TerrainLoader.Target target);

    // MUST CALL THE "ApplyGeneric" function because we can't hide away generics
    public JobHandle Apply(TerrainLoader.Target target, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending);

    // Apply any generic octree subdivider edit onto the octree
    internal static JobHandle ApplyGeneric<T>(TerrainLoader.Target target, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending, T subdivider) where T : struct, IOctreeSubdivider {
        SubdivideJob<T> job = new SubdivideJob<T> {
            target = target,
            nodes = nodes,
            pending = pending,
            maxDepth = VoxelUtils.MaxDepth,
            subdivider = subdivider,
        };
        return job.Schedule();
    }
}
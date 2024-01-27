using Unity.Collections;
using Unity.Jobs;
using System.Runtime.CompilerServices;

// Custom octree subdivier to allow end users to handle custom octree subdivision logic
public interface IOctreeSubdivider {
    // Should we subdivide the given node?
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldSubdivide(ref OctreeNode node, ref NativeArray<TerrainLoaderTarget> targets);

    // MUST CALL THE "ApplyGeneric" function because we can't hide away generics
    public JobHandle Apply(NativeArray<TerrainLoaderTarget> targets, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending);

    // Apply any generic octree subdivider edit onto the octree
    internal static JobHandle ApplyGeneric<T>(NativeArray<TerrainLoaderTarget> targets, NativeList<OctreeNode> nodes, NativeQueue<OctreeNode> pending, T subdivider) where T : struct, IOctreeSubdivider {
        SubdivideJob<T> job = new SubdivideJob<T> {
            targets = targets,
            nodes = nodes,
            pending = pending,
            maxDepth = VoxelUtils.MaxDepth,
            subdivider = subdivider,
        };
        return job.Schedule();
    }
}
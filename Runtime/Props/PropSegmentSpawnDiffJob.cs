using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

// This will handle spawning the prop segments from scratch and to be diffed later
[BurstCompile(CompileSynchronously = true)]
public struct PropSegmentSpawnDiffJob : IJob {
    public NativeHashSet<int4> oldPropSegments;
    public NativeHashSet<int4> propSegments;
    public TerrainLoaderTarget target;

    [WriteOnly]
    public NativeList<int4> addedSegments;

    [WriteOnly]
    public NativeList<int4> removedSegments;

    public int maxSegmentsInWorld;

    public float propSegmentSize;

    public void Execute() {
        propSegments.Clear();

        // TODO: Implement multi target support
        int3 c = (int3)target.propSegmentExtent;
        int3 min = new int3(-maxSegmentsInWorld, -maxSegmentsInWorld, -maxSegmentsInWorld);
        int3 max = new int3(maxSegmentsInWorld, maxSegmentsInWorld, maxSegmentsInWorld);

        int3 offset = (int3)math.round(target.center / propSegmentSize);

        for (int x = -c.x; x < c.x; x++) {
            for (int y = -c.y; y < c.y; y++) {
                for (int z = -c.z; z < c.z; z++) {
                    int3 segment = new int3(x, y, z) + offset;

                    float3 segmentPos = new float3(segment.x, segment.y, segment.z) * propSegmentSize + new float3(1, 1, 1) * (propSegmentSize / 2.0f);
                    float distance = math.distance(target.center, segmentPos) / propSegmentSize;

                    int lod = (int)math.round(distance / math.max(target.propSegmentLodMultiplier, 0.01));
                    lod = math.clamp(lod, 0, 1);

                    if (math.all(segment >= min) && math.all(segment <= max)) {
                        propSegments.Add(new int4(segment, lod));
                    }
                }
            }
        }

        addedSegments.Clear();
        removedSegments.Clear();

        foreach (var item in propSegments) {
            if (!oldPropSegments.Contains(item)) {
                addedSegments.Add(item);
            }
        }

        foreach (var item in oldPropSegments) {
            if (!propSegments.Contains(item)) {
                // multiple duplicates for each level to make sure there are no "zombie" segments that are left
                // only have to worry about this when using multiple targets

                removedSegments.Add(item);
            }
        }

        oldPropSegments.Clear();

        foreach (var item in propSegments) {
            oldPropSegments.Add(item);
        }
    }
}
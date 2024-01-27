using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using static UnityEngine.Rendering.HableCurve;

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

    public float propSegmentSize;

    public void Execute() {
        propSegments.Clear();

        // TODO: Implement multi target support
        int3 c = (int3)target.propSegmentExtent;

        int3 offset = (int3)math.round(target.center / propSegmentSize);

        for (int x = -c.x; x < c.x; x++) {
            for (int y = -c.y; y < c.y; y++) {
                for (int z = -c.z; z < c.z; z++) {
                    int3 segment = new int3(x, y, z) + offset;

                    float3 segmentPos = new float3(segment.x, segment.y, segment.z) * propSegmentSize + new float3(1, 1, 1) * (propSegmentSize / 2.0f);
                    float distance = math.distance(target.center, segmentPos) / propSegmentSize;

                    int lod = (int)math.round(distance * target.propSegmentLodMultiplier);
                    lod = math.clamp(lod, 0, 2);
                    propSegments.Add(new int4(segment, lod));
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


                removedSegments.Add(new int4(item.xyz, -1));
                removedSegments.Add(new int4(item.xyz, 0));
                removedSegments.Add(new int4(item.xyz, 1));
                removedSegments.Add(new int4(item.xyz, 2));
            }
        }

        oldPropSegments.Clear();

        foreach (var item in propSegments) {
            oldPropSegments.Add(item);
        }

        /*
         * 
         *     int minLod = 2;
    Vector3 center = segment.transform.position + Vector3.one * VoxelUtils.PropSegmentSize / 2.0f;

    foreach (var target in targets) {
        float distance = Vector3.Distance(target.transform.position, center) / VoxelUtils.PropSegmentSize;
        int lod = 2;

        if (distance < target.propSegmentPrefabSpawnerMultiplier) {
            lod = 0;
        } else if (distance < target.propSegmentInstancedRendererLodMultiplier) {
            lod = 1;
        }

        minLod = Mathf.Min(lod, minLod);
    }*/

    }
}
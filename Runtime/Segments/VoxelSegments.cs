using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Used for generating the main voxel regions that will be used for prop generation and structure generation
// Structure generation has to happen before prop generation, so we run that as its own stepSeS
public class VoxelSegments : VoxelBehaviour {
    // Toggles for debugging
    public bool debugGizmos = false;

    // Prop resolution per segment
    [Range(4, 32)]
    public int propSegmentResolution = 32;

    // How many voxel chunks fit in a prop segment
    [Range(1, 64)]
    public int voxelChunksInPropSegment = 8;

    // Max number of active segments possible at any given time
    [Min(128)]
    public int maxSegments = 512;

    // Max number of segments that we can unload / remove
    [Min(128)]
    public int maxSegmentsToRemove = 512;

    // Prop segment management and diffing
    private NativeHashSet<int4> propSegments;
    private NativeHashSet<int4> oldPropSegments;
    private NativeList<int4> addedSegments;
    private NativeList<int4> removedSegments;

    // Prop segment classes bounded to their positions
    internal Dictionary<int4, Segment> propSegmentsDict;
        
    // When we load in a prop segment
    public delegate void PropSegmentLoaded(Segment segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // Called right before we unload prop segments that must be destroyed. 
    public delegate void PropSegmentsPreRemoval(ref NativeList<int4> removedSegments);
    public event PropSegmentsPreRemoval onPropSegmetsPreRemoval;

    // When the fixed region should be hidden and everything that it owns should be destroyed
    public delegate void PropSegmentUnloaded(Segment segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Called whenever we need to serialize the data for a specific prop segment (before it gets deleted)
    public delegate void PropSegmentsWillBeRemoved(int4 segment);
    public event PropSegmentsWillBeRemoved onSerializePropSegment;

    internal Queue<Segment> pendingSegments;
    internal bool segmentsAwaitingRemoval;
    internal bool mustUpdate = false;
    
    // Checks if we completed segment generation
    public bool Free {
        get {
            return !mustUpdate && pendingSegments.Count == 0;
        }
    }

    private void OnValidate() {
        if (terrain == null) {
            propSegmentResolution = Mathf.ClosestPowerOfTwo(propSegmentResolution);
            voxelChunksInPropSegment = Mathf.ClosestPowerOfTwo(voxelChunksInPropSegment);
            VoxelUtils.PropSegmentResolution = propSegmentResolution;
            VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        }
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        propSegmentResolution = Mathf.ClosestPowerOfTwo(propSegmentResolution);
        voxelChunksInPropSegment = Mathf.ClosestPowerOfTwo(voxelChunksInPropSegment);
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        VoxelUtils.MaxSegmentsToRemove = maxSegmentsToRemove;
        VoxelUtils.MaxSegments = maxSegments;
        segmentsAwaitingRemoval = false;

        // Stuff used for management and addition/removal detection of segments
        propSegmentsDict = new Dictionary<int4, Segment>();
        propSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        oldPropSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        addedSegments = new NativeList<int4>(Allocator.Persistent);
        removedSegments = new NativeList<int4>(Allocator.Persistent);
        pendingSegments = new Queue<Segment>();
    }

    // Updates the prop segments LOD and renders instanced/billboarded instances for props
    private void Update() {
        mustUpdate |= terrain.VoxelOctree.mustUpdate;

        if (terrain.VoxelOctree.target == null)
            return;

        // Since async readback currently uses one pending buffer, we have to always wait until it is done
        // TODO: Implement proper circular buffers
        if (!terrain.VoxelProps.Free)
            return;

        // Only update if we can and if we finished generating
        if (mustUpdate && pendingSegments.Count == 0 && !segmentsAwaitingRemoval) {
            SegmentSpawnJob job = new SegmentSpawnJob {
                addedSegments = addedSegments,
                removedSegments = removedSegments,
                oldPropSegments = oldPropSegments,
                propSegments = propSegments,
                target = terrain.VoxelOctree.target.data,
                maxSegmentsInWorld = VoxelUtils.PropSegmentsCount / 2,
                propSegmentSize = VoxelUtils.PropSegmentSize,
            };

            job.Schedule().Complete();

            for (int i = 0; i < addedSegments.Length; i++) {
                var segment = new Segment();
                var pos = addedSegments[i];
                segment.worldPosition = new Vector3(pos.x, pos.y, pos.z) * VoxelUtils.PropSegmentSize;
                segment.regionPosition = pos.xyz;
                segment.spawnPrefabs = pos.w == 0;
                segment.indexRangeLookup = -1;
                segment.props = new Dictionary<int, (List<GameObject>, List<ushort>)>();
                propSegmentsDict.Add(pos, segment);
                pendingSegments.Enqueue(segment);
            }

            mustUpdate = false;
            segmentsAwaitingRemoval = removedSegments.Length > 0;

            // Serialize the data for the prop segments very early on
            foreach (var removed in removedSegments) {
                onSerializePropSegment?.Invoke(removed);
            }
        }

        // When we finished generating all pending segments delete the ones that are pending removal
        if (pendingSegments.Count == 0 && segmentsAwaitingRemoval && terrain.VoxelProps.Free) {
            segmentsAwaitingRemoval = false;
            onPropSegmetsPreRemoval?.Invoke(ref removedSegments);
            
            for (int i = 0; i < removedSegments.Length; i++) {
                var pos = removedSegments[i];
                if (propSegmentsDict.Remove(pos, out Segment val)) {
                    onPropSegmentUnloaded?.Invoke(val);
                }
            }
        }
        
        // Start generating the first pending segment we find
        Segment result;
        if (pendingSegments.TryPeek(out result)) {
            pendingSegments.Dequeue();
            onPropSegmentLoaded.Invoke(result);
        }
    }

    // Unload all props and enqueue them, basically regenerating them
    public void RegenerateRegions() {
        // WARNING: This causes GC.Collect spikes since we set a bunch of stuff null so it collects them automatically
        // what we should do instead is only regenerate the chunks that have been modified instead
        foreach (var item in propSegmentsDict) {
            onPropSegmentUnloaded?.Invoke(item.Value);
            pendingSegments.Enqueue(item.Value);
        }

        terrain.VoxelProps.ResetPropData();
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            foreach (var item in propSegmentsDict) {
                var key = item.Key.xyz;
                Vector3 center = new Vector3(key.x, key.y, key.z) * size + Vector3.one * size / 2.0f;

                if (item.Value.spawnPrefabs) {
                    Gizmos.color = Color.green;
                } else {
                    Gizmos.color = Color.red;
                }

                Gizmos.DrawWireCube(center, Vector3.one * size);
            }
        }
    }

    internal override void Dispose() {
        propSegments.Dispose();
        oldPropSegments.Dispose();
        addedSegments.Dispose();
        removedSegments.Dispose();
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Handles keeping track of voxel edits and dynamic edits in the world
public class VoxelEdits : VoxelBehaviour {
    // Max number of voxel jobs we will execute per frame
    [Range(1, 8)]
    public int voxelEditsJobsPerFrame = 1;
    public bool Free { get; private set; } = true;
    public bool debugGizmos = false;

    // Dictionary to map chunk positions to sparseVoxelData indices
    // Contains a bitmask telling us what chunks of a specific segment is enabled
    internal NativeArray<VoxelDeltaLookup> lookup;

    // All the chunks the user has modified in each LOD level
    internal UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    // Stores the containers of the different types of world edits
    internal WorldEditTypeRegistry worldEditRegistry;

    // Temporary place for voxel edits that have not been applied yet
    internal Queue<IVoxelEdit> tempVoxelEdits;

    // Used to register custom dynamic edit types
    public delegate void RegisterDynamicEditType(WorldEditTypeRegistry registry);
    public event RegisterDynamicEditType registerDynamicEditTypes;

    // Initialize the voxel edits handler
    internal override void Init() {
        lookup = new NativeArray<VoxelDeltaLookup>(VoxelUtils.MaxSegmentsVolume, Allocator.Persistent);
        
        for (int i = 0; i < VoxelUtils.MaxSegments; i++) {
            lookup[i] = new VoxelDeltaLookup {
                startingIndex = -1,
                bitset = new BitField64(),
            };
        }

        sparseVoxelData = new UnsafeList<SparseVoxelDeltaData>(0, Allocator.Persistent);
        worldEditRegistry = new WorldEditTypeRegistry();
        tempVoxelEdits = new Queue<IVoxelEdit>();

        // Register common dynamic edit types
        registerDynamicEditTypes += (WorldEditTypeRegistry registry) => {
            registry.Register<SphereWorldEdit>();
            registry.Register<CuboidWorldEdit>();
        };

        // Register custom dynamic edit types
        registerDynamicEditTypes?.Invoke(worldEditRegistry);
    }

    // Dispose of any memory
    internal override void Dispose() {
        for (int i = 0; i < lookup.Length; i++) {
            /*
            UnsafeBitArray bitset = lookup[i].bitset;
            if (bitset.IsCreated && !bitset.IsEmpty) {
                for (int j = 0; j < VoxelUtils.ChunksPerSegmentVolume; j++) {
                    if (bitset.IsSet(j)) {
                        SparseVoxelDeltaData data = sparseVoxelData[j];
                        //data.materials.Dispose();
                        //data.densities.Dispose();
                    }
                }
            }
            */

        }
        //sparseVoxelData.Dispose();
        lookup.Dispose();
    }

    private void Update() {
        IVoxelEdit edit;
        if (tempVoxelEdits.TryDequeue(out edit)) {
            ApplyVoxelEdit(edit, true);
        }

        Free = tempVoxelEdits.Count == 0 && terrain.VoxelMesher.Free;
    }

    // Apply a voxel edit to the terrain world
    public void ApplyVoxelEdit(IVoxelEdit edit, bool neverForget = false) {
        if ((!terrain.VoxelOctree.Free || !terrain.VoxelMesher.Free)) {
            if (neverForget)
                tempVoxelEdits.Enqueue(edit);
            return;
        }

        // Custom job to find all the octree nodes that touch the bounds
        Bounds bound = edit.GetBounds();
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bound, out temp);

        // Sparse voxel chunks that we must edit
        List<SparseVoxelDeltaChunk> sparseVoxelEditChunks = InitSegmentsFindSparseChunks(bound);

        // Modify sparse voxel data
        foreach (var item in sparseVoxelEditChunks) {
            SparseVoxelDeltaData data = sparseVoxelData[item.listIndex];
            JobHandle handle = edit.Apply(data, item.position);
            handle.Complete();
        }

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            chunk.Remesh(terrain);
        }
        temp.Value.Dispose();
    }

    // Apply a world edit to the terrain world immediately
    // The returned int is the index inside the registry for the world edit
    public int ApplyWorldEdit<T>(T worldEdit) where T: struct, IWorldEdit {
        // Custom job to find all the octree nodes that touch the bounds
        Bounds bound = worldEdit.GetBounds();
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bound, out temp);

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            chunk.Remesh(terrain);
        }
        temp.Value.Dispose();

        // We can add this later since the mesher isn't immediate; it will mesh next frame
        return worldEditRegistry.Add(worldEdit);
    }

    // Makes sure the segments that intersect the bounds are loaded in and ready for modification
    // This will also return the sparse voxel delta chunks that have been initialized and that must be written to
    private List<SparseVoxelDeltaChunk> InitSegmentsFindSparseChunks(Bounds bounds) {
        List<SparseVoxelDeltaChunk> sparseVoxelEditChunks = new List<SparseVoxelDeltaChunk>();
        int3 offset = -VoxelUtils.MaxSegments / 2;

        for (int x = 0; x < VoxelUtils.MaxSegments; x++) {
            for (int y = 0; y < VoxelUtils.MaxSegments; y++) {
                for (int z = 0; z < VoxelUtils.MaxSegments; z++) {
                    int3 segmentCoords = math.int3(x, y, z);
                    int3 worldSegmentCoords = segmentCoords + offset;
                    if (VoxelUtils.SegmentCoordsIntersectBounds(worldSegmentCoords, bounds)) {
                        InitSegmentFindSparseChunks(bounds, worldSegmentCoords, ref sparseVoxelEditChunks);
                    }
                }
            }
        }

        return sparseVoxelEditChunks;
    }

    // Makes sure the chunks that intersects the bounds (for this segment) are ready for editing
    private void InitSegmentFindSparseChunks(Bounds bounds, int3 worldSegmentCoords, ref List<SparseVoxelDeltaChunk> sparseVoxelEditChunks) {
        uint3 uintSegmentCoords = math.uint3(worldSegmentCoords + VoxelUtils.MaxSegments / 2);
        int segmentIndex = VoxelUtils.PosToIndex(uintSegmentCoords, (uint)VoxelUtils.MaxSegments);
        VoxelDeltaLookup segment = lookup[segmentIndex];

        // This will initialize the segment if it does not contain any chunks
        if (segment.startingIndex == -1) {
            segment.bitset = new BitField64();
            segment.startingIndex = sparseVoxelData.Length;

            for (int i = 0; i < VoxelUtils.ChunksPerSegmentVolume; i++) {
                sparseVoxelData.Add(SparseVoxelDeltaData.Empty);
            }
        }

        // This loop will create the memory allocations for edited chunks
        for (int i = 0; i < VoxelUtils.ChunksPerSegmentVolume; i++) {
            int3 localChunkCoords = math.int3(VoxelUtils.IndexToPos(i, (uint)VoxelUtils.ChunksPerSegment));
            int3 globalChunkCoords = worldSegmentCoords * VoxelUtils.ChunksPerSegment + localChunkCoords;

            // Check if the chunk intersects the given input bounds
            if (VoxelUtils.ChunkCoordsIntersectBounds(globalChunkCoords, bounds)) {
                // Initialize the SparseVoxelDeltaData chunk if it was not already initialized
                if (!segment.bitset.IsSet(i)) {
                    segment.bitset.SetBits(i, true);

                    SparseVoxelDeltaData data = new SparseVoxelDeltaData {
                        densities = new UnsafeList<half>(VoxelUtils.Volume, Allocator.Persistent),
                        materials = new UnsafeList<ushort>(VoxelUtils.Volume, Allocator.Persistent),
                    };

                    data.densities.AddReplicate(half.zero, VoxelUtils.Volume);
                    data.materials.AddReplicate(ushort.MaxValue, VoxelUtils.Volume);

                    sparseVoxelData[segment.startingIndex + i] = data;
                }

                sparseVoxelEditChunks.Add(new SparseVoxelDeltaChunk {
                    position = globalChunkCoords,
                    listIndex = segment.startingIndex + i,
                });
            }
        }

        lookup[segmentIndex] = segment;
    }

    // Check if a chunk contains voxel edits
    public bool IsChunkAffectedByVoxelEdits(VoxelChunk chunk) {
        int3 worldSegment = (int3)math.floor(chunk.node.Position / VoxelUtils.SegmentSize);
        uint3 unsignedWorldSegment = math.uint3(worldSegment + math.int3(VoxelUtils.MaxSegments / 2));
        int segmentIndex = VoxelUtils.PosToIndex(unsignedWorldSegment, (uint)VoxelUtils.MaxSegments);
        return lookup[segmentIndex].startingIndex != -1;
    }
    
    // Check if a chunk contains dynamic edits
    public bool IsChunkAffectedByDynamicEdits(VoxelChunk chunk) {
        return true;
        //return dynamicEdits.Any(dynEdit => dynEdit.GetBounds().Intersects(chunk.GetBounds()));
    }


    // Create an apply job dependeny for a chunk that has voxel edits
    public JobHandle TryGetApplyVoxelEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dependency) {
        if (!IsChunkAffectedByVoxelEdits(chunk)) {
            return dependency;
        }

        VoxelEditApplyJob job = new VoxelEditApplyJob {
            lookup = lookup,
            sparseVoxelData = sparseVoxelData,
            voxels = voxels,
            node = chunk.node,
            chunksPerSegment = VoxelUtils.ChunksPerSegment,
            segmentSize = VoxelUtils.SegmentSize,
            maxSegments = VoxelUtils.MaxSegments,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            voxelScale = VoxelUtils.VoxelSizeFactor,
        };
        return job.Schedule(VoxelUtils.Volume, 2048, dependency);
    }

    // Create a list of dependencies to apply to chunks that have been affected by dynamic edits
    // Applied BEFORE the voxel edits
    public JobHandle TryGetApplyDynamicEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels) {
        if (!IsChunkAffectedByDynamicEdits(chunk)) {
            return new JobHandle();
        }

        JobHandle dep = new JobHandle();
        foreach (var registry in worldEditRegistry.types) {
            foreach (var worldEdit in registry.List) {
                IWorldEdit edit = (IWorldEdit)worldEdit;
                dep = edit.Apply(chunk, ref voxels, dep);
            }
        }

        return dep;
    }

    private void OnDrawGizmosSelected() {
        if (!lookup.IsCreated || !debugGizmos)
            return;

        for (int i = 0; i < lookup.Length; i++) {
            Gizmos.color = new Color(1f, 1f, 1f, 1f);
            uint3 segmentCoordsUint = VoxelUtils.IndexToPos(i, (uint)VoxelUtils.MaxSegments);
            int3 segmentCoords = math.int3(segmentCoordsUint) - math.int3(VoxelUtils.MaxSegments / 2);

            VoxelDeltaLookup segment = lookup[i];

            if (segment.startingIndex == -1)
                continue;

            var offset = (float)VoxelUtils.SegmentSize;
            Vector3 segmentCenter = new Vector3(segmentCoords.x, segmentCoords.y, segmentCoords.z) * VoxelUtils.SegmentSize + Vector3.one * offset / 2F;
            Gizmos.DrawWireCube(segmentCenter * VoxelUtils.VoxelSizeFactor, Vector3.one * offset * VoxelUtils.VoxelSizeFactor);
            Vector3 offsetTwoIdfk = new Vector3(segmentCoords.x, segmentCoords.y, segmentCoords.z) * VoxelUtils.SegmentSize * VoxelUtils.VoxelSizeFactor;

            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            float size = VoxelUtils.VoxelSizeFactor * VoxelUtils.Size;
            for (int k = 0; k < VoxelUtils.ChunksPerSegmentVolume; k++) {
                if (!segment.bitset.IsSet(k))
                    continue;

                uint3 pos = VoxelUtils.IndexToPos(k, (uint)VoxelUtils.ChunksPerSegment);
                Vector3 pos2 = new Vector3(pos.x, pos.y, pos.z);
                Vector3 chunkOffset = Vector3.one * size * 0.5f;
                Vector3 chunkSize = new Vector3(size, size, size);
                Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawWireCube(pos2 * size + offsetTwoIdfk + chunkOffset, chunkSize);
            }
        }
    }
}

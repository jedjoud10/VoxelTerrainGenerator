using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits and dynamic edits in the world
public class VoxelEdits : VoxelBehaviour {
    // Max number of voxel jobs we will execute per frame
    [Range(1, 8)]
    public int voxelEditsJobsPerFrame = 1;
    public bool Free { get; private set; } = true;
    public bool debugGizmos = false;

    // Chunk nodes to sparse voxel data indices DIRECTLY
    internal NativeHashMap<OctreeNode, int> chunkLookup;

    // Voxel edit octree nodes
    internal NativeList<VoxelEditOctreeNode> nodes;

    // Dictionary to map octree nodes to sparseVoxelData indices
    internal NativeHashMap<int, int> lookup;

    // All the chunks the user has modified (different LODs as well)
    internal List<SparseVoxelDeltaData> sparseVoxelData;

    // Stores the containers of the different types of world edits
    internal WorldEditTypeRegistry worldEditRegistry;

    // Temporary place for voxel edits that have not been applied yet
    internal Queue<IVoxelEdit> tempVoxelEdits;

    // Used to register custom dynamic edit types
    public delegate void RegisterDynamicEditType(WorldEditTypeRegistry registry);
    public event RegisterDynamicEditType registerDynamicEditTypes;

    // Initialize the voxel edits handler
    internal override void Init() {
        chunkLookup = new NativeHashMap<OctreeNode, int>(0, Allocator.Persistent);
        nodes = new NativeList<VoxelEditOctreeNode>(Allocator.Persistent);
        nodes.Add(VoxelEditOctreeNode.RootNode(VoxelUtils.MaxDepth));

        lookup = new NativeHashMap<int, int>(0, Allocator.Persistent);
        sparseVoxelData = new List<SparseVoxelDeltaData>();
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
        foreach (var data in sparseVoxelData) {
            data.densities.Dispose();
            data.materials.Dispose();
        }

        chunkLookup.Dispose();
        nodes.Dispose();
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

        // Update voxel edits octree (run subdivision job on new bounds)
        NativeQueue<VoxelEditOctreeNode> pending = new NativeQueue<VoxelEditOctreeNode>(Allocator.TempJob);
        pending.Enqueue(nodes[0]);
        NativeList<int> chunksToUpdate = new NativeList<int>(Allocator.TempJob);
        NativeList<PosScale> addedNodes = new NativeList<PosScale>(Allocator.TempJob);

        VoxelEditSubdivisionJob subdivision = new VoxelEditSubdivisionJob {
            nodes = nodes,
            voxelEditBounds = edit.GetBounds(),
            maxDepth = VoxelUtils.MaxDepth,
            sparseVoxelCountOffset = sparseVoxelData.Count,
            lookup = lookup,
            chunkLookup = chunkLookup,
            addedNodes = addedNodes,
            pending = pending,
            chunksToUpdate = chunksToUpdate
        };
        subdivision.Schedule().Complete();

        foreach (var added in addedNodes) {
            SparseVoxelDeltaData data = new SparseVoxelDeltaData {
                position = added.position,
                scalingFactor = added.scalingFactor,
                densities = new NativeArray<half>(VoxelUtils.Volume, Allocator.Persistent),
                materials = new NativeArray<ushort>(VoxelUtils.Volume, Allocator.Persistent),
            };

            for (int i = 0; i < VoxelUtils.Volume; i++) {
                data.materials[i] = ushort.MaxValue;
            }

            sparseVoxelData.Add(data);
        }

        foreach (var item in chunksToUpdate) {
            SparseVoxelDeltaData data = sparseVoxelData[item];
            JobHandle handle = edit.Apply(data);
            handle.Complete();
        }

        // Custom job to find all the octree nodes that touch the bounds
        Bounds bound = edit.GetBounds();
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bound, out temp);

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            chunk.Remesh(terrain);
        }

        temp.Value.Dispose();
        pending.Dispose();
        chunksToUpdate.Dispose();
        addedNodes.Dispose();
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

    // Check if a chunk contains voxel edits
    public bool IsChunkAffectedByVoxelEdits(VoxelChunk chunk) {
        return false;
        //return chunkLookup.ContainsKey(chunk.node);
    }
    
    // Check if a chunk contains dynamic edits
    public bool IsChunkAffectedByDynamicEdits(VoxelChunk chunk) {
        Bounds chunkBounds = chunk.GetBounds();
        return worldEditRegistry.AllBounds().Any(bound => bound.Intersects(chunkBounds));
    }

    // Create an apply job dependeny for a chunk that has voxel edits
    public JobHandle TryGetApplyVoxelEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dependency) {
        if (!IsChunkAffectedByVoxelEdits(chunk)) {
            return dependency;
        }

        throw new NotImplementedException();
        /*
        int index = chunkLookup[chunk.node];
        SparseVoxelDeltaData data = sparseVoxelData[index];

        VoxelEditApplyJob job = new VoxelEditApplyJob {
            data = data,
            voxels = voxels,
        };
        return job.Schedule(VoxelUtils.Volume, 2048, dependency);
        */
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

        Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
        foreach (var item in nodes) {
            Gizmos.DrawWireCube(item.Center, item.size * Vector3.one);
        }

        /*
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
        */
    }
}

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

    // Voxel edit octree nodes
    internal NativeList<VoxelEditOctreeNode> nodes;

    // Chunk nodes to sparse voxel data indices DIRECTLY
    internal NativeHashMap<VoxelEditOctreeNode.RawNode, int> chunkLookup;

    // Dictionary to map octree nodes to sparseVoxelData indices
    internal NativeHashMap<int, int> lookup;

    // All the chunks the user has modified (different LODs as well)
    internal List<SparseVoxelDeltaData> sparseVoxelData;

    // Stores the containers of the different types of world edits
    internal SerializableRegistry worldEditRegistry;

    // Temporary place for voxel edits that have not been applied yet
    internal Queue<IVoxelEdit> tempVoxelEdits;

    // Used to register custom dynamic edit types
    public delegate void RegisterDynamicEditType(SerializableRegistry registry);
    public event RegisterDynamicEditType registerDynamicEditTypes;

    // Initialize the voxel edits handler
    internal override void Init() {
        chunkLookup = new NativeHashMap<VoxelEditOctreeNode.RawNode, int>(0, Allocator.Persistent);
        nodes = new NativeList<VoxelEditOctreeNode>(Allocator.Persistent);
        nodes.Add(VoxelEditOctreeNode.RootNode(VoxelUtils.MaxDepth));

        lookup = new NativeHashMap<int, int>(0, Allocator.Persistent);
        sparseVoxelData = new List<SparseVoxelDeltaData>();
        worldEditRegistry = new SerializableRegistry();
        tempVoxelEdits = new Queue<IVoxelEdit>();

        // Register common dynamic edit types
        registerDynamicEditTypes += (SerializableRegistry registry) => {
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
        NativeList<int> addedNodes = new NativeList<int>(Allocator.TempJob);

        var bounds = edit.GetBounds();
        bounds.Expand(4);

        VoxelEditSubdivisionJob subdivision = new VoxelEditSubdivisionJob {
            nodes = nodes,
            voxelEditBounds = bounds,
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
            VoxelEditOctreeNode node = nodes[added]; 

            SparseVoxelDeltaData data = new SparseVoxelDeltaData {
                position = node.position,
                scalingFactor = node.scalingFactor,
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
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bounds, out temp);

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
        VoxelEditOctreeNode.RawNode raw = new VoxelEditOctreeNode.RawNode {
            position = chunk.node.position,
            depth = chunk.node.depth,
            size = chunk.node.size,
        };

        return chunkLookup.ContainsKey(raw);
    }
    
    // Check if a chunk contains dynamic edits
    public bool IsChunkAffectedByDynamicEdits(VoxelChunk chunk) {
        Bounds chunkBounds = chunk.GetBounds();
        return worldEditRegistry.TryGetAll<IWorldEdit>().Select(x => x.GetBounds()).Any(bound => bound.Intersects(chunkBounds));
    }

    // Create an apply job dependeny for a chunk that has voxel edits
    public JobHandle TryGetApplyVoxelEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels, JobHandle dependency) {
        if (!IsChunkAffectedByVoxelEdits(chunk)) {
            return dependency;
        }

        VoxelEditOctreeNode.RawNode raw = new VoxelEditOctreeNode.RawNode {
            position = chunk.node.position,
            depth = chunk.node.depth,
            size = chunk.node.size,
        };

        int index = chunkLookup[raw];
        SparseVoxelDeltaData data = sparseVoxelData[index];

        VoxelEditApplyJob job = new VoxelEditApplyJob {
            data = data,
            voxels = voxels,
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

        foreach (var item in nodes) {
            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            Gizmos.DrawWireCube(item.Center, item.size * Vector3.one);
            
            if (item.lookup != -1) {
                Gizmos.color = new Color(1f, 0f, 0f, 1.0f);
                Gizmos.DrawWireCube(item.Center, item.size * Vector3.one * 0.9f);
            }

        }
    }
}

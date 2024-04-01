using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits and dynamic edits in the world
public class VoxelEdits : VoxelBehaviour {
    // Reference that we can use to edit a dynamic edit after it has been placed
    public struct DynamicEditHandle {
        internal int index;
        internal int registryIndex;
    }

    // Reference that we can use to fetch modified data of a voxel edit
    internal class VoxelEditCountersHandle {
        internal int changed;
        internal int pending;
        internal VoxelEditCounterCallback callback;
    } 

    // Callback that we can pass to the voxel edit functions to allow us to check how many voxels were added/removed
    public delegate void VoxelEditCounterCallback(int changed);

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
    // ONLY USED FOR SERIALIZATION AND SAVING
    internal SerializableRegistry worldEditRegistry;

    // ALL dynamic edits (even the ones that should not be serialized)
    internal List<IDynamicEdit> dynamicEdits;

    // Temporary place for voxel/dynamic edits that have not been applied yet
    internal Queue<IVoxelEdit> tempVoxelEdits = new Queue<IVoxelEdit>();
    internal Queue<IDynamicEdit> tempDynamicEdit = new Queue<IDynamicEdit>();

    public bool Free => tempVoxelEdits.Count == 0 && terrain.VoxelMesher.Free;

    // Used to register custom dynamic edit types
    public delegate void RegisterDynamicEditType(SerializableRegistry registry);
    public event RegisterDynamicEditType onRegisterDynamicEditTypes;

    // Tells us when we can apply edits
    bool applyEdits;

    // Initialize the voxel edits handler
    internal override void Init() {
        chunkLookup = new NativeHashMap<VoxelEditOctreeNode.RawNode, int>(0, Allocator.Persistent);
        nodes = new NativeList<VoxelEditOctreeNode>(Allocator.Persistent)
        {
            VoxelEditOctreeNode.RootNode(VoxelUtils.MaxDepth)
        };

        lookup = new NativeHashMap<int, int>(0, Allocator.Persistent);
        sparseVoxelData = new List<SparseVoxelDeltaData>();
        worldEditRegistry = new SerializableRegistry();
        dynamicEdits = new List<IDynamicEdit>();

        terrain.onInitialGenerationDone += () => { applyEdits = true; };

        // Register common dynamic edit types
        onRegisterDynamicEditTypes += (SerializableRegistry registry) => {
            registry.Register<SphereDynamicEdit>();
            registry.Register<CuboidDynamicEdit>();
        };

        // Register custom dynamic edit types
        onRegisterDynamicEditTypes?.Invoke(worldEditRegistry);
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
        if (!applyEdits)
            return;

        if (terrain.Free && terrain.VoxelMesher.Free && terrain.VoxelGenerator.Free && terrain.VoxelOctree.Free) {
            IVoxelEdit edit;
            if (tempVoxelEdits.TryDequeue(out edit)) {
                ApplyVoxelEdit(edit, true);
            }

            IDynamicEdit dynEdit;
            if (tempDynamicEdit.TryDequeue(out dynEdit)) {
                InternalApplyDynEditImmediate(dynEdit);
            }
        }
    }

    // Apply a voxel edit to the terrain world
    public void ApplyVoxelEdit(IVoxelEdit edit, bool neverForget = false, bool immediate = false, VoxelEditCounterCallback callback = null) {
        if (!(terrain.Free && terrain.VoxelMesher.Free && terrain.VoxelGenerator.Free && terrain.VoxelOctree.Free)) {
            if (neverForget)
                tempVoxelEdits.Enqueue(edit);
            return;
        }

        foreach (var item in sparseVoxelData) {
            item.applyJobHandle.Complete();
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
                materials = new NativeArray<byte>(VoxelUtils.Volume, Allocator.Persistent),
            };

            for (int i = 0; i < VoxelUtils.Volume; i++) {
                data.materials[i] = byte.MaxValue;
            }

            sparseVoxelData.Add(data);
        }

        VoxelEditCountersHandle countersHandle = new VoxelEditCountersHandle {
            changed = 0,
            pending = 0,
            callback = callback,
        };

        for (int i = 0; i < chunksToUpdate.Length; i++) {
            SparseVoxelDeltaData data = sparseVoxelData[chunksToUpdate[i]];
            JobHandle handle = edit.Apply(data);
            data.applyJobHandle = handle;
            sparseVoxelData[chunksToUpdate[i]] = data;
            
            if (immediate)
                handle.Complete();
        }

        // Custom job to find all the octree nodes that touch the bounds
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bounds, out temp);

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            chunk.voxelCountersHandle = countersHandle;
            countersHandle.pending++;
            chunk.Remesh(terrain);
        }

        temp.Value.Dispose();
        pending.Dispose();
        chunksToUpdate.Dispose();
        addedNodes.Dispose();
    }

    // Internally used to hide generic
    private void InternalApplyDynEditImmediate(IDynamicEdit dynamicEdit) {
        // Custom job to find all the octree nodes that touch the bounds
        Bounds bound = dynamicEdit.GetBounds();
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bound, out temp);

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            chunk.Remesh(terrain);
        }
        temp.Value.Dispose();
    }

    // Apply a dynamic edit to the terrain world when free or immediately
    public DynamicEditHandle ApplyDynamicEdit<T>(T dynamicEdit, bool immediate = false, bool save = true) where T: struct, IDynamicEdit {
        DynamicEditHandle reference = new DynamicEditHandle {
            index = dynamicEdits.Count,
            registryIndex = -1,
        };

        if (immediate && terrain.Free) {
            InternalApplyDynEditImmediate(dynamicEdit);
        } else {
            tempDynamicEdit.Enqueue(dynamicEdit);
        }

        // Sometimes we wish to apply dynamic edits that are not serialized
        // use cases for this would be props that are dynamic edits themselves
        // Since props serialization is automatically handled, we would cuase double serialization 
        // (one for the prefabs and one for the dyn edits)
        if (save) {
            reference.registryIndex = worldEditRegistry.Add(dynamicEdit);
        }

        dynamicEdits.Add(dynamicEdit);
        return reference;
    }

    // Update a dynamic edit that is already applied to the world using its reference
    public void ModifyDynamicEdit<T>(DynamicEditHandle reference, Func<T, T> callback) where T: struct, IDynamicEdit {
        T result = callback.Invoke((T)dynamicEdits[reference.index]);
        dynamicEdits[reference.index] = result;

        if (reference.registryIndex != -1) {
            int lookup = worldEditRegistry.lookup[typeof(T)];
            var list = (IList<T>)worldEditRegistry.types[lookup].List;
            list[reference.registryIndex] = result;
        }
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

    // Create an apply job dependeny for a chunk that has voxel edits
    public JobHandle TryGetApplyVoxelEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels, NativeCounter counter, JobHandle dependency) {
        if (!IsChunkAffectedByVoxelEdits(chunk)) {
            if (chunk.voxelCountersHandle != null) {
                chunk.voxelCountersHandle.pending--;
                chunk.voxelCountersHandle = null;
            }
            
            return dependency;
        }

        VoxelEditOctreeNode.RawNode raw = new VoxelEditOctreeNode.RawNode {
            position = chunk.node.position,
            depth = chunk.node.depth,
            size = chunk.node.size,
        };

        int index = chunkLookup[raw];
        SparseVoxelDeltaData data = sparseVoxelData[index];

        JobHandle newDep = JobHandle.CombineDependencies(dependency, data.applyJobHandle);

        VoxelEditApplyJob job = new VoxelEditApplyJob {
            data = data,
            voxels = voxels,
            counter = counter,
        };
        return job.Schedule(VoxelUtils.Volume, 2048 * 8, newDep);
    }    

    // Create a list of dependencies to apply to chunks that have been affected by dynamic edits
    // Applied BEFORE the voxel edits
    public JobHandle TryGetApplyDynamicEditJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> voxels) {
        JobHandle dep = new JobHandle();

        foreach (var dynamicEdit in dynamicEdits) {
            if (dynamicEdit.GetBounds().Intersects(chunk.GetBounds())) {
                dep = dynamicEdit.Apply(chunk, ref voxels, dep);
            }
        }

        return dep;
    }

    // Update the modified voxel counters of a chunk after finishing meshing
    internal void UpdateCounters(MeshJobHandler handler, VoxelChunk voxelChunk)
    {
        VoxelEditCountersHandle handle = voxelChunk.voxelCountersHandle;
        if (handle != null)
        {
            handle.pending--;
            int lookup = chunkLookup[new VoxelEditOctreeNode.RawNode
            {
                position = voxelChunk.node.position,
                depth = voxelChunk.node.depth,
                size = voxelChunk.node.size,
            }];

            // Check current values, update them
            SparseVoxelDeltaData data = sparseVoxelData[lookup];
            int lastValue = data.lastCounters;
            int newValue = handler.voxelCounter.Count;

            // Store the data back into the sparse voxel array
            int delta = newValue - lastValue;
            data.lastCounters = newValue;
            sparseVoxelData[lookup] = data;

            handle.changed += delta;
            if (handle.pending == 0)
                handle.callback?.Invoke(handle.changed);            
        }
    }

    // Fetch the voxel array for a specific chunk at any given position
    // This allows the user to do custom voxel fetching to check the value of the voxel at a specific position
    // This is an asynchronous operation, and thus returns a JobHandle to be able to add a job dependency to actually wait for the job to complete
    // This only works for chunks that actually have a CPU sided native voxel array 
    // If you are going to call this method multiple times, you should externally cache the results (it applies the edits each time)
    public bool TryGetVoxelData(Vector3 position, out NativeArray<Voxel> voxels, out JobHandle handle) {
        voxels = new NativeArray<Voxel>();
        handle = new JobHandle();

        if (!(terrain.Free && terrain.VoxelMesher.Free && terrain.VoxelGenerator.Free && terrain.VoxelOctree.Free)) {
            return false;
        }

        // Custom job to find all the octree nodes that touch the bounds
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(new Bounds {
            center = position,
            extents = Vector3.zero,
        }, out temp);

        // Only one chunk at a time big fello (satisfactory reference)
        if (temp.Value.Length != 1) {
            return false;
        }

        // Actually create the voxels and the job values
        OctreeNode node = temp.Value[0];
        VoxelChunk chunk = terrain.Chunks[node];
        
        voxels = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeCounter tempCounter = new NativeCounter(Allocator.Persistent);
        voxels.CopyFrom(chunk.container.voxels);
        JobHandle dynamicEdit = TryGetApplyDynamicEditJobDependency(chunk, ref voxels);
        handle = TryGetApplyVoxelEditJobDependency(chunk, ref voxels, tempCounter, dynamicEdit);
        return true;
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

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Voxel terrain that handles generating the chunks and handling detail generation
// generate chunks -> generate voxels -> generate mesh -> generate mesh collider
[RequireComponent(typeof(VoxelGenerator))]
[RequireComponent(typeof(VoxelMesher))]
[RequireComponent(typeof(VoxelOctree))]
[RequireComponent(typeof(VoxelEdits))]
public class VoxelTerrain : MonoBehaviour
{
    [Header("Main Settings")]
    [Min(16)]
    public int resolution = 32;

    [Min(0)]
    public int voxelSizeReduction = 0;
    
    public GameObject chunkPrefab;
    private Dictionary<OctreeNode, VoxelChunk> chunks;
    private VoxelGenerator voxelGenerator;
    private VoxelMesher voxelMesher;
    private VoxelOctree voxelOctree;

    // Pending chunks that we will have to hide eventually
    private List<OctreeNode> toRemove = new List<OctreeNode>();

    // Pending chunks that we will need to make visible
    private List<VoxelChunk> toMakeVisible = new List<VoxelChunk>();

    // Called when the terrain finishes generating the base chunk and octree
    public delegate void InitialGenerationDone();
    public event InitialGenerationDone onInitialGenerationDone;

    // Called when the terrain finishes generating newly chunks
    public delegate void ChunkGenerationDone();
    public event ChunkGenerationDone onChunkGenerationDone;

    // Did the terrain finish all its tasks
    public bool Free { get; private set; } = false;

    internal bool started = false;

    // Did the terrain finish computing the initial base terrain
    public bool Initial { get; private set; } = false;

    private void OnValidate()
    {
        if (!started)
        {
            VoxelUtils.Size = resolution;
            VoxelUtils.VoxelSizeReduction = voxelSizeReduction;
        }
    }

    void Start() {
        // Initialize the generator and mesher
        started = true;
        voxelGenerator = GetComponent<VoxelGenerator>(); 
        voxelMesher = GetComponent<VoxelMesher>();
        voxelOctree = GetComponent<VoxelOctree>();
        VoxelUtils.Size = resolution;
        VoxelUtils.VoxelSizeReduction = voxelSizeReduction;
        voxelGenerator.Init();
        voxelMesher.Init();
        voxelOctree.Init();

        // Register the events
        voxelGenerator.onVoxelGenerationComplete += OnVoxelGenerationComplete;
        voxelMesher.onVoxelMeshingComplete += OnVoxelMeshingComplete;
        voxelMesher.onCollisionBakingComplete += OnCollisionBakingComplete;
        voxelOctree.onOctreeChanged += OnOctreeChanged;
        onChunkGenerationDone += SwapsChunk;

        // Init local vars
        chunks = new Dictionary<OctreeNode, VoxelChunk>();
    }

    // Dispose of all the voxel behaviours
    private void OnApplicationQuit()
    {
        voxelGenerator.Dispose();
        voxelMesher.Dispose();
        voxelOctree.Dispose();
    }

    private void Update()
    {
        if (!Free && voxelGenerator.Free && voxelMesher.Free) {
            Free = true;

            onChunkGenerationDone?.Invoke();

            if (!Initial)
            {
                onInitialGenerationDone?.Invoke();
                Initial = true;
            }
        }
    }

    // Deswpans the chunks that we do not need of and makes the new ones visible
    void SwapsChunk()
    {
        foreach (var item in toRemove)
        {
            if (chunks.TryGetValue(item, out VoxelChunk value))
            {
                value.voxels.Dispose();
                chunks.Remove(item);
                Destroy(value.gameObject);
            }
        }

        toRemove.Clear();

        foreach (var item in toMakeVisible)
        {
            item.GetComponent<MeshRenderer>().enabled = true;
        }

        toMakeVisible.Clear();
    }

    // Generate the new chunks and delete the old ones
    private void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed)
    {
        foreach (var item in removed)
        {
            toRemove.Add(item);
        }

        foreach (var item in added)
        {
            if (item.childBaseIndex == -1)
            {
                float size = item.ScalingFactor();
                GameObject obj = Instantiate(chunkPrefab, item.WorldPosition(), Quaternion.identity, this.transform);
                //obj.GetComponent<MeshRenderer>().enabled = false;
                obj.transform.localScale = new Vector3(size, size, size);
                VoxelChunk chunk = obj.GetComponent<VoxelChunk>();
                chunk.voxels = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                chunk.node = item;
                voxelGenerator.GenerateVoxels(chunk);
                chunks.TryAdd(item, chunk);
                toMakeVisible.Add(chunk);
            }
        }

        Free = false;
    }

    // When we finish generating the voxel data, begin the mesh generation
    void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request)
    {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();
        chunk.voxels.CopyFrom(request.voxels);
        voxelMesher.GenerateMesh(chunk, request, chunk.node.generateCollisions);
    }

    // Update the mesh of the given chunk when we generate it
    void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh voxelMesh)
    {
        chunk.GetComponent<MeshFilter>().mesh = voxelMesh.mesh;
        chunk.GetComponent<MeshRenderer>().materials = voxelMesh.materials;
    }

    // Update the mesh collider when we finish collision baking
    void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh voxelMesh) 
    {
        chunk.GetComponent<MeshCollider>().sharedMesh = voxelMesh.mesh;
    }

    // Request all the chunks to regenerate their meshes
    public void RequestAll(bool disableColliders = true, bool tempHide = false)
    {
        if (Free && voxelGenerator.Free && voxelMesher.Free)
        {
            foreach (var item in chunks)
            {
                if (disableColliders)
                {
                    item.Value.GetComponent<MeshCollider>().sharedMesh = null;
                }

                if (tempHide)
                {
                    item.Value.GetComponent<MeshFilter>().mesh = null;
                }

                voxelGenerator.GenerateVoxels(item.Value);
            }
        }
    }
}

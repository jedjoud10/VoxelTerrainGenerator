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
    // Singleton pattern heheheha
    public static VoxelTerrain Instance { get; private set; }

    // Common components added onto the terrain
    public VoxelGenerator VoxelGenerator { get; private set; }
    public VoxelMesher VoxelMesher { get; private set; }
    public VoxelOctree VoxelOctree { get; private set; }
    public VoxelEdits VoxelEdits { get; private set; }


    [Header("Main Settings")]
    [Min(16)]
    public int resolution = 32;

    [Min(0)]
    public int voxelSizeReduction = 0;
    
    public GameObject chunkPrefab;
    public Dictionary<OctreeNode, VoxelChunk> Chunks { get; private set; }

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    void Start() {
        // Initialize the generator and mesher
        started = true;
        VoxelGenerator = GetComponent<VoxelGenerator>(); 
        VoxelMesher = GetComponent<VoxelMesher>();
        VoxelOctree = GetComponent<VoxelOctree>();
        VoxelEdits = GetComponent<VoxelEdits>();
        
        // Set the voxel utils static class
        VoxelUtils.Size = resolution;
        VoxelUtils.VoxelSizeReduction = voxelSizeReduction;

        // Set self inside voxel behavior
        VoxelGenerator.terrain = this;
        VoxelMesher.terrain = this;
        VoxelOctree.terrain = this;
        VoxelEdits.terrain = this;

        VoxelGenerator.Init();
        VoxelMesher.Init();
        VoxelOctree.Init();
        VoxelEdits.Init();

        // Register the events
        VoxelGenerator.onVoxelGenerationComplete += OnVoxelGenerationComplete;
        VoxelMesher.onVoxelMeshingComplete += OnVoxelMeshingComplete;
        VoxelMesher.onCollisionBakingComplete += OnCollisionBakingComplete;
        VoxelOctree.onOctreeChanged += OnOctreeChanged;
        onChunkGenerationDone += SwapsChunk;

        // Init local vars
        Chunks = new Dictionary<OctreeNode, VoxelChunk>();
    }

    // Dispose of all the voxel behaviours
    private void OnApplicationQuit()
    {
        VoxelOctree.Dispose();
        VoxelMesher.Dispose();
        VoxelEdits.Dispose();
        VoxelGenerator.Dispose();

        foreach (var item in Chunks)
        {
            item.Value.voxels.Dispose();
        }
    }

    private void Update()
    {
        if (!Free && VoxelGenerator.Free && VoxelMesher.Free && toMakeVisible.Count > 0) {
            Free = true;

            onChunkGenerationDone?.Invoke();

            if (!Initial)
            {
                Debug.Log("Initial generation done");
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
            if (Chunks.TryGetValue(item, out VoxelChunk value))
            {
                value.voxels.Dispose();
                Chunks.Remove(item);
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
                obj.GetComponent<MeshRenderer>().enabled = false;
                obj.transform.localScale = new Vector3(size, size, size);
                VoxelChunk chunk = obj.GetComponent<VoxelChunk>();
                chunk.voxels = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                chunk.node = item;
                VoxelGenerator.GenerateVoxels(chunk);
                Chunks.TryAdd(item, chunk);
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
        if (Free && VoxelGenerator.Free && VoxelMesher.Free)
        {
            foreach (var item in Chunks)
            {
                if (disableColliders)
                {
                    item.Value.GetComponent<MeshCollider>().sharedMesh = null;
                }

                if (tempHide)
                {
                    item.Value.GetComponent<MeshFilter>().mesh = null;
                }

                VoxelGenerator.GenerateVoxels(item.Value);
            }
        }
    }
}

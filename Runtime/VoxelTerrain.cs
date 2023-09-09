using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Voxel terrain that handles generating the chunks and handling detail generation
// generate chunks -> generate voxels -> generate mesh -> generate mesh collider
[RequireComponent(typeof(VoxelGenerator))]
[RequireComponent(typeof(VoxelMesher))]
[RequireComponent(typeof(VoxelCollisions))]
[RequireComponent(typeof(VoxelOctree))]
[RequireComponent(typeof(VoxelEdits))]
public class VoxelTerrain : MonoBehaviour
{
    // Singleton pattern heheheha
    public static VoxelTerrain Instance { get; private set; }

    // Common components added onto the terrain
    public VoxelGenerator VoxelGenerator { get; private set; }
    public VoxelMesher VoxelMesher { get; private set; }
    public VoxelCollisions VoxelCollisions { get; private set; }
    public VoxelOctree VoxelOctree { get; private set; }
    public VoxelEdits VoxelEdits { get; private set; }


    [Header("Main Settings")]
    [Range(16, 64)]
    public int resolution = 32;

    [Min(0)]
    public int voxelSizeReduction = 0;

    public bool backBufferedChunkVisibility;


    // Object pooling stuff
    public GameObject chunkPrefab;
    private List<GameObject> pooledChunkGameObjects;
    private List<NativeArray<Voxel>> pooledNativeVoxelArrays;

    public Dictionary<OctreeNode, VoxelChunk> Chunks { get; private set; }

    // Pending chunks that we will have to hide eventually
    private List<OctreeNode> toRemoveChunk = new List<OctreeNode>();

    // Pending chunks that we will need to make visible
    private List<VoxelChunk> toMakeVisible = new List<VoxelChunk>();

    // Called when the terrain finishes generating the base chunk and octree
    public delegate void InitialGenerationDone();
    public event InitialGenerationDone onInitialGenerationDone;

    // Called when the terrain finishes generating newly chunks
    public delegate void ChunkGenerationDone();
    public event ChunkGenerationDone onChunkGenerationDone;

    public bool Free { get; private set; } = true;

    internal bool started = false;

    // Did the terrain finish computing the initial base terrain
    public bool Initial { get; private set; } = false;

    private void OnValidate()
    {
        if (!started)
        {
            resolution = Mathf.ClosestPowerOfTwo(resolution);
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
        VoxelCollisions = GetComponent<VoxelCollisions>();
        VoxelOctree = GetComponent<VoxelOctree>();
        VoxelEdits = GetComponent<VoxelEdits>();

        // Set the voxel utils static class
        VoxelUtils.Size = resolution;
        VoxelUtils.VoxelSizeReduction = voxelSizeReduction;

        // Initialize all the behaviors
        VoxelGenerator.InitWith(this);
        VoxelMesher.InitWith(this);
        VoxelCollisions.InitWith(this);
        VoxelOctree.InitWith(this);
        VoxelEdits.InitWith(this);

        // Register the events
        VoxelGenerator.onVoxelGenerationComplete += OnVoxelGenerationComplete;
        VoxelMesher.onVoxelMeshingComplete += OnVoxelMeshingComplete;
        VoxelCollisions.onCollisionBakingComplete += OnCollisionBakingComplete;
        VoxelOctree.onOctreeChanged += OnOctreeChanged;
        onChunkGenerationDone += SwapsChunk;

        // Init local vars
        Chunks = new Dictionary<OctreeNode, VoxelChunk>();
        pooledChunkGameObjects = new List<GameObject>();
        pooledNativeVoxelArrays = new List<NativeArray<Voxel>>();
    }

    // Dispose of all the voxel behaviours
    private void OnApplicationQuit()
    {
        VoxelOctree.Dispose();
        VoxelMesher.Dispose();
        VoxelEdits.Dispose();
        VoxelGenerator.Dispose();
        VoxelCollisions.Dispose();

        foreach (var item in Chunks)
        {
            if (item.Value.voxels.HasValue)
            {
                item.Value.voxels.Value.Dispose();
            }
        }

        foreach (var item in pooledNativeVoxelArrays)
        {
            item.Dispose();
        }
    }

    private void Update()
    {
        if (!Free && VoxelGenerator.Free && VoxelMesher.Free && VoxelOctree.Free && toMakeVisible.Count > 0) {
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
        // Remove the chunks from the scene and put them back into the pool
        foreach (var item in toRemoveChunk)
        {
            if (Chunks.TryGetValue(item, out VoxelChunk voxelChunk))
            {
                Chunks.Remove(item);
                voxelChunk.gameObject.SetActive(false);
                pooledChunkGameObjects.Add(voxelChunk.gameObject);
                
                if (voxelChunk.voxels.HasValue)
                {
                    pooledNativeVoxelArrays.Add(voxelChunk.voxels.Value);
                    voxelChunk.voxels = null;
                }
            }
        }

        toRemoveChunk.Clear();

        // Make the chunks visible
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
            toRemoveChunk.Add(item);
        }

        // Fetch new chunks from the pool
        foreach (var item in added)
        {
            if (item.ChildBaseIndex != -1)
                continue;

            GameObject chunk = FetchPooledChunk();

            float size = item.ScalingFactor;
            chunk.GetComponent<MeshRenderer>().enabled = !backBufferedChunkVisibility;
            chunk.transform.position = item.Position;
            chunk.transform.localScale = new Vector3(size, size, size);
            VoxelChunk voxelChunk = chunk.GetComponent<VoxelChunk>();
            voxelChunk.node = item;
            VoxelGenerator.GenerateVoxels(voxelChunk);
            Chunks.TryAdd(item, voxelChunk);
            toMakeVisible.Add(voxelChunk);

            if (item.Depth == item.maxDepth)
            {
                voxelChunk.voxels = FetchVoxelNativeArray();
            }
        }

        Free = false;
    }

    // Fetches a pooled chunk, or creates a new one from scratch
    private GameObject FetchPooledChunk()
    {
        GameObject chunk;

        if (pooledChunkGameObjects.Count == 0)
        {
            GameObject obj = Instantiate(chunkPrefab, this.transform);
            Mesh mesh = new Mesh();
            obj.GetComponent<VoxelChunk>().sharedMesh = mesh;
            obj.name = $"Voxel Chunk";
            chunk = obj;
        }
        else
        {
            chunk = pooledChunkGameObjects[0];
            pooledChunkGameObjects.RemoveAt(0);
            chunk.GetComponent<MeshCollider>().sharedMesh = null;
            chunk.GetComponent<MeshFilter>().sharedMesh = null;
        }

        chunk.SetActive(true);
        return chunk;
    }

    // Fetches a voxel native array, or allocates one from scratch
    private NativeArray<Voxel> FetchVoxelNativeArray()
    {
        NativeArray<Voxel> nativeArray;

        if (pooledNativeVoxelArrays.Count == 0)
        {
            nativeArray = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
        else
        {
            nativeArray = pooledNativeVoxelArrays[0];
            pooledNativeVoxelArrays.RemoveAt(0);
        }

        return nativeArray;
    }

    // When we finish generating the voxel data, begin the mesh generation
    private void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request)
    {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();

        if (chunk.voxels.HasValue)
            chunk.voxels.Value.CopyFrom(request.voxels);
        
        voxelMesher.GenerateMesh(chunk, request, chunk.node.GenerateCollisions);
    }

    // Update the mesh of the given chunk when we generate it
    private void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh)
    {
        var renderer = chunk.GetComponent<MeshRenderer>();
        chunk.GetComponent<MeshFilter>().sharedMesh = chunk.sharedMesh;

        // Set mesh and renderer settings
        renderer.materials = mesh.Materials;

        // Set renderer bounds
        renderer.bounds = new Bounds
        {
            min = chunk.node.Position,
            max = chunk.node.Position + chunk.node.Size,
        };
    }

    // Update the mesh collider when we finish collision baking
    private void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh mesh) 
    {
        if (mesh.VertexCount > 0 & mesh.TriangleCount > 0)
        {
            chunk.GetComponent<MeshCollider>().sharedMesh = chunk.sharedMesh;
        }
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

    // Used for debugging the amount of jobs remaining
    void OnGUI()
    {
        if (Debug.isDebugBuild)
        {
            GUI.Label(new Rect(0, 0, 300, 30), $"Pending GPU async readback jobs: {VoxelGenerator.pendingVoxelGenerationChunks.Count}");
            GUI.Label(new Rect(0, 15, 300, 30), $"Pending mesh jobs: {VoxelMesher.pendingMeshJobs.Count}");
            GUI.Label(new Rect(0, 30, 300, 30), $"Pending mesh baking jobs: {VoxelCollisions.ongoingBakeJobs.Count}");
            GUI.Label(new Rect(0, 45, 300, 30), $"# of pooled chunk game objects: {pooledChunkGameObjects.Count}");
            GUI.Label(new Rect(0, 60, 300, 30), $"# of pooled native voxel arrays: {pooledNativeVoxelArrays.Count}");
            GUI.Label(new Rect(0, 75, 300, 30), $"# of chunks to make visible: {toMakeVisible.Count}");
            GUI.Label(new Rect(0, 90, 300, 30), $"# of chunks to remove: {toRemoveChunk.Count}");
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Voxel terrain that handles generating the chunks and handling detail generation
// generate chunks -> generate voxels -> generate mesh -> generate mesh collider
[RequireComponent(typeof(VoxelGenerator))]
[RequireComponent(typeof(VoxelMesher))]
[RequireComponent(typeof(VoxelCollisions))]
[RequireComponent(typeof(VoxelOctree))]
[RequireComponent(typeof(VoxelEdits))]
public class VoxelTerrain : MonoBehaviour {
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

    public bool debugGUI = false;

    [Min(0)]
    public int voxelSizeReduction = 0;

    public bool backBufferedChunkVisibility;


    // Object pooling stuff
    public GameObject chunkPrefab;
    private List<GameObject> pooledChunkGameObjects;
    private List<VoxelChunkContainer> pooledVoxelChunkContainers;

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

    private void OnValidate() {
        if (!started) {
            resolution = Mathf.ClosestPowerOfTwo(resolution);
            VoxelUtils.Size = resolution;
            VoxelUtils.VoxelSizeReduction = voxelSizeReduction;
        }
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
        } else {
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
        pooledVoxelChunkContainers = new List<VoxelChunkContainer>();
    }

    // Dispose of all the voxel behaviours
    private void OnApplicationQuit() {
        VoxelOctree.Dispose();
        VoxelMesher.Dispose();
        VoxelEdits.Dispose();
        VoxelGenerator.Dispose();
        VoxelCollisions.Dispose();

        foreach (var item in pooledVoxelChunkContainers) {
            item.TempDispose();
        }
    }

    private void Update() {
        if (!Free && VoxelGenerator.Free && VoxelMesher.Free && VoxelOctree.Free && toMakeVisible.Count > 0) {
            Free = true;

            onChunkGenerationDone?.Invoke();

            if (!Initial) {
                Debug.Log("Initial generation done");
                onInitialGenerationDone?.Invoke();
                Initial = true;
            }
        }
    }

    // Deswpans the chunks that we do not need of and makes the new ones visible
    void SwapsChunk() {
        // Remove the chunks from the scene and put them back into the pool
        foreach (var item in toRemoveChunk) {
            if (Chunks.TryGetValue(item, out VoxelChunk voxelChunk)) {
                Chunks.Remove(item);
                voxelChunk.gameObject.SetActive(false);
                pooledChunkGameObjects.Add(voxelChunk.gameObject);

                if (voxelChunk.uniqueVoxelContainer) {
                    pooledVoxelChunkContainers.Add((VoxelChunkContainer)voxelChunk.container);
                    voxelChunk.container = null;
                }
            }
        }

        toRemoveChunk.Clear();

        // Make the chunks visible
        foreach (var item in toMakeVisible) {
            item.GetComponent<MeshRenderer>().enabled = true;
        }

        toMakeVisible.Clear();
    }

    // Generate the new chunks and delete the old ones
    private void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed) {
        foreach (var item in removed) {
            toRemoveChunk.Add(item);
        }

        // Fetch new chunks from the pool
        foreach (var item in added) {
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

            // Only generate chunk voxel data for chunks at lowest depth
            if (item.Depth == item.maxDepth) {
                voxelChunk.uniqueVoxelContainer = true;
                voxelChunk.container = FetchVoxelChunkContainer();
                voxelChunk.container.chunk = voxelChunk;
            } else {
                voxelChunk.uniqueVoxelContainer = false;
            }
        }

        Free = false;
    }

    // Fetches a pooled chunk, or creates a new one from scratch
    private GameObject FetchPooledChunk() {
        GameObject chunk;

        if (pooledChunkGameObjects.Count == 0) {
            GameObject obj = Instantiate(chunkPrefab, this.transform);
            Mesh mesh = new Mesh();
            obj.GetComponent<VoxelChunk>().sharedMesh = mesh;
            obj.name = $"Voxel Chunk";
            chunk = obj;
        } else {
            chunk = pooledChunkGameObjects[0];
            pooledChunkGameObjects.RemoveAt(0);
            chunk.GetComponent<MeshCollider>().sharedMesh = null;
            chunk.GetComponent<MeshFilter>().sharedMesh = null;
        }

        chunk.SetActive(true);
        return chunk;
    }

    // Fetches a voxel native array, or allocates one from scratch
    private VoxelChunkContainer FetchVoxelChunkContainer() {
        VoxelChunkContainer nativeArray;

        if (pooledVoxelChunkContainers.Count == 0) {
            nativeArray = new VoxelChunkContainer {
                voxels = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                chunk = null,
            };
            Debug.LogWarning("ALLOCATE VOXEL CHUNK CONTAINER");
        } else {
            nativeArray = pooledVoxelChunkContainers[0];
            pooledVoxelChunkContainers.RemoveAt(0);
        }

        return nativeArray;
    }

    // When we finish generating the voxel data, begin the mesh generation
    private void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request) {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();

        // Copy the voxel data from the request into the chunk's voxel data
        if (chunk.uniqueVoxelContainer) {
            Debug.LogWarning("Copy from request to perm chunk");
            chunk.container.voxels.CopyFrom(request.voxels);
            request.TempDispose();
        } else {
            Debug.LogWarning("Set container to request");
            chunk.container = request;
        }

        voxelMesher.GenerateMesh(chunk, chunk.node.GenerateCollisions);
    }

    // Update the mesh of the given chunk when we generate it
    private void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh) {
        var renderer = chunk.GetComponent<MeshRenderer>();
        chunk.GetComponent<MeshFilter>().sharedMesh = chunk.sharedMesh;

        // Using VoxelReadbackRequest as request, dispose to give back to voxgen
        if (!chunk.uniqueVoxelContainer) {
            chunk.container.TempDispose();
            chunk.container = null;
        }

        // Set mesh and renderer settings
        renderer.materials = mesh.Materials;

        // Set renderer bounds
        renderer.bounds = new Bounds {
            min = chunk.node.Position,
            max = chunk.node.Position + chunk.node.Size,
        };
    }

    // Update the mesh collider when we finish collision baking
    private void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh mesh) {
        if (mesh.VertexCount > 0 & mesh.TriangleCount > 0) {
            chunk.GetComponent<MeshCollider>().sharedMesh = chunk.sharedMesh;
        }
    }

    // Request all the chunks to regenerate their voxels (optional) AND meshes
    public void RequestAll(bool voxel, bool disableColliders = true, bool tempHide = false) {
        if (Free && VoxelGenerator.Free && VoxelMesher.Free) {
            foreach (var item in Chunks) {
                if (disableColliders) {
                    item.Value.GetComponent<MeshCollider>().sharedMesh = null;
                }

                if (tempHide) {
                    item.Value.GetComponent<MeshFilter>().mesh = null;
                }

                if (voxel) {
                    VoxelGenerator.GenerateVoxels(item.Value);
                } else {
                    VoxelMesher.GenerateMesh(item.Value, item.Value.uniqueVoxelContainer);
                }
            }
        }
    }

    // Used for debugging the amount of jobs remaining
    void OnGUI() {
        if (debugGUI) {
            GUI.Label(new Rect(0, 0, 300, 30), $"Pending GPU async readback jobs: {VoxelGenerator.pendingVoxelGenerationChunks.Count}");
            GUI.Label(new Rect(0, 15, 300, 30), $"Pending mesh jobs: {VoxelMesher.pendingMeshJobs.Count}");
            GUI.Label(new Rect(0, 30, 300, 30), $"Pending mesh baking jobs: {VoxelCollisions.ongoingBakeJobs.Count}");
            GUI.Label(new Rect(0, 45, 300, 30), $"# of pooled chunk game objects: {pooledChunkGameObjects.Count}");
            GUI.Label(new Rect(0, 60, 300, 30), $"# of pooled native voxel arrays: {pooledVoxelChunkContainers.Count}");
            GUI.Label(new Rect(0, 75, 300, 30), $"# of chunks to make visible: {toMakeVisible.Count}");
            GUI.Label(new Rect(0, 90, 300, 30), $"# of chunks to remove: {toRemoveChunk.Count}");
        }
    }
}

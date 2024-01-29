using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

// Voxel terrain that handles generating the chunks and handling prop generation
// generate chunks -> generate voxels -> generate mesh -> generate mesh collider
[RequireComponent(typeof(VoxelGenerator))]
[RequireComponent(typeof(VoxelMesher))]
[RequireComponent(typeof(VoxelCollisions))]
[RequireComponent(typeof(VoxelOctree))]
[RequireComponent(typeof(VoxelEdits))]
[RequireComponent(typeof(VoxelProps))]
public partial class VoxelTerrain : MonoBehaviour {
    public enum GenerationReason {
        None,
        Initial,
        TerrainLoader,
        Deserialized,
        AnonymousRequest
    }

    // Singleton pattern heheheha
    public static VoxelTerrain Instance { get; private set; }

    // Common components added onto the terrain
    public VoxelGenerator VoxelGenerator { get; private set; }
    public VoxelMesher VoxelMesher { get; private set; }
    public VoxelCollisions VoxelCollisions { get; private set; }
    public VoxelOctree VoxelOctree { get; private set; }
    public VoxelEdits VoxelEdits { get; private set; }
    public VoxelProps VoxelProps { get; private set; }


    [Header("Main Settings")]
    [Range(16, 64)]
    public int resolution = 32;

    public bool debugGUI = false;

    [Min(0)]
    public int voxelSizeReduction = 0;


    // Object pooling stuff
    public GameObject chunkPrefab;
    private List<GameObject> pooledChunkGameObjects;
    private List<UniqueVoxelChunkContainer> pooledVoxelChunkContainers;

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

    // Called when the terrain is starting to get saved
    public delegate void TerrainSerializationStart();
    public event TerrainSerializationStart onSerializeStart;

    // Called when the terrain was saved
    public delegate void TerrainSerializationFinish();
    public event TerrainSerializationFinish onSerializeFinish;

    // Called when the terrain is starting to get loaded
    public delegate void TerrainDeserializationStart();
    public event TerrainDeserializationStart onDeserializeStart;

    // Called when the terrain was loaded from saved data
    public delegate void TerrainDeserializationFinish();
    public event TerrainDeserializationFinish onDeserializeFinish;

    // When we add a chunk to the octree
    public delegate void ChunkAdded(VoxelChunk chunk);
    public event ChunkAdded onChunkAdded;
    public bool Free { get; private set; } = true;
    internal bool started = false;
    private System.Diagnostics.Stopwatch timer;

    // Current reason why the terrain is generating
    private GenerationReason requestReason;

    private void OnValidate() {
        if (!started) {
            resolution = Mathf.ClosestPowerOfTwo(resolution);
            VoxelUtils.Size = resolution;
            VoxelUtils.VoxelSizeReduction = voxelSizeReduction;
        }
    }

    void Start() {
        if (Instance != null && Instance != this) {
            Destroy(this);
        } else {
            Instance = this;
        }

        // Initialize the generator and mesher
        started = true;
        VoxelGenerator = GetComponent<VoxelGenerator>();
        VoxelMesher = GetComponent<VoxelMesher>();
        VoxelCollisions = GetComponent<VoxelCollisions>();
        VoxelOctree = GetComponent<VoxelOctree>();
        VoxelEdits = GetComponent<VoxelEdits>();
        VoxelProps = GetComponent<VoxelProps>();

        // Set the voxel utils static class
        VoxelUtils.Size = resolution;
        VoxelUtils.VoxelSizeReduction = voxelSizeReduction;

        // Initialize all the behaviors
        VoxelGenerator.InitWith(this);
        VoxelMesher.InitWith(this);
        VoxelCollisions.InitWith(this);
        VoxelProps.InitWith(this);
        VoxelOctree.InitWith(this);
        VoxelEdits.InitWith(this);

        // Register the events
        VoxelGenerator.onVoxelGenerationComplete += OnVoxelGenerationComplete;
        VoxelMesher.onVoxelMeshingComplete += OnVoxelMeshingComplete;
        VoxelCollisions.onCollisionBakingComplete += OnCollisionBakingComplete;
        VoxelOctree.onOctreeChanged += OnOctreeChanged;

        // Init local vars
        Chunks = new Dictionary<OctreeNode, VoxelChunk>();
        pooledChunkGameObjects = new List<GameObject>();
        pooledVoxelChunkContainers = new List<UniqueVoxelChunkContainer>();
        timer = new System.Diagnostics.Stopwatch();
        timer.Start();
        requestReason = GenerationReason.Initial;
    }

    // Dispose of all the voxel behaviours
    private void OnApplicationQuit() {
        VoxelOctree.Dispose();
        VoxelMesher.Dispose();
        VoxelEdits.Dispose();
        VoxelGenerator.Dispose();
        VoxelCollisions.Dispose();
        VoxelProps.Dispose();


        foreach (var item in Chunks) {
            if (item.Value.container != null)
                item.Value.container.voxels.Dispose();
        }

        foreach (var item in pooledVoxelChunkContainers) {
            // Dispose manually since the TempDispose override does nothing
            item.voxels.Dispose();
        }
    }

    private void Update() {
        if (!Free && VoxelGenerator.Free && VoxelMesher.Free && VoxelOctree.Free && VoxelEdits.Free) {
            Free = true;
            onChunkGenerationDone?.Invoke();

            if (toMakeVisible.Count > 0) {
                // Remove the chunks from the scene and put them back into the pool
                foreach (var item in toRemoveChunk) {
                    if (Chunks.TryGetValue(item, out VoxelChunk voxelChunk)) {
                        Chunks.Remove(item);
                        PoolChunkBack(voxelChunk);
                    }
                }

                toRemoveChunk.Clear();

                // Make the chunks visible
                foreach (var item in toMakeVisible) {
                    item.GetComponent<MeshRenderer>().enabled = true;
                }

                toMakeVisible.Clear();
            }

            switch (requestReason) {
                case GenerationReason.Initial:
                    timer.Stop();
                    Debug.Log($"Initial generation done. Took {timer.ElapsedMilliseconds}ms");
                    onInitialGenerationDone?.Invoke();
                    break;
                case GenerationReason.Deserialized:
                    onDeserializeFinish?.Invoke();
                    break;
                default:
                    break;
            }

            requestReason = GenerationReason.None;
        }
    }

    // Give the chunk's resources back to the main pool
    private void PoolChunkBack(VoxelChunk voxelChunk) {
        voxelChunk.gameObject.SetActive(false);
        pooledChunkGameObjects.Add(voxelChunk.gameObject);

        if (voxelChunk.container != null && voxelChunk.container is UniqueVoxelChunkContainer) {
            pooledVoxelChunkContainers.Add((UniqueVoxelChunkContainer)voxelChunk.container);
        }

        if (voxelChunk.container is VoxelReadbackRequest) {
            voxelChunk.container.TempDispose();
        }

        voxelChunk.container = null;
    }

    // Generate the new chunks and delete the old ones
    private void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed, ref NativeList<OctreeNode> all) {
        foreach (var item in removed) {
            toRemoveChunk.Add(item);
        }

        // Fetch new chunks from the pool
        bool generated = false;
        foreach (var item in added) {
            if (item.childBaseIndex != -1)
                continue;
            generated = true;
            GameObject gameObject = FetchPooledChunk();

            float size = item.scalingFactor;
            gameObject.GetComponent<MeshRenderer>().enabled = false;
            gameObject.transform.position = item.position;
            gameObject.transform.localScale = new Vector3(size, size, size);
            VoxelChunk chunk = gameObject.GetComponent<VoxelChunk>();
            chunk.node = item;

            // Only generate chunk voxel data for chunks at lowest depth
            chunk.container = null;
            if (item.depth == VoxelUtils.MaxDepth) {
                chunk.container = FetchVoxelChunkContainer();
                chunk.container.chunk = chunk;
            }

            // Begin the voxel pipeline by generating the voxels for this chunk
            VoxelGenerator.GenerateVoxels(chunk);
            Chunks.TryAdd(item, chunk);
            toMakeVisible.Add(chunk);
            onChunkAdded?.Invoke(chunk);
        }

        Free = !generated;
        
        if (generated && requestReason == GenerationReason.None) {
            requestReason = GenerationReason.TerrainLoader;
        }
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
    private UniqueVoxelChunkContainer FetchVoxelChunkContainer() {
        UniqueVoxelChunkContainer nativeArray;

        if (pooledVoxelChunkContainers.Count == 0) {
            nativeArray = new UniqueVoxelChunkContainer {
                voxels = new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                chunk = null,
            };
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
        if (chunk.container is UniqueVoxelChunkContainer) {
            chunk.container.voxels.CopyFrom(request.voxels);
            request.TempDispose();
        } else {
            chunk.container = request;
        }

        voxelMesher.GenerateMesh(chunk, chunk.node.depth == VoxelUtils.MaxDepth);
    }

    // Update the mesh of the given chunk when we generate it
    private void OnVoxelMeshingComplete(VoxelChunk chunk, VoxelMesh mesh) {
        var renderer = chunk.GetComponent<MeshRenderer>();
        chunk.GetComponent<MeshFilter>().sharedMesh = chunk.sharedMesh;

        // Using VoxelReadbackRequest as request, dispose to give back to voxgen
        if (chunk.container is VoxelReadbackRequest) {
            chunk.container.TempDispose();
            chunk.container = null;
        }

        // Set mesh and renderer settings
        renderer.materials = mesh.Materials;

        // Set renderer bounds
        renderer.bounds = new Bounds {
            min = chunk.node.position,
            max = chunk.node.position + chunk.node.size,
        };

        // Pool the chunk if it's empty
        /*
        if (mesh.VertexCount == 0)
            PoolChunkBack(chunk);
        */
    }

    // Update the mesh collider when we finish collision baking
    private void OnCollisionBakingComplete(VoxelChunk chunk, VoxelMesh mesh) {
        if (mesh.VertexCount > 0 & mesh.TriangleCount > 0) {
            chunk.GetComponent<MeshCollider>().sharedMesh = chunk.sharedMesh;
        }
    }

    // Request all the chunks to regenerate their voxels (optional) AND meshes
    public void RequestAll(bool voxel, bool disableColliders = true, bool tempHide = false, GenerationReason reason = GenerationReason.AnonymousRequest) {
        if (Free) {
            foreach (var item in Chunks) {
                if (disableColliders) {
                    item.Value.GetComponent<MeshCollider>().sharedMesh = null;
                }

                if (tempHide) {
                    item.Value.GetComponent<MeshFilter>().mesh = null;
                }

                if (voxel) {
                    item.Value.Regenerate(this);
                } else {
                    item.Value.Remesh(this);
                }
            }

            this.requestReason = reason;
            Free = false;
        }
    }

    // Used for debugging the amount of jobs remaining
    void OnGUI() {
        var offset = 0;
        void Label(string text) {
            GUI.Label(new Rect(0, offset, 300, 30), text);
            offset += 15;
        }

        if (debugGUI) {
            GUI.Box(new Rect(0, 0, 300, 315), "");
            Label($"Pending GPU async readback jobs: {VoxelGenerator.pendingVoxelGenerationChunks.Count}");
            Label($"Pending mesh jobs: {VoxelMesher.pendingMeshJobs.Count}");
            Label($"Pending mesh baking jobs: {VoxelCollisions.ongoingBakeJobs.Count}");
            Label($"# of pooled chunk game objects: {pooledChunkGameObjects.Count}");
            Label($"# of pooled native voxel arrays: {pooledVoxelChunkContainers.Count}");

            int usedVoxelArrays = Chunks.Where(x => x.Value.container is UniqueVoxelChunkContainer).Count();
            Label($"# of free native voxel arrays (voxel generator): {VoxelGenerator.freeVoxelNativeArrays.Cast<bool>().Where(x => x).Count()}");
            Label($"# of used native voxel arrays: {usedVoxelArrays}");
            Label($"# of chunks to make visible: {toMakeVisible.Count}");
            Label($"# of enabled chunks: {Chunks.Where(x => x.Value.gameObject.activeSelf).Count()}");
            Label($"# of enabled and meshed chunks: {Chunks.Where(x => (x.Value.gameObject.activeSelf && x.Value.sharedMesh.subMeshCount > 0)).Count()}");
            Label($"# of chunks to remove: {toRemoveChunk.Count}");
            Label($"# of world edits: {VoxelEdits.worldEditRegistry.TryGetAll<IWorldEdit>().Count}");
            Label($"# of pending voxel edits: {VoxelEdits.tempVoxelEdits.Count}");
            int mul = System.Runtime.InteropServices.Marshal.SizeOf(Voxel.Empty) * VoxelUtils.Volume;
            int bytes = pooledVoxelChunkContainers.Count * mul;
            int kbs = bytes / 1024;
            Label($"KBs of pooled native voxel arrays: {kbs}");
            bytes = usedVoxelArrays * mul;
            int kbs2 = bytes / 1024;
            Label($"KBs of used native voxel arrays: {kbs2}");
            Label($"KBs of total native voxel arrays: {kbs+kbs2}");
            Label("Generator free: " + VoxelGenerator.Free);
            Label("Mesher free: " + VoxelMesher.Free);
            Label("Octree free: " + VoxelOctree.Free);
            Label("Edits free: " + VoxelEdits.Free);
        }
    }
}

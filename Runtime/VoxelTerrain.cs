using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// Voxel terrain that handles generating the chunks and handling detail generation
// generate chunks -> generate voxels -> generate mesh -> generate mesh collider
[RequireComponent(typeof(VoxelGenerator), typeof(VoxelMesher), typeof(VoxelOctree))]
public class VoxelTerrain : MonoBehaviour
{
    [Header("Main Settings")]
    [Min(16)]
    public int resolution = 32;

    [Min(0)]
    public int voxelSizeReduction = 0;
    public bool generateCollisions = false;
    public Material material;
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

    bool started = false;
    bool generating = false;
    bool initial = true;

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
        started = false;
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
        if (generating && voxelGenerator.Free && voxelMesher.Free) {
            generating = false;

            onChunkGenerationDone?.Invoke();

            if (initial)
            {
                onInitialGenerationDone?.Invoke();
                initial = false;
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
            if (item.leaf)
            {
                float size = item.ScalingFactor();
                GameObject obj = Instantiate(chunkPrefab, item.WorldPosition(), Quaternion.identity, this.transform);
                obj.GetComponent<MeshRenderer>().material = material;
                obj.GetComponent<MeshRenderer>().enabled = false;
                obj.transform.localScale = new Vector3(size, size, size);
                VoxelChunk chunk = obj.GetComponent<VoxelChunk>();
                chunk.node = item;
                voxelGenerator.GenerateVoxels(chunk);
                chunks.TryAdd(item, chunk);
                toMakeVisible.Add(chunk);
            }
        }

        generating = true;
    }

    // When we finish generating the voxel data, begin the mesh generation
    void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request)
    {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();
        voxelMesher.GenerateMesh(chunk, request, chunk.node.generateCollisions && generateCollisions);
    }

    // Update the mesh of the given chunk when we generate it
    void OnVoxelMeshingComplete(VoxelChunk chunk, Mesh mesh)
    {
        chunk.GetComponent<MeshFilter>().mesh = mesh;
    }

    // Update the mesh collider when we finish collision baking
    void OnCollisionBakingComplete(VoxelChunk chunk, Mesh mesh) 
    {
        chunk.GetComponent<MeshCollider>().sharedMesh = mesh;
    }
}

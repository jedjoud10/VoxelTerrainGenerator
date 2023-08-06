using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
    public bool computeCollision = false;
    public Material material;
    
    public GameObject chunkPrefab;

    private Dictionary<OctreeNode, VoxelChunk> chunks;

    private VoxelGenerator voxelGenerator;
    private VoxelMesher voxelMesher;
    private VoxelOctree voxelOctree;

    void Start() {
        // Initialize the generator and mesher
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

    // Generate the new chunks and delete the old ones
    private void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed)
    {
        foreach (var item in removed)
        {
            if (chunks.TryGetValue(item, out VoxelChunk value))
            {
                chunks.Remove(item);
                Destroy(value.gameObject);
            }
        }

        foreach (var item in added)
        {
            if (item.leaf)
            {
                float size = item.ScalingFactor();
                GameObject obj = Instantiate(chunkPrefab, item.WorldPosition(), Quaternion.identity, this.transform);
                obj.GetComponent<MeshRenderer>().material = material;
                obj.transform.localScale = new Vector3(size, size, size);
                VoxelChunk chunk = obj.GetComponent<VoxelChunk>();
                voxelGenerator.GenerateVoxels(chunk);
                chunks.TryAdd(item, chunk);
            }
        }
    }

    // When we finish generating the voxel data, begin the mesh generation
    void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request)
    {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();
        voxelMesher.GenerateMesh(chunk, request, computeCollision);
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

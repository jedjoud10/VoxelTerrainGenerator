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
public class VoxelTerrain : MonoBehaviour
{
    private Queue<VoxelChunk> chunks = new Queue<VoxelChunk>();
    
    public GameObject chunkPrefab;

    public Vector3Int extents;

    void Start() {
        VoxelGenerator voxelGenerator = GetComponent<VoxelGenerator>(); 
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();

        voxelGenerator.onVoxelGenerationComplete += OnVoxelGenerationComplete;
        voxelMesher.onVoxelMeshingComplete += OnVoxelMeshingComplete;
        voxelMesher.onCollisionBakingComplete += OnCollisionBakingComplete;

        float offset = 64 * VoxelUtils.VOXEL_SIZE;

        for (int x = -extents.x; x < extents.x; x++) {
            for (int y = -extents.z; y < extents.z; y++) {
                for (int z = -extents.y; z < extents.y; z++) {
                    GameObject obj = Instantiate(chunkPrefab, new Vector3(x * offset, z * offset, y * offset), Quaternion.identity, this.transform);
                    VoxelChunk chunk = obj.GetComponent<VoxelChunk>();
                    voxelGenerator.GenerateVoxels(chunk);
                }  
            }
        }
    }

    void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request)
    {
        VoxelMesher voxelMesher = GetComponent<VoxelMesher>();
        voxelMesher.GenerateMesh(chunk, request, true);
    }

    void OnVoxelMeshingComplete(VoxelChunk chunk, Mesh mesh)
    {
        chunk.GetComponent<MeshFilter>().mesh = mesh;
    }

    void OnCollisionBakingComplete(VoxelChunk chunk, Mesh mesh) 
    {
        chunk.GetComponent<MeshCollider>().sharedMesh = mesh;
    }
}

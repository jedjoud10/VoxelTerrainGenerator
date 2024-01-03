using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Script added to all game objects that represent a chunk
public class VoxelChunk : MonoBehaviour {
    // Voxel chunk node
    public OctreeNode node;

    // Either the chunk's own voxel data (in case collisions are enabled) 
    // OR the voxel request data (temp)
    // If null it means the chunk cannot be generated (no voxel data!!)
    // This will NEVER be modified by the edit system.
    public VoxelTempContainer container;

    // Check if the chunk should contain its own data
    // If this is set that means we must generate collisions for the chunk as well
    public bool uniqueVoxelContainer;

    // Shared generated mesh
    public Mesh sharedMesh;

    // Remesh the chunk given the parent terrain
    public void Remesh(VoxelTerrain terrain) {
        if (uniqueVoxelContainer) {
            // Regenerate the mesh based on the unique voxel container
            terrain.VoxelMesher.GenerateMesh(this, true);
        } else {
            // If not, simply regenerate the chunk
            // This is pretty inefficient but it's a matter of memory vs performance
            terrain.VoxelGenerator.GenerateVoxels(this);
        }
    }

    // Regenerate the chunk given the parent terrain
    public void Regenerate(VoxelTerrain terrain) {
        terrain.VoxelGenerator.GenerateVoxels(this);
    }
}

// Cached voxel chunk container for chunks with their own temp voxels (for modifs)
public class VoxelChunkContainer : VoxelTempContainer {
    public override void TempDispose() {
    }
}
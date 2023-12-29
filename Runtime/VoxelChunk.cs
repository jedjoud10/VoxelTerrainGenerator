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
    public bool uniqueVoxelContainer;

    // Shared generated mesh
    public Mesh sharedMesh;
}

// Cached voxel chunk container for chunks with their own temp voxels (for modifs)
public class VoxelChunkContainer : VoxelTempContainer {
    public override void TempDispose() {
    }
}
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Script added to all game objects that represent a chunk
public class VoxelChunk : MonoBehaviour
{
    // The octree node that is associated with this voxel chunk
    public OctreeNode node;

    // Keep track of the voxel data for this voxel chunk
    public NativeArray<Voxel> voxels;

    // As voxel chunk container
    public VoxelChunkContainer AsContainer()
    {
        return new VoxelChunkContainer { chunk = this, voxels = voxels };
    }
}

public class VoxelChunkContainer : VoxelTempContainer
{
    public override void TempDispose()
    {
    }
}
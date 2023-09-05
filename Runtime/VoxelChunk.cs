using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Script added to all game objects that represent a chunk
public class VoxelChunk : MonoBehaviour
{
    public OctreeNode node;
    public NativeArray<Voxel>? voxels;
    public Mesh sharedMesh;

    public VoxelChunkContainer AsContainer()
    {
        if (voxels == null)
            return null;

        return new VoxelChunkContainer { chunk = this, voxels = voxels.Value };
    }
}

public class VoxelChunkContainer : VoxelTempContainer
{
    public override void TempDispose()
    {
    }
}
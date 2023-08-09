using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Maximum number of edit jobs per frame
    [Range(1, 8)]
    public int maxEditJobs = 1;

    // Indirection texture that contains the index of the textures at specific points in space
    private Texture3D indirectionTexture;

    // List of texture "segments" that will be sparsely stored in the world
    private List<VoxelTextures> editedVoxels;

    // Initialize the voxel edits handler
    internal override void Init()
    {
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the currently stored chunks
    public void ApplyVoxelEdit(IVoxelEdit edit)
    {
    }
}

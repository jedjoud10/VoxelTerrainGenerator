using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Maximum number of mesh jobs reserved for editing
    [Range(1, 8)]
    public int reservedMeshJobs = 1;

    // Indirection texture that contains the index of the textures at specific points in space
    private Texture3D indirectionTexture;

    // List of texture "segments" that will be sparsely stored in the world
    private List<Texture3D> editedVoxels;

    // Initialize the voxel edits handler
    internal override void Init()
    {
        editedVoxels = new List<Texture3D>();
        indirectionTexture = VoxelUtils.CreateTexture(256, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UInt);
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the terrain world
    public void ApplyVoxelEdit(IVoxelEdit edit)
    {
        // find the chunks affected by the edit
        // begin the jobs for the affected chunks (synchronous)
        // wait for completion immediately
    }
}

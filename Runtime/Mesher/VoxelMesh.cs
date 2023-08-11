using UnityEngine;

// The generated voxel mesh that we can render to the player
public struct VoxelMesh
{
    // Generated mesh that we can set
    public Mesh mesh;

    // Materials that must be set when setting the mesh
    public Material[] materials;

    public static VoxelMesh Empty = new VoxelMesh
    {
        mesh = null,
        materials = null
    };
}
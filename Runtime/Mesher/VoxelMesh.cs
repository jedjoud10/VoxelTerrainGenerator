using UnityEngine;

// The generated voxel mesh that we can render to the player
public struct VoxelMesh
{
    // Generated mesh that we can set
    public Mesh Mesh { get; internal set; }

    // Materials that must be set when setting the mesh
    public Material[] Materials { get; internal set; }

    // Should we compute collisions for this voxel mesh?
    public bool ComputeCollisions { get; internal set; }
    
    // Total number of vertices used by this mesh
    public int VertexCount { get; internal set; }

    // Total number of triangles used by this mesh
    public int TriangleCount { get; internal set; }

    public static VoxelMesh Empty = new VoxelMesh
    {
        Mesh = null,
        Materials = null,
        ComputeCollisions = false,
        VertexCount = 0,
        TriangleCount = 0,
    };
}
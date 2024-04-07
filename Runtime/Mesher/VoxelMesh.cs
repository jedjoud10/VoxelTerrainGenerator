using UnityEngine;

// The generated voxel mesh that we can render to the player
public struct VoxelMesh
{
    // Actual mesh reference (which is stored inside the chunk anyways)
    public Mesh SharedMesh { get; internal set; }

    // Lookup for converting sub-mesh index to voxel material index
    public int[] VoxelMaterialsLookup { get; internal set; }

    // Should we compute collisions for this voxel mesh?
    public bool ComputeCollisions { get; internal set; }

    // Total number of vertices used by this mesh
    public int VertexCount { get; internal set; }

    // Total number of triangles used by this mesh
    public int TriangleCount { get; internal set; }

    public static VoxelMesh Empty = new VoxelMesh
    {
        SharedMesh = null,
        VoxelMaterialsLookup = null,
        VertexCount = 0,
        TriangleCount = 0,
        ComputeCollisions = false,
    };
}
using UnityEngine;

// Script added to all game objects that represent a chunk
public class VoxelChunk : MonoBehaviour {
    // Voxel chunk node
    public OctreeNode node;

    // Either the chunk's own voxel data (in case collisions are enabled) 
    // OR the voxel request data (temp)
    // If null it means the chunk cannot be generated (no voxel data!!)
    public VoxelTempContainer container;

    // Callback that we must invoke when we finish meshing this voxel chunk
    internal VoxelEdits.VoxelEditCountersHandle voxelCountersHandle;

    // Shared generated mesh
    public Mesh sharedMesh;
    internal int[] voxelMaterialsLookup;

    // Get the AABB world bounds of this chunk
    public Bounds GetBounds() {
        return new Bounds {
            min = node.position,
            max = node.position + node.size,
        };
    }

    // Remesh the chunk given the parent terrain
    public void Remesh(VoxelTerrain terrain, int maxFrames = 5) {
        if (container is UniqueVoxelChunkContainer) {
            // Regenerate the mesh based on the unique voxel container
            terrain.VoxelMesher.GenerateMesh(this, node.depth == VoxelUtils.MaxDepth, maxFrames);
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

    // Convert a specific sub-mesh index (from physics collision for example) to voxel material index
    public bool TryGetVoxelMaterialFromSubmesh(int submeshIndex, out int voxelMaterialIndex) {
        if (voxelMaterialsLookup != null && submeshIndex < voxelMaterialsLookup.Length) {
            voxelMaterialIndex = voxelMaterialsLookup[submeshIndex];
            return true;
        }

        voxelMaterialIndex = -1;
        return false;
    }
}

// Cached voxel chunk container for chunks with their own temp voxels (for modifs)
public class UniqueVoxelChunkContainer : VoxelTempContainer {
    public override void TempDispose() {
    }
}
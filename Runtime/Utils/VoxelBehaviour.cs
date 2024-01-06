using UnityEngine;

// Used internally by the classes that handle terrain
public abstract class VoxelBehaviour : MonoBehaviour
{
    // Fetch the parent terrain heheheha
    internal protected VoxelTerrain terrain;

    // Initialize the voxel behavior with the given terrain
    internal void InitWith(VoxelTerrain terrain)
    {
        this.terrain = terrain;
        Init();
    }

    // Initialize the voxel behaviour (called from the voxel terrain)
    internal abstract void Init();

    // Dispose of any internally stored memory
    internal abstract void Dispose();
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Voxel prop that can be spawned in the world using two different methods
// Ray based method and density based method
// At further away distances the voxel prop will be swapped out either for a billboard or an indirectly drawn mesh
[CreateAssetMenu(menuName = "VoxelTerrain/New Voxel prop")]
public class Prop : ScriptableObject {
    // Used for LOD0 prop segments; prefabs spawned in the world
    public GameObject prefab;

    // Used for LOD1 prop segments; instanced indirect mesh rendering
    public Mesh instancedMesh;
    public Material instancedMeshMaterial;
}
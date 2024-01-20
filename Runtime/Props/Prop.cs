using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Voxel prop that can be spawned in the world using two different methods
// Ray based method and density based method
// At further away distances the voxel prop will be swapped out either for a billboard or an indirectly drawn mesh
[CreateAssetMenu(menuName = "VoxelTerrain/New Voxel prop")]
public class Prop : ScriptableObject {
    public GameObject prefab;
    public Mesh instancedMesh;
    public Mesh instancedBillboardMesh;
    public Texture2D instancedBillboardAlbedo;
    public Texture2D instancedBillboardNormals;
}
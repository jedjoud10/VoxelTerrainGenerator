using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Voxel prop that can be spawned in the world using two different methods
// Ray based method and density based method
// At further away distances the voxel prop will be swapped out either for a billboard or an indirectly drawn mesh
[CreateAssetMenu(menuName = "VoxelTerrain/New Voxel prop")]
public class Prop : ScriptableObject {
    // Used for LOD0 prop segments; prefabs spawned in the world
    public GameObject prefab;

    // Settings related to how we will generate the billboards
    public float billboardCaptureCameraScale = 10.0f;
    public int billboardTextureWidth = 1024;
    public int billboardTextureHeight = 1024;
    public Vector3 billboardCaptureRotation = Vector3.zero;
    public Vector3 billboardCapturePosition = new Vector3(10, 0, 0);
    public ComputeShader generationShader;

    // Settings related to how we will render the billboards
    public Vector2 billboardSize = Vector2.one * 10;
    public Vector3 billboardOffset;
    public bool billboardRestrictRotationY = false;
    public bool billboardCastShadows = false;
    public float billboardAlphaClipThreshold = 0.5f;
}


// Extra data that tells us how to render billboarded/instanced props
public class IndirectExtraPropData {
    public Texture2D billboardAlbedoTexture;
    public Texture2D billboardNormalTexture;
    public Material billboardMaterial;
}


// Blittable prop definition (that is also copied on the GPU compute shader)
// World pos, worl rot, world scale
public struct BlittableProp {
    public float4 position_and_scale;
    public float4 euler_angles_padding;
}
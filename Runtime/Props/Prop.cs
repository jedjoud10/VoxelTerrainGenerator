using System;
using Unity.Mathematics;
using UnityEngine;

// Voxel prop that can be spawned in the world using two different methods
// Ray based method and density based method
// At further away distances the voxel prop will be swapped out either for a billboard or an indirectly drawn mesh
[CreateAssetMenu(menuName = "VoxelTerrain/New Voxel prop")]
public class Prop : ScriptableObject {
    // Used for LOD0 prop segments; prefabs spawned in the world
    [Header("Behavior")]
    public GameObject prefab;
    public PropSpawnBehavior propSpawnBehavior = PropSpawnBehavior.RenderBillboards | PropSpawnBehavior.SpawnPrefabs;

    // Max number of props per segment and PER ALL segments
    // Spawning more segments than this will cause them to not be loaded, not very good
    [Min(1)] public int maxPropsPerSegment = 4096;
    [Min(1)] public int maxPropsInTotal = 4096;
    [Min(1)] public int maxVisibleProps = 4096;

    // Settings related to how we will generate the billboards
    [Header("Billboard Capture")]
    public float billboardCaptureCameraScale = 10.0f;
    public int billboardTextureWidth = 256;
    public int billboardTextureHeight = 256;
    public Vector3 billboardCaptureRotation = Vector3.zero;
    public Vector3 billboardCapturePosition = new Vector3(10, 0, 0);

    // How to show the billboard
    [Header("Billboard Rendering")]
    public Vector2 billboardSize = Vector2.one * 10;
    public Vector3 billboardOffset;
    public bool billboardRestrictRotationY = false;
    public bool billboardCastShadows = false;
    public float billboardAlphaClipThreshold = 0.5f;

    // Will this prop be generated as a prefab
    public bool WillSpawnPrefab => propSpawnBehavior.HasFlag(PropSpawnBehavior.SpawnPrefabs);

    // Will this prop be rendered as a billboard
    public bool WillRenderBillboard => propSpawnBehavior.HasFlag(PropSpawnBehavior.RenderBillboards);
}

[Flags]
public enum PropSpawnBehavior {
    None = 0,

    // Enables/disables rendering far away billboards
    RenderBillboards = 1 << 0,

    // Enables/disables spawning in actual prefabs
    SpawnPrefabs = 1 << 1,

    // Swaps out everything for instanced meshes (useful for small rocks or stuff not to be interacted with)
    OnlyRenderInstances = 1 << 2,
}


// Extra data that tells us how to render billboarded/instanced props
public class IndirectExtraPropData {
    public Texture2D billboardAlbedoTexture;
    public Texture2D billboardNormalTexture;
}


// Blittable prop definition (that is also copied on the GPU compute shader)
// World pos, world rot, world scale
public struct BlittableProp {
    // Size in bytes of the blittable prop
    public const int size = 16; 

    // 2 bytes for x,y,z and w (scale)
    public half4 packed_position_and_scale;

    // 3 bytes for rotation (x,y,z)
    // 2 bytes for dispatch index
    // 1 byte for prop variant
    // 2 unused padding bytes
    public half4 packed_rotation_dispatch_index_prop_variant_padding;
}
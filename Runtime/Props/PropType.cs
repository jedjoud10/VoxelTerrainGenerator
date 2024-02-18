using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

// Voxel detail that can be spawned in the world
// Voxel details are either "Props" or "Structures"
// Props can be billboarded, or rendered indirectly and using instancing
// Structures are used for creating custom structures in the world before prop generation
[CreateAssetMenu(menuName = "VoxelTerrain/New Voxel prop")]
public class PropType : ScriptableObject {
    [Serializable]
    public class PropVariantType {
        public GameObject prefab;
        public float billboardCaptureCameraScale = 10.0f;
        public Vector3 billboardCaptureRotation = Vector3.zero;
        public Vector3 billboardCapturePosition = new Vector3(0, 0, 5);
    }

    [Header("Behavior")]
    public List<PropVariantType> variants;
    public PropSpawnBehavior propSpawnBehavior = PropSpawnBehavior.RenderIndirectInstanced | PropSpawnBehavior.SpawnPrefabs;

    // Max number of props per segment and PER ALL segments
    // Spawning more segments than this will cause them to not be loaded, not very good
    [Min(1)] public int maxPropsPerSegment = 4096;
    [Min(1)] public int maxPropsInTotal = 4096;
    [Min(1)] public int maxVisibleProps = 4096;
    [Min(1)] public float maxInstancingDistance = 1000;

    // Used when the SwapBillboardsForMeshes feature is on
    public Material instancedMeshMaterial;
    public Mesh instancedMesh;

    // Capture settings that apply for ALL variants
    [Header("Billboard Capture")]
    public int billboardTextureWidth = 256;
    public int billboardTextureHeight = 256;
    public FilterMode billboardTextureFilterMode = FilterMode.Bilinear;
    public Vector2 billboardSize = Vector2.one * 10;
    public Vector3 billboardOffset;
    public Vector3 billboardSizeOrigin;

    // How to show the instance / billboards that apply for ALL variants
    [Header("GPU Instance Rendering")]
    public bool instancesRestrictRotationY = false;
    public bool instancesCastShadows = false;

    // Will this prop be generated as a prefab
    public bool WillSpawnPrefab => propSpawnBehavior.HasFlag(PropSpawnBehavior.SpawnPrefabs);

    // Will this prop be rendered as a billboard
    public bool WillRenderIndirectInstances => propSpawnBehavior.HasFlag(PropSpawnBehavior.RenderIndirectInstanced);
}

[Flags]
public enum PropSpawnBehavior {
    None = 0,

    // Enables/disables rendering far away billboards/instances
    RenderIndirectInstanced = 1 << 0,

    // Enables/disables spawning in actual prefabs
    SpawnPrefabs = 1 << 1,

    // Replaces EVERYTHING with prefabs
    SwapForPrefabs = 1 << 2,

    // Swaps out everything for instanced meshes
    // (useful for small rocks or stuff not to be interacted with that we don't want as a gameobject)
    // Only works when we have NO prop prefabs!!!
    SwapForInstancedMeshes = 1 << 3,
}


// Extra data that tells us how to render billboarded/instanced props
// Textures stored as arrays so we can have multiple variants
public class IndirectExtraPropData {
    public Texture2DArray billboardAlbedoTexture;
    public Texture2DArray billboardNormalTexture;
    public Texture2DArray billboardMaskTexture;
}


// Blittable prop definition (that is also copied on the GPU compute shader)
// World pos, world rot, world scale
[StructLayout(LayoutKind.Sequential)]
public struct BlittableProp {
    // Size in bytes of the blittable prop
    public const int size = 16;

    public half pos_x;
    public half pos_y;
    public half pos_z;
    public half scale;

    // 3 bytes for rotation (x,y,z)
    public byte rot_x;
    public byte rot_y;
    public byte rot_z;
    
    // 1 unused padding byte
    public byte _padding;

    // 2 bytes for dispatch index
    public ushort dispatchIndex;

    // 1 byte for prop variant
    public byte variant;

    // 1 unused padding bytes
    public byte _padding1;
}
using Unity.Mathematics;

// Created from the octree loader objects
// All parameters are stored in octree space
public struct TerrainLoaderTarget {
    // Should we generate collisions for chunks generated around this target?
    public bool generateCollisions;

    // Position of the target in world space (very small coordinates)
    public float3 center;

    // Radius in world space
    public float radius;

    // Extents around the terrain loader where we must spawn prop segments
    public uint3 propSegmentExtent;

    // Multiplier for the LOD system
    public float propSegmentLodMultiplier;
}

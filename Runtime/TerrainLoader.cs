using System;
using Unity.Mathematics;
using UnityEngine;

// Will be used by the prop system to load prop segments and spawn props
public class TerrainLoader : MonoBehaviour {
    [Serializable]
    public struct Target {
        // Should we generate collisions for chunks generated around this target?
        public bool generateCollisions;

        // Position of the target in world space (very small coordinates)
        [HideInInspector]
        public float3 center;

        // Radius in world space
        [Min(0.001F)]
        public float radius;

        // Should the terrain loader force the voxel props to generated prop segments
        public bool affectsVoxelProps;

        // Extents around the terrain loader where we must spawn prop segments
        public uint3 propSegmentExtent;

        // Multiplier for the prop segment lod system
        public float propSegmentLodMultiplier;

        // Max distance we can move before we must regenerate the octree around us
        [Min(0.001F)]
        public float maxDistanceThreshold;
    }

    public Target data = new Target {
        generateCollisions = false,
        center = float3.zero,
        radius = 16f,
        affectsVoxelProps = true,
        propSegmentExtent = new uint3(5, 1, 5),
        maxDistanceThreshold = 1F,
    };
    private Vector3 last;
    private VoxelOctree octree;
    public Camera viewCamera;

    void Start() {
        last = transform.position;
    }

    // NOTE: How do we do this in a multiplayer setting?
    // will we force the host to load all terrain around all players
    // or will it only trust the player inputs and the fact that the players themselves will generate terrain?

    void Update() {
        if (VoxelTerrain.Instance != null) {
            // Initialize both octree and props
            bool bruhtonium = false;
            if (octree == null) {
                octree = VoxelTerrain.Instance.VoxelOctree;

                if (VoxelTerrain.Instance.VoxelOctree.target == null) {
                    octree.target = this;
                    bruhtonium = true;
                    octree.mustUpdate = true;
                } else {
                    Debug.LogWarning("Already have a target. Multi-target support has been removed already!!");
                }
            }

            // Update octree variables (pass in required prop segment mul)
            if (Vector3.Distance(transform.position, last) > data.maxDistanceThreshold || bruhtonium) {
                data.center = transform.position;
                octree.mustUpdate = true;
            }
        }
    }
}
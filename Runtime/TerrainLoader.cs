using Unity.Mathematics;
using UnityEngine;

// Will be used by the prop system to load prop segments and spawn props
public class TerrainLoader : MonoBehaviour {
    // How many prop segments we should spawn around the terrain loader
    public uint3 propSegmentExtent = new uint3(1, 1, 1);

    // Multiplier for the prop segment lod system
    public float propSegmentLodMultiplier = 1f;

    // Should the terrain loader force the voxel props to generated prop segments
    public bool affectsVoxelProps = true;

    // Should we generate collisions for chunks generated around this loader?
    public bool generateCollisions = false;

    // Radius in world space
    [Min(0.001F)]
    public float radius = 16.0F;

    // Max distance we can move before we must regenerate the octree around us
    [Min(0.001F)]
    public float maxDistanceThreshold = 1.0F;

    private Vector3 last;
    private VoxelOctree octree;
    private VoxelProps props;

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
            if (octree == null && props == null) {
                octree = VoxelTerrain.Instance.VoxelOctree;
                octree.targetsLookup.Add(this, octree.targets.Length);
                octree.targets.Add(new TerrainLoaderTarget {
                    generateCollisions = generateCollisions,
                    center = transform.position,
                    radius = radius,
                });
                bruhtonium = true;
            }

            // Update octree variables (pass in required prop segment mul)
            if (Vector3.Distance(transform.position, last) > maxDistanceThreshold || bruhtonium) {
                if (octree.Free) {
                    int index = octree.targetsLookup[this];
                    octree.targets[index] = new TerrainLoaderTarget {
                        generateCollisions = generateCollisions,
                        center = transform.position,
                        radius = radius,
                        propSegmentExtent = propSegmentExtent,
                        propSegmentLodMultiplier = propSegmentLodMultiplier,
                    };

                    last = transform.position;
                    octree.mustUpdate = true;
                }
            }
        }
    }

    private void OnDestroy() {
        // uhhhhhhhhhhhhhhhhhhhh
        if(props != null) {
            props.targets.Remove(this);
        }
    }
}
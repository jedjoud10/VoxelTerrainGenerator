using UnityEngine;

// Will be used by the prop system to load prop segments and spawn props
public class TerrainLoader : MonoBehaviour {
    // Multiplier that is applied to octree nodes with the same size as prop segments
    // to force them to be generated and spawned in the world as chunks.
    // Result of this is a lot more prop segments throughout the world
    [Min(1.0f)]
    public float octreePropSegmentNodeMultiplier = 2;

    // Maximum distance in which prop segments will become LOD0 (prefab spawner)
    public float propSegmentPrefabSpawnerMaxDistance = 800f;

    // Maximum distance in which prop segments will become LOD1 (indirect renderer)
    public float propSegmentInstancedRendererLodMaxDistance = 1400f;

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
                props = VoxelTerrain.Instance.VoxelProps;
                octree.targetsLookup.Add(this, octree.targets.Length);
                octree.targets.Add(new OctreeTarget {
                    generateCollisions = generateCollisions,
                    center = transform.position,
                    radius = radius,
                });
                props.targets.Add(this);
                bruhtonium = true;
            }

            // Update octree variables (pass in required prop segment mul)
            if (Vector3.Distance(transform.position, last) > maxDistanceThreshold || bruhtonium) {
                if (octree.Free) {
                    int index = octree.targetsLookup[this];
                    octree.targets[index] = new OctreeTarget {
                        octreePropSegmentNodeMultiplier = octreePropSegmentNodeMultiplier,
                        generateCollisions = generateCollisions,
                        center = transform.position,
                        radius = radius,
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
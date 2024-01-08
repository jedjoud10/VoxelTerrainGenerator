using UnityEngine;

// Will be used by the octree system to load specific regions of the map
public class OctreeLoader : MonoBehaviour {
    // Should we generate collisions for chunks generated around this loader?
    public bool generateCollisions = false;

    // Radius in world space
    [Min(0.001F)]
    public float radius = 32.0F;

    // Max distance we can move before we must regenerate the octree around us
    [Min(0.001F)]
    public float maxDistanceThreshold = 16.0F;

    private Vector3 last;
    private VoxelOctree octree;

    void Start() {
        last = transform.position;
    }

    void Update() {
        if (VoxelTerrain.Instance != null) {
            if (octree == null) {
                octree = VoxelTerrain.Instance.VoxelOctree;
                octree.TryUpdateOctreeLoader(this);
            }

            if (Vector3.Distance(transform.position, last) > maxDistanceThreshold) {
                if (octree.TryUpdateOctreeLoader(this)) {
                    last = transform.position;
                }
            }
        }


    }
}
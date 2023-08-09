using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Unity.Mathematics;
using UnityEngine;

// Will be used by the octree system to load specific regions of the map
public class OctreeLoader : MonoBehaviour
{
    // Should we generate collisions for chunks generated around this loader?
    public bool generateCollisions = false;

    // Radius in world space
    [Min(0.001F)]
    public float radius = 32.0F;

    // Priority to take over other octree loaders
    public int priority = 0;

    // Max distance we can move before we must regenerate the octree around us
    [Min(0.001F)]
    public float maxDistanceThreshold = 16.0F;

    private Vector3 last;
    public VoxelOctree octree;

    void Start()
    {
        octree.UpdateOctreeLoader(this);
        last = transform.position;
    }

    void Update()
    {    
        if (Vector3.Distance(transform.position, last) > maxDistanceThreshold)
        {
            last = transform.position;
            octree.UpdateOctreeLoader(this);
        }
    }
}
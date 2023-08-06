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

    // LOD multiplier unique to this taget
    [Min(0.001F)]
    public float lodMultiplier = 1.0F;
}
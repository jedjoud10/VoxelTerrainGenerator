using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Unity.Mathematics;
using UnityEngine;

// Created from the octree loader objects
// All parameters are stored in octree space
public struct OctreeTarget
{
    // Should we generate collisions for chunks generated around this target?
    public bool generateCollisions;

    // Position of the target in world space (very small coordinates)
    public float3 center;

    // Radius in world space
    public float radius;

    // LOD multiplier unique to this taget
    public float lodMultiplier;
}

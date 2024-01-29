using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// A prop segment that simply exists abstractly in the world. Not associated with a game object
public class PropSegment {
    // Internal position
    public int3 segmentPosition;

    // World space position
    public Vector3 worldPosition;

    // 0 => spawn gameobjects 
    // 1 => billboard
    public bool spawnPrefabs;

    // bitmask containing all the generated prop types for this segment
    public BitField32 generatedProps;

    public Dictionary<int, (ComputeBuffer, int)> test;
    public Dictionary<int, List<GameObject>> props;
}
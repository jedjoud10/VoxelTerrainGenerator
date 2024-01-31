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

    // Props that are handled by this prop segment
    public Dictionary<int, List<GameObject>> props;

    // Lookup index we use when destroying this prop segment and need to get rid of the billboards
    public int indexRangeLookup;
}
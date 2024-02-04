using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A prop segment that simply exists abstractly in the world. Not associated with a game object
public class PropSegment {
    // Internal position
    public int3 segmentPosition;

    // World space position
    public Vector3 worldPosition;

    // Basically denotes how close the user is to the prop segment
    public bool spawnPrefabs;

    // Props that are handled by this prop segment
    // Assumes that there is a maximum of 1 variant per type per dispatch group
    public Dictionary<int, (List<GameObject>, List<ushort>)> props;

    // Lookup index we use when destroying this prop segment and need to get rid of the billboards
    public int indexRangeLookup;
}
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A voxel region is used to generate props and structures
// Voxel regions are stored in the main voxel regions terrain component
// We must split the world in equal "regions" so that prop spawn density and
// structure spawn density stay the same across LODs, otherwise density would not be consistent
public class Segment {
    // Internal position
    public int3 regionPosition;

    // World space position
    public Vector3 worldPosition;

    // Basically denotes how close the user is to the prop region
    public bool spawnPrefabs;

    // Props that are handled by this prop region
    // Assumes that there is a maximum of 1 variant per type per dispatch group
    public Dictionary<int, (List<GameObject>, List<ushort>)> props;

    // Lookup index we use when destroying this prop region and need to get rid of the billboards or instanced props
    public int indexRangeLookup;
}
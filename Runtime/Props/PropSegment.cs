using System.Collections.Generic;
using UnityEngine;

// A prop segment that simply exists abstractly in the world. Not associated with a game object
public class PropSegment {
    // World space position
    public Vector3 position;

    // 0 => spawn gameobjects 
    // 1 => indirectly draw the props
    // 2 => billboard
    public int lod;

    public Dictionary<int, (ComputeBuffer, int)> test;
    public Dictionary<int, List<GameObject>> props;
}
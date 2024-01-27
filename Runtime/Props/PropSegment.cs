using System.Collections.Generic;
using UnityEngine;

// A prop segment that simply exists abstractly in the world. Not associated with a game object
public class PropSegment {
    // 0 => spawn gameobjects 
    // 1 => indirectly draw the props
    // 2 => billboard
    public int lod;

    // Game object that contains spawned props
    public GameObject owner;
}
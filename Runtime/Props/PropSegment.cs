using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A prop segment that either contains gameobject props or indirectly drawn props
public class PropSegment : MonoBehaviour {
    // 0 => spawn gameobjects 
    // 1 => indirectly draw the props
    // 2 => billboard
    public int lod;
}
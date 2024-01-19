using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Blittable prop definition (that is also copied on the GPU compute shader)
// World pos, worl rot, world scale
public struct BlittableProp {
    public float4 position_and_scale;
}
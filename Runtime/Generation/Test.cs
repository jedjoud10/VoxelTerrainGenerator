using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Test : VoxelGraph {
    public override void Execute(Var<float3> position, out Var<float> density, out Var<uint> material) {
        density = position.y();
        material = 0;
    }
}

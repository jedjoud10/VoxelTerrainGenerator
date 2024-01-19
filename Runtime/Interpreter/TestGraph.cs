using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class TestGraph : Graph {
    public override Var<uint> PropGraph(Var<float3> position) {
        return Var<uint>.Identity;
    }

    public override (Var<half>, Var<uint>) VoxelGraph(Var<float3> position) {
        Var<float> y = position.y() + position.z();
        Var<float> result = snoise(position);


        return (position.y(), Var<uint>.Identity);
    }
}
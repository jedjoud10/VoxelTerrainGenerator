using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class TestGraph : Graph {
    public override Node<uint> PropGraph(Node<float3> position) {
        return Node<uint>.Identity;
    }

    public override (Node<half>, Node<uint>) VoxelGraph(Node<float3> position) {
        Node<float> y = position.y();


        return (position.y(), Node<uint>.Identity);
    }
}
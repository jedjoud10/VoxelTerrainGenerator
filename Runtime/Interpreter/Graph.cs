using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A subgraph contains a specific type of input and outputs
// There are two types of subgraphs currently: density / material and props / props subgraphs
// DM subgraphs are used for density and material gen. Executed at chunk res
// prop subgraphs are executed at the specific prop resolution
public abstract class Graph {
    public abstract (Node<half>, Node<uint>) VoxelGraph(Node<float3> position);
    public abstract Node<uint> PropGraph(Node<float3> position);

}
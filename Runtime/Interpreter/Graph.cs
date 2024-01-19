using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A subgraph contains a specific type of input and outputs
// There are two types of subgraphs currently: density / material and props / props subgraphs
// DM subgraphs are used for density and material gen. Executed at chunk res
// prop subgraphs are executed at the specific prop resolution

// "ports" are variables
// "nodes" are operators
public abstract class Graph {
    public abstract (Var<half>, Var<uint>) VoxelGraph(Var<float3> position);
    public abstract Var<uint> PropGraph(Var<float3> position);

    public Var<float> snoise(Var<float3> test) { return test.y(); }

}
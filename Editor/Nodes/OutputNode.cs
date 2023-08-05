using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GraphProcessor;
using System.Linq;
using System;
using UnityEngine.Windows;

[System.Serializable]
public struct Testino<T> { }

// Output node that we must write to for density / color / material
[System.Serializable, NodeMenuItem("Outputs/Set Voxel Output")]
public class OutputNode : BaseNode
{
    [Input(name = "density")]
    public Testino<float> inputs;


    public override string name => "Set Voxel Output";

    public void GetInputs(List<SerializableEdge> edges)
    {
        // inputs = edges.Select(e => (float)e.passThroughBuffer);
    }
}
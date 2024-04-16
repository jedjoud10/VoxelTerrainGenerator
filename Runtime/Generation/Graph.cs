using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A voxel graph is the base class to inherit from to be able to write custom voxel stuff
public abstract class VoxelGraph {

    // Execute the voxel graph at a specific position and fetch the density and material values
    public abstract void Execute(Var<float3> position, out Var<float> density, out Var<uint> material);

    // This transpile the voxel graph into HLSL code that can be executed on the GPU
}

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// A voxel graph is the base class to inherit from to be able to write custom voxel stuff
[ExecuteInEditMode]
public class VoxelGraph: MonoBehaviour {
    // Execute the voxel graph at a specific position and fetch the density and material values
    public virtual void Execute(Var<float3> position, out Var<float> density, out Var<uint> material) {
        density = position.y();



        material = 0;
    }

    // This transpile the voxel graph into HLSL code that can be executed on the GPU
    // This can be done outside the editor, but shader compilation MUST be done in editor
    public string Transpile() {
        return "";
    }
}

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Voxel data that is contained within a 64x64x64 native array
public class VoxelData
{
    public NativeArray<float> data;

    public VoxelData(NativeArray<float> nativeArray)
    {

    }
}
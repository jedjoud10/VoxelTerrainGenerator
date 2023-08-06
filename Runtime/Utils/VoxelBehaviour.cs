using System.Runtime.InteropServices;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Used internally by the classes that handle terrain
public abstract class VoxelBehaviour : MonoBehaviour
{
    // Initialize the voxel behaviour (called from the voxel terrain)
    internal abstract void Init();

    // Dispose of any internally stored memory
    internal abstract void Dispose();
}

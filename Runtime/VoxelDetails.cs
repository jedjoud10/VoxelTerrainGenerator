using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Single voxel detail value
public class VoxelDetailTest
{
    public enum GenerationMethod 
    {
        CellDensity,
        Ray,
    }

    public GenerationMethod method;
    public Vector3 localStartRayPosition;
    public Vector3 localEndRayPosition;
}

// Responsible for generating the voxel details and using procedural instanced rendering for the billboards at a distance
public class VoxelDetails : MonoBehaviour
{
    public List<VoxelDetailTest> details;
}

using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.UIElements;

// Copies the temp triangulation data to the permanent location where we store the offsets too
[BurstCompile(CompileSynchronously = true)]
public struct CopyJob : IJobParallelFor
{
    public void Execute(int index)
    {
    }
}
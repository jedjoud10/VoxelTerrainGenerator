using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Fiter job that will store the locations of completely empty / filled segments in the mesh to speed up meshing
[BurstCompile(CompileSynchronously = true)]
public struct FilterJob : IJobParallelFor
{
    public void Execute(int index)
    {
    }
}
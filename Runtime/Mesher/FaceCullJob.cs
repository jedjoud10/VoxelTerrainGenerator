using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


// Detects the faces that are completely filled or completely empty
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public struct FaceCullJob : IJobParallelFor {
    public void Execute(int index) {
    }
}
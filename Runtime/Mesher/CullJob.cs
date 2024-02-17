using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Given the main octree structure, this job will tell us what chunks should be COMPLETELY invisible to the camera
// Basically occlusion culling but using the voxel data to help us cull
// This will fetch the "chunk face" data (whether a face is completely hidden or slightly visible)
// and use that data to allow us to cull the chunks
[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public struct CullJob : IJobParallelFor {
    public void Execute(int index) {
    }
}
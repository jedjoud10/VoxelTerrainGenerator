using UnityEngine;
using Unity.Jobs;
using Unity.Burst;


// Bakes the collision mesh for the given voxel meshes
[BurstCompile(CompileSynchronously = true)]
public struct BakeJob : IJob {
    public int meshId;
    public void Execute() {
        Physics.BakeMesh(meshId, false);
    }
}
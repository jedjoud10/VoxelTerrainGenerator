using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Queued up mesh job that we are waiting to begin
internal struct PendingMeshJob
{
    public VoxelChunk chunk;
    public VoxelTempContainer container;
    public bool computeCollisions;
    public JobHandle dependency;

    public static PendingMeshJob Empty = new PendingMeshJob
    {
        chunk = null,
        container = null,
        computeCollisions = false
    };
}
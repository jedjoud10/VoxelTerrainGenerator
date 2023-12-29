using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Queued up mesh job that we are waiting to begin
internal struct PendingMeshJob {
    public VoxelChunk chunk;
    public bool computeCollisions;

    public static PendingMeshJob Empty = new PendingMeshJob {
        chunk = null,
        computeCollisions = false
    };
}
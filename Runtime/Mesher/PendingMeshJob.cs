// Queued up mesh job that we are waiting to begin
internal struct PendingMeshJob {
    public VoxelChunk chunk;
    public bool collisions;
    public int maxFrames;

    public static PendingMeshJob Empty = new PendingMeshJob {
        chunk = null,
        collisions = false,
        maxFrames = 0
    };
}
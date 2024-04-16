using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// CPU representation of what a voxel is. The most important value here is the density value
[StructLayout(LayoutKind.Sequential)]
public struct Voxel {
    public const int size = sizeof(int);

    // Density of the voxel as a half to save some memory
    public half density;

    // Material of the voxel that depicts its color and other parameters
    public byte material;

    // Used for extra color data on a per vertex basis
    public byte _padding;

    // Empty voxel with the empty material
    public readonly static Voxel Empty = new Voxel {
        density = half.zero,
        material = byte.MaxValue,
        _padding = 0,
    };
}

// Delta data that contains the voxel values for any arbitrarily sized chunk
public struct SparseVoxelDeltaData {
    public float3 position;
    public float scalingFactor;

    // Densities that we will compress using a lossless compression algorithm
    public NativeArray<half> densities;

    // Byte that we will compress using RLE
    // byte.max represents a value that the user has not modified yet
    public NativeArray<byte> materials;

    // Job handle for the "apply" task for this sparse voxel data
    public JobHandle applyJobHandle;

    // Last counters for the chunk for this delta node
    // Used to calculate delta counter values
    public NativeArray<int> lastCounters;

    // Create sparse voxel data for an unnaffected delta chunk
    public static SparseVoxelDeltaData Empty = new SparseVoxelDeltaData {
        densities = default,
        materials = default,
        applyJobHandle = new JobHandle(),
        position = new float3(0, 0, 0),
        scalingFactor = -1,
        lastCounters = default,
    };
}

// Voxel container with custom dispose methods
// (implemented for voxel readback request and voxel edit request)
public abstract class VoxelTempContainer {
    public NativeArray<Voxel> voxels;
    public VoxelChunk chunk;

    // Dispose of the voxel container
    public abstract void TempDispose();
}
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;

// CPU representation of what a voxel is
// I don't think I'll have bigger types than this so wtv
// In any case if we want to generate more fields than this
// we'll just implement a custom system for that bra
[StructLayout(LayoutKind.Sequential)]
public struct Voxel
{
    // Density of the voxel as a half to save some memory
    public half density;

    // Material of the voxel that depicts its color and other parameters
    public ushort material;

    // Empty voxel with the empty material
    public readonly static Voxel Empty = new Voxel
    {
        density = half.zero,
        material = ushort.MaxValue
    };
}

// Sparse voxel data (SoA) that will be stored on disk / serialized / deserialized
// This will correspond to a single segment's chunk
[StructLayout(LayoutKind.Sequential)]
public struct SparseVoxelData
{
    // Densities that we will compress using a lossless compression algorithm
    // TODO: I need to find a compression algo that works with the unity C# job system
    public NativeArray<half> densities;

    // Ushorts that we will compress using RLE
    // ushort.max represents a value that the user has not modified yet
    public NativeArray<ushort> materials;

    // Create sparse voxel data for an unnaffected delta chunk
    public static SparseVoxelData Empty = new SparseVoxelData
    {
        densities = default,
        materials = default
    };

}

// A segment is a collection of 4x4x4 chunks in the world
[StructLayout(LayoutKind.Sequential)]
public struct VoxelSegment
{
    // Bitset containing the chunks that are active
    public ulong bitset;

    // Starting index of the sparse voxel data
    public int startingIndex;
}

// Voxel container with custom dispose methods
// (implemented for voxel readback request and voxel edit request)
public abstract class VoxelTempContainer
{
    public NativeArray<Voxel> voxels;
    public VoxelChunk chunk;

    // Dispose of the voxel container
    public abstract void TempDispose();
}
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

// Voxel container with custom dispose methods
// (implemented for voxel readback request and voxel edit request)
public abstract class VoxelTempContainer
{
    public NativeArray<Voxel> voxels;
    public VoxelChunk chunk;

    // Dispose of the voxel container
    public abstract void TempDispose();
}
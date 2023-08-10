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

    private ushort _padding;
}

[StructLayout(LayoutKind.Sequential)]
public struct Voxel4
{
    public half density0;
    public ushort material0;
    private ushort _padding0;
    public half density1;
    public ushort material1;
    private ushort _padding1;
    public half density2;
    public ushort material2;
    private ushort _padding2;
    public half density3;
    public ushort material3;
    private ushort _padding3;
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
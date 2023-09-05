using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// How I think we will deal with GPU accelerated noise generation
/// We have multiple "passes" that can generate noise on the GPU
/// Each "pass" can be either executed by a 2D dispatch or a 3D dispatch
/// Passes of the same dimensionality will be combined into the same texture (RGBA texture)
/// Passes output halfs into the RGBA texture (so at max, we can have 8 passes per texture)
/// Each "pass" has a type to specify the type of noise it should generate (simplex, voronoi, random)
/// Each "pass" has "features" that can be conditionally enabled to make the noise more interesting (fBm, billow, inversion)
/// </summary>

// Pass features for each pass
[Flags]
public enum PassFeatures
{
    None = 0,
    Fractal = 1,
    Billow = 2,
    Inverted = 4
}

// Noise type that we will generate
public enum NoiseType
{
    Random,
    Simplex,
    VoronoiF1,
    VoronoiF2,
}

// Dimensionality of the noise
public enum NoiseDimensionality
{
    Two,
    Three,
}

public sealed class Noise
{
    public PassFeatures features;
    public NoiseType type;
    public NoiseDimensionality dimensionality;
    public float3 shift;
    public float3 scale;
    public float factor;
    public float lacunarity;
    public float persistence;
    public uint octaves;
}


public class VoxelGenerator2
{
}

public class VoxelAccelerator
{
    // Request to generate some noise using the GPU and read it back
    public void RequestGpuAcceleratedNoise() { }
}


public interface IVoxelGenerator
{
    // Called before we generate any voxels to cache any resources
    public void Cache(VoxelAccelerator cache);

    // Generate the voxel at the given position
    public Voxel Generate(float3 position);
}

public interface IVoxelPass
{
    // Dimensionality of the voxel pass. Depicts how voxel noise should be generated ong
    public int Dimensionality();
}
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
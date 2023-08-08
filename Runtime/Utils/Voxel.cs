using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// CPU representation of what a voxel is
public struct Voxel
{
    public half density;
    public byte material;
}
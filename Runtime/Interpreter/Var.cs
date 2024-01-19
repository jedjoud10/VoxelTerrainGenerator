using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Simple node within the voxel graph interpreter
// All nodes can be defaulted to ZERO (simply represent POD types anyways)
public struct Var<T> {
    public static Var<T> Identity { get; }

    public static implicit operator Var<T>(Var<float> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<half> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<uint> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<int> d) => Var<T>.Identity;

    public static Var<T> operator +(Var<T> a, Var<T> b) => a;
} 
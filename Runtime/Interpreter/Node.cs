using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Simple node within the voxel graph interpreter
// All nodes can be defaulted to ZERO (simply represent POD types anyways)
public struct Node<T> {
    public static Node<T> Identity { get; }

    public static implicit operator Node<T>(Node<float> d) => Node<T>.Identity;
    public static implicit operator Node<T>(Node<half> d) => Node<T>.Identity;
    public static implicit operator Node<T>(Node<uint> d) => Node<T>.Identity;
    public static implicit operator Node<T>(Node<int> d) => Node<T>.Identity;
} 
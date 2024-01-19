using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
public static class VoxelInterpreterUtils {
    public static Node<float> x(this Node<float3> vec3) {
        return Node<float>.Identity;
    }
    public static Node<float> y(this Node<float3> vec3) {
        return Node<float>.Identity;
    }
    public static Node<float> z(this Node<float3> vec3) {
        return Node<float>.Identity;
    }
}
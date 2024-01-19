using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
public static class VoxelInterpreterUtils {
    public static Var<float> x(this Var<float3> vec3) {
        return Var<float>.Identity;
    }
    public static Var<float> y(this Var<float3> vec3) {
        return Var<float>.Identity;
    }
    public static Var<float> z(this Var<float3> vec3) {
        return Var<float>.Identity;
    }
}
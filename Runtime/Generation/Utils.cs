using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Common utils and shorthand forms
public static class Utils {
    // Get the x value of the float3
    public static Var<float> x(this Var<float3> vec3) {
        return Var<float>.Identity;
    }

    // Get the y value of the float3
    public static Var<float> y(this Var<float3> vec3) {
        return Var<float>.Identity;
    }

    // Get the z value of the float3
    public static Var<float> z(this Var<float3> vec3) {
        return Var<float>.Identity;
    }

    // Evaluate an snoise type noise at a specific position. Short-hand for creating snoise and evaluating it
    public static Var<float> Snoise<T>(this Noise, Var<T> position, Var<float> scale) {
        Noise n = new Noise(1.0f, scale);
        n.type = Noise.Type.Simplex;
        return n.Evaluate(position);
    }

}

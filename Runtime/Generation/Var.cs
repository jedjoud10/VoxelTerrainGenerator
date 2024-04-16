using Codice.Client.BaseCommands.Merge;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Var<T> {
    // the name of the variable. automatically generated
    public string Name { get; internal set; }

    // is the current value defined in the code as a constant (or an editor variable)
    public bool IsStatic { get; internal set; }

    // variable name that will be substituted
    public string CodeName { get; internal set; }

    // Dimensionality of a variable
    public enum Dimensions {
        One,
        Two,
        Three,
        Undefined
    }

    // Get the dimensionality of the variable
    public Dimensions Dimensionality {
        get {
            if (typeof(T) == typeof(float)) {
                return Dimensions.One;
            } else if (typeof(T) == typeof(float2)) {
                return Dimensions.Two;
            } else if (typeof(T) == typeof(float3)) {
                return Dimensions.Three;
            }

            return Dimensions.Undefined;
        }
    }

    // Null, zero, or identity value
    public static Var<T> Identity {
        get {
            return new Var<T> {
                Name = "identity_" + typeof(T).FullName,
                IsStatic = false,
                CodeName = "identity_" + typeof(T).FullName,
            };
        }
    }

    // Implicitly convert a constant value to a variable
    public static implicit operator Var<T>(T value) => new Var<T> {
        Name = "_st_",
        IsStatic = true,
        CodeName = "_st_"
    };

    // Implict conversion from common types to this type
    public static implicit operator Var<T>(Var<double> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<float> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<uint> d) => Var<T>.Identity;
    public static implicit operator Var<T>(Var<int> d) => Var<T>.Identity;

    // Inject a custom variable that will update its value dynamically based on the given callback 
    // Mainly used to pass inputs from fields from the unity editor to the graph
    public static Var<T> Inject(Func<T> callback) { return null; }

    // Common operators
    public static Var<T> operator +(Var<T> a, Var<T> b) => a;
    public static Var<T> operator -(Var<T> a, Var<T> b) => a;
    public static Var<T> operator *(Var<T> a, Var<T> b) => a;
    public static Var<T> operator /(Var<T> a, Var<T> b) => a;
}
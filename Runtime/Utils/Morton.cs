// Stolen from https://github.com/johnsietsma/InfPoints/blob/master/com.infpoints/Runtime/Morton.cs

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using Unity.Mathematics;
/// <summary>
/// Morton order.
/// See http://asgerhoedt.dk/?p=276 for an overview.
/// Encoding and decoding functions adapted from https://fgiesen.wordpress.com/2009/12/13/decoding-morton-codes/.
/// Original code in the public domain (https://fgiesen.wordpress.com/2011/01/17/texture-tiling-and-swizzling/#comment-858)
/// Added 64 bit versions.
/// </summary>
public static class Morton {
    /// <summary>
    /// The maximum value that a coordinate can be when encoding to a 32 bit morton code.
    /// </summary>
    public static readonly uint MaxCoordinateValue32 = 0b0011_1111_1111;  // 1023

    /// <summary>
    /// The maximum value that a coordinate can be when encoding to a 64 bit morton code.
    /// </summary>
    public static readonly uint MaxCoordinateValue64 = 0b0001_1111_1111_1111_1111_1111;  // 2097151, (8^7)-1

    /// <summary>
    /// Encode a 3 dimensional coordinate to morton code.
    /// Check MaxCoordinateVale32 for numeric limits.
    /// </summary>
    /// <param name="coordinate">x,y,z coordinate</param>
    /// <returns>The morton code</returns>
    public static uint EncodeMorton32(uint3 coordinate) {
        DebugCheckLimits32(coordinate);
        return (Part1By2_32(coordinate.z) << 2) + (Part1By2_32(coordinate.y) << 1) + Part1By2_32(coordinate.x);
    }

    /// <summary>
    /// Vectorised version. This will take the "packed" coordinates and Burst will auto-vectorise so four encodings
    /// happen for the price of one. 
    /// </summary>
    /// <param name="coordinateX">(xxxx)</param>
    /// <param name="coordinateY">(yyyy)</param>
    /// <param name="coordinateZ">(zzzz)</param>
    /// <returns>The 4 morton codes</returns>
    public static uint4 EncodeMorton32(uint4 coordinateX, uint4 coordinateY, uint4 coordinateZ) {
        DebugCheckLimits32(coordinateX);
        DebugCheckLimits32(coordinateY);
        DebugCheckLimits32(coordinateZ);
        return (Part1By2_32(coordinateZ) << 2) + (Part1By2_32(coordinateY) << 1) + Part1By2_32(coordinateX);
    }

    /// <summary>
    /// 64bit version of EncodeMorton32. Produces a ulong rather then a uint.
    /// </summary>
    /// <param name="coordinates"></param>
    /// <returns>The morton code</returns>
    public static ulong EncodeMorton64(uint3 coordinates) {
        DebugCheckLimits64(coordinates);
        return (Part1By2_64(coordinates.z) << 2) + (Part1By2_64(coordinates.y) << 1) + Part1By2_64(coordinates.x);
    }

    /// <summary>
    /// Transform a morton code to a (x,y,z) coordinate.
    /// </summary>
    /// <param name="code">The morton code</param>
    /// <returns>The (x,y,z) coordinate</returns>
    public static uint3 DecodeMorton32(uint code) {
        var x = Compact1By2_32(code);
        var y = Compact1By2_32(code >> 1);
        var z = Compact1By2_32(code >> 2);
        return new uint3(x, y, z);
    }

    /// <summary>
    /// SIMD version. Pass in four codes as a single unit4, it will be auto-vectorised by Burst.
    /// </summary>
    /// <param name="code">Four morton codes</param>
    /// <returns>For sets of coordinates, packed as (x,x,x,x),(y,y,y,y),(z,z,z,z)</returns>
    public static uint4x3 DecodeMorton32(uint4 code) {
        var x = Compact1By2_32(code);
        var y = Compact1By2_32(code >> 1);
        var z = Compact1By2_32(code >> 2);
        return new uint4x3(x, y, z);
    }

    /// <summary>
    /// 64 bit version of <see cref="DecodeMorton32"/>
    /// There is no long4 type, so we can't take advantage of Burst auto-vectorisation of long types.
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    public static uint3 DecodeMorton64(ulong code) {
        var x = Compact1By2_64(code);
        var y = Compact1By2_64(code >> 1);
        var z = Compact1By2_64(code >> 2);
        return new uint3(x, y, z);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    static void DebugCheckLimits32(uint3 coordinates) {
        if (math.cmax(coordinates) > MaxCoordinateValue32) {
            throw new OverflowException(
                $"An element of coordinates {coordinates} is larger then the maximum {MaxCoordinateValue32}");
        }
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    static void DebugCheckLimits32(uint4 packedCoordinate) {
        if (math.cmax(packedCoordinate) > MaxCoordinateValue32) {
            throw new OverflowException(
                $"One of the coordinates in {packedCoordinate} is larger then the maximum {MaxCoordinateValue32}");
        }
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    static void DebugCheckLimits64(uint3 coordinates) {
        if (math.cmax(coordinates) > MaxCoordinateValue64) {
            throw new OverflowException(
                $"An element of coordinates {coordinates} is larger then the maximum {MaxCoordinateValue32}");
        }
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    static void DebugCheckLimits64(uint4 packedCoordinate) {
        if (math.cmax(packedCoordinate) > MaxCoordinateValue64) {
            throw new OverflowException(
                $"An element of coordinates {packedCoordinate} is larger then the maximum {MaxCoordinateValue32}");
        }
    }

    // "Insert" two 0 bits after each of the 10 low bits of x
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Part1By2_32(uint x) {
        x &= 0b0011_1111_1111;  // x = ---- ---- ---- ---- ---- --98 7654 3210
        x = (x ^ (x << 16)) & 0b1111_1111_0000_0000_0000_0000_1111_1111;  // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x << 8)) & 0b0000_0011_0000_0000_1111_0000_0000_1111;  // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x << 4)) & 0b0000_0011_0000_1100_0011_0000_1100_0011;  // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x << 2)) & 0b0000_1001_0010_0100_1001_0010_0100_1001;  // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        return x;
    }

    // SIMD friendly version
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint4 Part1By2_32(uint4 x) {
        x &= 0x000003ff;                  // x = ---- ---- ---- ---- ---- --98 7654 3210
        x = (x ^ (x << 16)) & 0xff0000ff; // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x << 8)) & 0x0300f00f;  // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x << 4)) & 0x030c30c3;  // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x << 2)) & 0x09249249;  // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong Part1By2_64(uint x) {
        ulong x64 = x;
        //                                                                          x = --10 9876 5432 1098 7654 3210
        x64 &= 0b0011_1111_1111_1111_1111_1111;

        //                             x = ---- ---0 9876 ---- ---- ---- ---- ---- ---- ---- ---- 5432 1098 7654 3210
        x64 = (x64 ^ (x64 << 32)) & 0b0000_0001_1111_0000_0000_0000_0000_0000_0000_0000_0000_1111_1111_1111_1111;

        //                             x = ---- ---0 9876 ---- ---- ---- ---- 5432 1098 ---- ---- ---- ---- 7654 3210
        x64 = (x64 ^ (x64 << 16)) & 0b0000_0001_1111_0000_0000_0000_0000_1111_1111_0000_0000_0000_0000_1111_1111;

        //                        x = ---0 ---- ---- 9876 ---- ---- 5432 ---- ---- 1098 ---- ---- 7654 ---- ---- 3210
        x64 = (x64 ^ (x64 << 8)) & 0b0001_0000_0000_1111_0000_0000_1111_0000_0000_1111_0000_0000_1111_0000_0000_1111;

        //                        x = ---0 ---- 98-- --76 ---- 54-- --32 ---- 10-- --98 ---- 76-- --54 ---- 32-- --10
        x64 = (x64 ^ (x64 << 4)) & 0b0001_0000_1100_0011_0000_1100_0011_0000_1100_0011_0000_1100_0011_0000_1100_0011;

        //                        x = ---1 --0- -9-- 8--7 --6- -5-- 4--3 --1- -0-- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        x64 = (x64 ^ (x64 << 2)) & 0b0001_0010_0100_1001_0010_0100_1001_0010_0100_1001_0010_0100_1001_0010_0100_1001;
        return x64;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Compact1By2_32(uint x) {
        x &= 0b0000_1001_0010_0100_1001_0010_0100_1001;  // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        x = (x ^ (x >> 2)) & 0b0000_0011_0000_1100_0011_0000_1100_0011;  // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x >> 4)) & 0b0000_0011_0000_0000_1111_0000_0000_1111;  // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x >> 8)) & 0b1111_1111_0000_0000_0000_0000_1111_1111;  // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x >> 16)) & 0b0000_0000_0000_0000_0000_0011_1111_1111;  // x = ---- ---- ---- ---- ---- --98 7654 3210
        return x;
    }

    // SIMD friendly version
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint4 Compact1By2_32(uint4 x) {
        x &= 0x09249249; // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        x = (x ^ (x >> 2)) & 0x030c30c3; // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x >> 4)) & 0x0300f00f; // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x >> 8)) & 0xff0000ff; // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x >> 16)) & 0x000003ff; // x = ---- ---- ---- ---- ---- --98 7654 3210
        return x;
    }

    // Inverse of Part1By2 - "delete" all bits not at positions divisible by 3
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Compact1By2_64(ulong x) {
        //                  x = ---1 --0- -9-- 8--7 --6- -5-- 4--3 --1- -0-- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        x &= 0b0001_0010_0100_1001_0010_0100_1001_0010_0100_1001_0010_0100_1001_0010_0100_1001;

        //                  x = ---0 ---- 98-- --76 ---- 54-- --32 ---- 10-- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x >> 2)) & 0b0001_0000_1100_0011_0000_1100_0011_0000_1100_0011_0000_1100_0011_0000_1100_0011;

        //                  x = ---0 ---- ---- 9876 ---- ---- 5432 ---- ---- 1098 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x >> 4)) & 0b0001_0000_0000_1111_0000_0000_1111_0000_0000_1111_0000_0000_1111_0000_0000_1111;

        //                  x = ---- ---0 9876 ---- ---- ---- ---- 5432 1098 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x >> 8)) & 0b0000_0001_1111_0000_0000_0000_0000_1111_1111_0000_0000_0000_0000_1111_1111;

        //                  x = ---- ---0 9876 ---- ---- ---- ---- ---- ---- ---- ---- 5432 1098 7654 3210
        x = (x ^ (x >> 16)) & 0b0000_0001_1111_0000_0000_0000_0000_0000_0000_0000_0000_1111_1111_1111_1111;

        //                  x = ---- ---- ---- ---- ---- ---- ---- ---- ---- ---0 9876 5432 1098 7654 3210
        x = (x ^ (x >> 32)) & 0b0000_0000_0000_0000_0000_0000_0000_0000_0000_0001_1111_1111_1111_1111_1111;

        return (uint)x;
    }
}
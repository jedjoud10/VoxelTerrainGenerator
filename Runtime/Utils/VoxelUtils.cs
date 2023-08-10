using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Common voxel utility methods
public static class VoxelUtils
{
    // Scaling value applied to the vertices
    public static float VertexScaling => (float)Size / ((float)Size - 2.0F);

    // Voxel scaling size
    public static int VoxelSizeReduction { get; internal set; }
    public static float VoxelSize => 1F / Mathf.Pow(2F, VoxelSizeReduction);

    // Current chunk resolution
    public static int Size { get; internal set; }
    public static uint UintSize => (uint)Size;

    // Total number of voxels in a volume
    public static int Volume => Size * Size * Size;

    // Create a 3D render texture with the specified size and format
    public static RenderTexture CreateRenderTexture(int size, GraphicsFormat format)
    {
        RenderTexture texture = new RenderTexture(size, size, 0, format);
        texture.height = size;
        texture.width = size;
        texture.depth = 0;
        texture.volumeDepth = size;
        texture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        texture.enableRandomWrite = true;
        texture.Create();
        return texture;
    }

    // Create a 3D texture with the specified size and format
    public static Texture3D CreateTexture(int size, GraphicsFormat format)
    {
        Texture3D texture = new Texture3D(size, size, size, format, TextureCreationFlags.None);
        texture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        texture.Apply();
        return texture;
    }

    // Convert an index to a 3D position
    public static uint3 IndexToPos(int index, int size)
    {
        return Morton.DecodeMorton32((uint)index);
        
        uint index2 = (uint)index;
        uint size2 = (uint)size;
        
        // N(ABC) -> N(A) x N(BC)
        uint y = index2 / (size2 * size2);   // x in N(A)
        uint w = index2 % (size2 * size2);  // w in N(BC)

        // N(BC) -> N(B) x N(C)
        uint z = w / size2;// y in N(B)
        uint x = w % size2;        // z in N(C)
        return new uint3(x, y, z);
        
    }

    // Convert a 3D position into an index
    [return: AssumeRange(0u, 262144)]
    public static int PosToIndex(uint3 position, int size)
    {
        return (int)Morton.EncodeMorton32(position);
        return (int)math.round((position.y * size * size + (position.z * size) + position.x));
    }

    // Calculate normals using first differences
    public static float3 CalculateNormals(uint3 position, ref NativeArray<half> densities, int size)
    {
        float baseDensity = densities[PosToIndex(position, size)];
        float densityOffsetX = densities[PosToIndex(position, size) + 1];
        float densityOffsetY = densities[PosToIndex(position, size) + size];
        float densityOffsetZ = densities[PosToIndex(position, size) + size * size];
    
        return math.normalizesafe(new float3(baseDensity - densityOffsetX, baseDensity - densityOffsetY, baseDensity - densityOffsetZ));
    }

    // Convert a packed color material value to rgb color and uint material
    public static void UnpackColorMaterial(in uint input, out float3 color, out byte material)
    {
        sbyte packedColorX = (sbyte)(input & 0xFF);
        sbyte packedColorY = (sbyte)((input >> 8) & 0xFF);
        sbyte packedColorZ = (sbyte)((input >> 16) & 0xFF);
        material = (byte)((input >> 24) & 0xFF);
        color = math.float3((float)packedColorX / 128.0F, (float)packedColorY / 128.0F, (float)packedColorZ / 128.0F);
    }

    // Sampled the voxel grid using trilinear filtering
    public static float SampleGridInterpolated(float3 position, ref NativeArray<half> densities, int size) {
        float3 frac = math.frac(position);
        uint3 voxPos = (uint3)math.floor(position);

        float d000 = densities[PosToIndex(voxPos, size)];
        float d100 = densities[PosToIndex(voxPos, size) + 1];
        float d010 = densities[PosToIndex(voxPos, size) + size * size];
        float d110 = densities[PosToIndex(voxPos, size) + size * size + 1];

        float d001 = densities[PosToIndex(voxPos, size) + size];
        float d101 = densities[PosToIndex(voxPos, size) + 1 + size];
        float d011 = densities[PosToIndex(voxPos, size) + size * size + size];
        float d111 = densities[PosToIndex(voxPos, size) + size * size + 1 + size];

        float mixed0 = math.lerp(d000, d100, frac.x);
        float mixed1 = math.lerp(d010, d110, frac.x);
        float mixed2 = math.lerp(d001, d101, frac.x);
        float mixed3 = math.lerp(d011, d111, frac.x); 

        float mixed4 = math.lerp(mixed0, mixed2, frac.z);
        float mixed5 = math.lerp(mixed1, mixed3, frac.z);

        float mixed6 = math.lerp(mixed4, mixed5, frac.y);

        return mixed6;
    }

    // Calculate the ambient occlusion factor of a specific vertex based on its normals
    public static float CalculatePerVertexAmbientOcclusion(float3 position, ref NativeArray<half> densities, int size)
    {
        float ao = 0;

        for (int x = 0; x <= 1; x++) {
            for (int y = 0; y <= 1; y++) {
                for (int z = 0; z <= 1; z++) {
                    float3 offset = new float3(x, y, z) * 2 - new float3(1);
                    float3 final = (position + offset * 2 + new float3(1));
                    final = math.clamp(final, float3.zero, new float3(size - 2));
                    float density = SampleGridInterpolated(final, ref densities, size);
                    ao += density > 0 ? 1 : 0;
                }
            }
        }

        return ao / (3*3*3);
    }
}
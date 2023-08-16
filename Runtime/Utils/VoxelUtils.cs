using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static GluonGui.WorkspaceWindow.Views.WorkspaceExplorer.Configuration.ConfigurationTreeNodeCheck;

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

    // Minimum density at which we enable skirting
    public static float MinSkirtDensityThreshold { get; internal set; }

    // Should we enable smoothing when meshing?
    public static bool Smoothing { get; internal set; }

    // Stolen from https://gist.github.com/dwilliamson/c041e3454a713e58baf6e4f8e5fffecd
    public static readonly ushort[] EdgeMasks = new ushort[] {
        0x0, 0x109, 0x203, 0x30a, 0x80c, 0x905, 0xa0f, 0xb06,
        0x406, 0x50f, 0x605, 0x70c, 0xc0a, 0xd03, 0xe09, 0xf00,
        0x190, 0x99, 0x393, 0x29a, 0x99c, 0x895, 0xb9f, 0xa96,
        0x596, 0x49f, 0x795, 0x69c, 0xd9a, 0xc93, 0xf99, 0xe90,
        0x230, 0x339, 0x33, 0x13a, 0xa3c, 0xb35, 0x83f, 0x936,
        0x636, 0x73f, 0x435, 0x53c, 0xe3a, 0xf33, 0xc39, 0xd30,
        0x3a0, 0x2a9, 0x1a3, 0xaa, 0xbac, 0xaa5, 0x9af, 0x8a6,
        0x7a6, 0x6af, 0x5a5, 0x4ac, 0xfaa, 0xea3, 0xda9, 0xca0,
        0x8c0, 0x9c9, 0xac3, 0xbca, 0xcc, 0x1c5, 0x2cf, 0x3c6,
        0xcc6, 0xdcf, 0xec5, 0xfcc, 0x4ca, 0x5c3, 0x6c9, 0x7c0,
        0x950, 0x859, 0xb53, 0xa5a, 0x15c, 0x55, 0x35f, 0x256,
        0xd56, 0xc5f, 0xf55, 0xe5c, 0x55a, 0x453, 0x759, 0x650,
        0xaf0, 0xbf9, 0x8f3, 0x9fa, 0x2fc, 0x3f5, 0xff, 0x1f6,
        0xef6, 0xfff, 0xcf5, 0xdfc, 0x6fa, 0x7f3, 0x4f9, 0x5f0,
        0xb60, 0xa69, 0x963, 0x86a, 0x36c, 0x265, 0x16f, 0x66,
        0xf66, 0xe6f, 0xd65, 0xc6c, 0x76a, 0x663, 0x569, 0x460,
        0x460, 0x569, 0x663, 0x76a, 0xc6c, 0xd65, 0xe6f, 0xf66,
        0x66, 0x16f, 0x265, 0x36c, 0x86a, 0x963, 0xa69, 0xb60,
        0x5f0, 0x4f9, 0x7f3, 0x6fa, 0xdfc, 0xcf5, 0xfff, 0xef6,
        0x1f6, 0xff, 0x3f5, 0x2fc, 0x9fa, 0x8f3, 0xbf9, 0xaf0,
        0x650, 0x759, 0x453, 0x55a, 0xe5c, 0xf55, 0xc5f, 0xd56,
        0x256, 0x35f, 0x55, 0x15c, 0xa5a, 0xb53, 0x859, 0x950,
        0x7c0, 0x6c9, 0x5c3, 0x4ca, 0xfcc, 0xec5, 0xdcf, 0xcc6,
        0x3c6, 0x2cf, 0x1c5, 0xcc, 0xbca, 0xac3, 0x9c9, 0x8c0,
        0xca0, 0xda9, 0xea3, 0xfaa, 0x4ac, 0x5a5, 0x6af, 0x7a6,
        0x8a6, 0x9af, 0xaa5, 0xbac, 0xaa, 0x1a3, 0x2a9, 0x3a0,
        0xd30, 0xc39, 0xf33, 0xe3a, 0x53c, 0x435, 0x73f, 0x636,
        0x936, 0x83f, 0xb35, 0xa3c, 0x13a, 0x33, 0x339, 0x230,
        0xe90, 0xf99, 0xc93, 0xd9a, 0x69c, 0x795, 0x49f, 0x596,
        0xa96, 0xb9f, 0x895, 0x99c, 0x29a, 0x393, 0x99, 0x190,
        0xf00, 0xe09, 0xd03, 0xc0a, 0x70c, 0x605, 0x50f, 0x406,
        0xb06, 0xa0f, 0x905, 0x80c, 0x30a, 0x203, 0x109, 0x0,
    };

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
    public static uint3 IndexToPos(int index)
    {
        return Morton.DecodeMorton32((uint)index);
    }

    // Convert a 3D position into an index
    [return: AssumeRange(0u, 262144)]
    public static int PosToIndex(uint3 position)
    {
        return (int)Morton.EncodeMorton32(position);
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
        return 0.0F;
        /*
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
        */
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
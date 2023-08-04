using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Common voxel utility methods
public static class VoxelUtils
{
    // Scaling value applied to the vertices
    public const float VERTEX_SCALING = (float)64 / (float)61;

    // Voxel scaling size
    public const float VOXEL_SIZE = 0.5F;

    // Create a 3D texture with the specified size and format
    public static RenderTexture CreateTexture(int size, GraphicsFormat format)
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

    // Convert an index to a 3D position
    public static uint3 IndexToPos(int index)
    {
        uint index2 = (uint)index;
        
        // N(ABC) -> N(A) x N(BC)
        uint y = index2 / (64 * 64);   // x in N(A)
        uint w = index2 % (64 * 64);  // w in N(BC)

        // N(BC) -> N(B) x N(C)
        uint z = w / 64;        // y in N(B)
        uint x = w % 64;        // z in N(C)
        return new uint3(x, y, z);
    }

    // Convert a 3D position into an index
    public static int PosToIndex(uint3 position)
    {
        return (int)math.round((position.y * 64 * 64 + (position.z * 64) + position.x));
    }

    // Calculate normals using first differences
    public static float3 CalculateNormals(uint3 position, ref NativeArray<float> voxels)
    {
        float baseDensity = voxels[PosToIndex(position)];
        float densityOffsetX = voxels[PosToIndex(position) + 1];
        float densityOffsetY = voxels[PosToIndex(position) + 64];
        float densityOffsetZ = voxels[PosToIndex(position) + 64 * 64];
    
        return math.normalizesafe(new float3(baseDensity - densityOffsetX, baseDensity - densityOffsetY, baseDensity - densityOffsetZ));
    }

    // Sampled the voxel grid using trilinear filtering
    public static float SampleGridInterpolated(float3 position, ref NativeArray<float> voxels) {
        float3 frac = math.frac(position);
        uint3 voxPos = (uint3)math.floor(position);

        float d000 = voxels[PosToIndex(voxPos)];
        float d100 = voxels[PosToIndex(voxPos) + 1];
        float d010 = voxels[PosToIndex(voxPos) + 64 * 64];
        float d110 = voxels[PosToIndex(voxPos) + 64 * 64 + 1];

        float d001 = voxels[PosToIndex(voxPos) + 64];
        float d101 = voxels[PosToIndex(voxPos) + 1 + 64];
        float d011 = voxels[PosToIndex(voxPos) + 64 * 64 + 64];
        float d111 = voxels[PosToIndex(voxPos) + 64 * 64 + 1 + 64];

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
    public static float CalculatePerVertexAmbientOcclusion(float3 position, ref NativeArray<float> voxels)
    {
        float ao = 0;

        for (int x = 0; x <= 1; x++) {
            for (int y = 0; y <= 1; y++) {
                for (int z = 0; z <= 1; z++) {
                    float3 offset = new float3(x, y, z) * 2 - new float3(1);
                    float3 final = (position + offset * 2 + new float3(1));
                    final = math.clamp(final, float3.zero, new float3(62));
                    float density = SampleGridInterpolated(final, ref voxels);
                    ao += density > 0 ? 1 : 0;
                }
            }
        }

        return ao / (3*3*3);
    }
}
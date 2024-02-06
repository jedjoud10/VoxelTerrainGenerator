using System;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Common terrain utility methods
public static class VoxelUtils {
    // Scaling value applied to the vertices
    public static float VertexScaling => (float)Size / ((float)Size - 3.0F);

    // Voxel scaling size
    public static int VoxelSizeReduction { get; internal set; }

    // Scaling factor when using voxel size reduction
    // Doesn't actually represent the actual size of the voxel (since we do some scaling anyways)
    public static float VoxelSizeFactor => 1F / Mathf.Pow(2F, VoxelSizeReduction);

    // Global world size in meters
    public static float GlobalWorldSize => Mathf.CeilToInt(Mathf.Pow(2F, (float)MaxDepth) * VoxelSizeFactor * Size);

    // Max depth of the octree world
    public static int MaxDepth { get; internal set; }

    // Current chunk resolution
    public static int Size { get; internal set; }

    // How many chunks fit in a single prop segment
    public static int ChunksPerPropSegment { get; internal set; }

    // Total size of a prop segment
    public static int PropSegmentSize => (int)(ChunksPerPropSegment * Size);

    // Chunk resolution for prop chunks (remember that prop chunks are 8 times as big as normal chunks)
    public static int PropSegmentResolution { get; internal set; }

    // Number of prop segments per world
    public static int PropSegmentsCount => (int)GlobalWorldSize / PropSegmentSize;

    // Should we use skirts when meshing?
    public static bool Skirts { get; internal set; }

    // Total number of voxels in a chunk
    public static int Volume => Size * Size * Size;

    // Minimum density at which we enable skirting
    public static float MinSkirtDensityThreshold { get; internal set; }

    // Should we enable smoothing when meshing?
    public static bool Smoothing { get; internal set; }

    // Max possible number of materials supported by the terrain mesh
    public const int MAX_MATERIAL_COUNT = 256;

    // Max possible number of dynamic edit types supported by the terrain
    public const int MAX_DYNAIMC_EDIT_TYPE_COUNT = 256;

    // Offsets used for octree generation
    public static readonly int3[] OctreeChildOffset = {
        new int3(0, 0, 0),
        new int3(0, 0, 1),
        new int3(1, 0, 0),
        new int3(1, 0, 1),
        new int3(0, 1, 0),
        new int3(0, 1, 1),
        new int3(1, 1, 0),
        new int3(1, 1, 1),
    };

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
    public static RenderTexture Create3DRenderTexture(int size, GraphicsFormat format) {
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

    // Create a 2D render texture with the specified size and format
    public static RenderTexture Create2DRenderTexture(int size, GraphicsFormat format) {
        RenderTexture texture = new RenderTexture(size, size, 0, format);
        texture.height = size;
        texture.width = size;
        texture.depth = 0;
        texture.volumeDepth = 1;
        texture.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        texture.enableRandomWrite = true;
        texture.Create();
        return texture;
    }

    // Custom modulo operator to discard negative numbers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint3 Mod(int3 val, int size) {
        int3 r = val % size;
        return (uint3)math.select(r, r + size, r < 0);
    }

    // Custom modulo operator to discard negative numbers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 Mod(float3 val, float size) {
        float3 r = val % size;
        return math.select(r, r + size, r < 0);
    }

    // Convert an index to a 3D position (morton coding)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint3 IndexToPos(int index) {
        return Morton.DecodeMorton32((uint)index);
    }

    // Convert a 3D position into an index (morton coding)
    [return: AssumeRange(0u, 262144)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PosToIndex(uint3 position) {
        return (int)Morton.EncodeMorton32(position);
    }

    // Convert an index to a 3D position
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint3 IndexToPos(int index, uint size) {
        uint index2 = (uint)index;

        // N(ABC) -> N(A) x N(BC)
        uint y = index2 / (size * size);   // x in N(A)
        uint w = index2 % (size * size);  // w in N(BC)

        // N(BC) -> N(B) x N(C)
        uint z = w / size;        // y in N(B)
        uint x = w % size;        // z in N(C)
        return new uint3(x, y, z);
    }

    // Convert a 3D position into an index
    [return: AssumeRange(0u, 262144)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PosToIndex(uint3 position, uint size) {
        return (int)math.round((position.y * size * size + (position.z * size) + position.x));
    }

    // Sampled the voxel grid using trilinear filtering
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static half SampleGridInterpolated(float3 position, ref NativeArray<half> densities, int size) {
        float3 frac = math.frac(position);
        uint3 voxPos = (uint3)math.floor(position);
        voxPos = math.min(voxPos, math.uint3(size - 2));

        float d000 = densities[PosToIndex(voxPos)];
        float d100 = densities[PosToIndex(voxPos + math.uint3(1, 0, 0))];
        float d010 = densities[PosToIndex(voxPos + math.uint3(0, 1, 0))];
        float d110 = densities[PosToIndex(voxPos + math.uint3(0, 0, 1))];

        float d001 = densities[PosToIndex(voxPos + math.uint3(0, 0, 1))];
        float d101 = densities[PosToIndex(voxPos + math.uint3(1, 0, 1))];
        float d011 = densities[PosToIndex(voxPos + math.uint3(0, 1, 1))];
        float d111 = densities[PosToIndex(voxPos + math.uint3(1, 1, 1))];

        float mixed0 = math.lerp(d000, d100, frac.x);
        float mixed1 = math.lerp(d010, d110, frac.x);
        float mixed2 = math.lerp(d001, d101, frac.x);
        float mixed3 = math.lerp(d011, d111, frac.x);

        float mixed4 = math.lerp(mixed0, mixed2, frac.z);
        float mixed5 = math.lerp(mixed1, mixed3, frac.z);

        float mixed6 = math.lerp(mixed4, mixed5, frac.y);

        return (half)mixed6;
    }

    // Check if we can use delta compression using the current and last densities
    // Basically checks if the 9 bit MSBs are equal
    public static bool CouldDelta(ushort last, ushort current) {
        int test1 = last & ~0x7F;
        int test2 = current & ~0x7F;
        return test1 == test2;
    }

    // Encode delta values for the density (7 LSbs)
    public static byte EncodeDelta(ushort density) {
        return (byte)(0b1 << 7 | density & 0x7F);
    }

    // Decode delta values for the density (7 LSbs)
    public static ushort DecodeDelta(byte encoded) {
        return (ushort)(encoded & 0x7F);
    }

    // Normalize a density value to it's compressed, lossy form
    public static half NormalizeHalf(half val) {
        return val;
    }

    // Convert a half to a ushort
    public static ushort AsUshort(half val) {
        return val.value;
    }

    // Convert a ushort to a half
    public static half AsHalf(ushort val) {
        return new half {
            value = (ushort)(val)
        };
    }

    // Convert a ushort to two bytes
    public static (byte, byte) UshortToBytes(ushort val) {
        return ((byte)(val >> 8), (byte)(val & 0xFF));
    }

    // Convert a uint to four bytes
    public static (byte, byte, byte, byte) UintToBytes(uint val) {
        return ((byte)(val >> 16), (byte)(val >> 16), (byte)(val >> 8), (byte)(val & 0xFF));
    }

    // Convert two bytes to a ushort
    public static ushort BytesToUshort(byte first, byte second) {
        return (ushort)(first << 8 | second);
    }

    // Uncompress the rotation of a blittable prop type
    public static Vector3 UncompressPropRotation(ref BlittableProp prop) {
        return UncompressPropRotationFromRaw(prop.rot_x, prop.rot_y, prop.rot_z);
    }

    // Convert all compressed prop rotations to euler angles
    public static Vector3 UncompressPropRotationFromRaw(byte xRot, byte yRot, byte zRot) {
        static float Single(byte rot) {
            const float RATIO = 360.0f / 255.0f;
            return ((float)rot * RATIO);
        }

        return new Vector3(Single(xRot), Single(yRot), Single(zRot));
    }

    // Convert local prop type dispatch index to global per segment bitmask index
    public static int FetchPropBitmaskIndex(int propType, ushort dispatchIndex) {
        return dispatchIndex + (PropSegmentResolution * PropSegmentResolution * PropSegmentResolution * propType);
    }
}
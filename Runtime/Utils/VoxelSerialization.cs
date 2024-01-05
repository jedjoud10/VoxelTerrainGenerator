using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// General static class we will use for serializing the edits and world seed
// This will first use RLE encoding for the chunk data in segments and then another compression algo on top of that
public static class VoxelSerialization {
    // Serialize all edits, world region files, and seed and save it
    public static void Serialize(VoxelTerrain terrain) {
        if (!terrain.Free)
            return;
        Debug.LogWarning("Serializing voxel terrain");
        Debug.LogWarning($"Serializing {terrain.VoxelEdits.dynamicEdits.Count} dynamic edits");

        foreach (var data in terrain.VoxelEdits.sparseVoxelData) {
            if (!data.densities.IsCreated)
                continue;

            UnsafeList<half> uncompressedDensities = data.densities;
            UnsafeList<ushort> uncompressedMaterials = data.materials;

            NativeList<uint> compressedMaterials = new NativeList<uint>(Allocator.TempJob);
            NativeList<byte> compressedDensities = new NativeList<byte>(Allocator.TempJob);

            CompressionJob encode = new CompressionJob {
                materialsOut = compressedMaterials,
                densitiesOut = compressedDensities,
                materialsIn = uncompressedMaterials,
                densitiesIn = uncompressedDensities,
            };

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            encode.Schedule().Complete();
            sw.Stop();
            Debug.Log($"Compressed. Mat byte len: {compressedMaterials.Length}. Densities byte len: {compressedDensities.Length}. Took {sw.ElapsedMilliseconds}ms");

            DecompressionJob decode = new DecompressionJob {
                densitiesIn = compressedDensities,
                materialsIn = compressedMaterials,
                densitiesOut = uncompressedDensities,
                materialsOut = uncompressedMaterials,
            };

            sw.Reset();
            sw.Start();
            decode.Schedule().Complete();
            sw.Stop();
            Debug.Log($"RLE decompressed. Took {sw.ElapsedMilliseconds}ms");
            compressedMaterials.Dispose();
            compressedDensities.Dispose();
        }

        terrain.RequestAll(false);
    }

    // Deserialize the edits and seed and set them in the voxel terrain
    public static void Deserialize(VoxelTerrain terrain) {
    }
}

using System;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEngine;

// General static class we will use for serializing the edits and world seed
public static class VoxelSerialization {
    // Serialize all edits, world region files, and seed and save it
    public static void WriteValueSafe(this FastBufferWriter writer, VoxelTerrain terrain) {
        if (!terrain.Free) {
            Debug.LogWarning("Could not serialize terrain! (busy)");
            return;
        }

        Debug.LogWarning("Serializing terrain using FastBufferWriter...");
        writer.WriteValueSafe(terrain.VoxelGenerator.seed);
        terrain.VoxelEdits.worldEditRegistry.Serialize(writer);
        Debug.LogWarning($"Size after world edits: {writer.Position}");
        writer.WriteValueSafe(terrain.VoxelEdits.nodes.Length);
        writer.WriteValueSafe(terrain.VoxelEdits.nodes.AsArray());
        NativeHashMap<VoxelEditOctreeNode.RawNode, int> chunkLookup = terrain.VoxelEdits.chunkLookup;
        writer.WriteValueSafe(chunkLookup.GetKeyArray(Allocator.Temp));
        writer.WriteValueSafe(chunkLookup.GetValueArray(Allocator.Temp));
        NativeHashMap<int, int> lookup = terrain.VoxelEdits.lookup;
        writer.WriteValueSafe(lookup.GetKeyArray(Allocator.Temp));
        writer.WriteValueSafe(lookup.GetValueArray(Allocator.Temp));
        Debug.LogWarning($"Size after voxel edit meta-data: {writer.Position}");


        NativeList<uint> compressedMaterials = new NativeList<uint>(Allocator.TempJob);
        NativeList<byte> compressedDensities = new NativeList<byte>(Allocator.TempJob);

        int[] arr = new int[32];

        writer.WriteValueSafe(terrain.VoxelEdits.sparseVoxelData.Count);
        foreach (var data in terrain.VoxelEdits.sparseVoxelData) {
            CompressionJob encode = new CompressionJob {
                materialsOut = compressedMaterials,
                densitiesOut = compressedDensities,
                materialsIn = data.materials,
                densitiesIn = data.densities,
            };

            encode.Schedule().Complete();

            int compressedBytes = compressedDensities.Length + compressedMaterials.Length * 4;
            arr[(int)Math.Log(data.scalingFactor, 2.0f)] = compressedBytes;

            writer.WriteValueSafe(data.position);
            writer.WriteValueSafe(data.scalingFactor);
            writer.WriteValueSafe(compressedDensities.AsArray());
            writer.WriteValueSafe(compressedMaterials.AsArray());
            compressedDensities.Clear();
            compressedMaterials.Clear();
        }

        compressedMaterials.Dispose();
        compressedDensities.Dispose();

        for (int i = 0; i < arr.Length; i++) {
            if (arr[i] > 0) {
                Debug.LogWarning($"LOD {i}, compressed size: {arr[i]} bytes");
            }
        }

        Debug.LogWarning($"Finished serializing the terrain! Final size: {writer.Position} bytes");
    }

    // Deserialize the edits and seed and set them in the voxel terrain
    public static void ReadValueSafe(this FastBufferReader reader, VoxelTerrain terrain) {
        if (!terrain.Free) {
            Debug.LogWarning("Could not deserialize terrain! (busy)");
            return;
        }

        Debug.LogWarning("Deserializing terrain using FastBufferReader...");
        reader.ReadValueSafe(out terrain.VoxelGenerator.seed);
        terrain.VoxelEdits.worldEditRegistry.Deserialize(reader);
        terrain.VoxelGenerator.SeedToPerms();

        reader.ReadValueSafe(out int nodesCount);
        terrain.VoxelEdits.nodes.Resize(nodesCount, NativeArrayOptions.ClearMemory);

        reader.ReadValueSafeTemp(out NativeArray<VoxelEditOctreeNode> nodes);
        terrain.VoxelEdits.nodes.Clear();
        terrain.VoxelEdits.nodes.AddRange(nodes);

        NativeHashMap<VoxelEditOctreeNode.RawNode, int> chunkLookup = terrain.VoxelEdits.chunkLookup;
        chunkLookup.Clear();
        reader.ReadValueSafeTemp(out NativeArray<VoxelEditOctreeNode.RawNode> keys);
        reader.ReadValueSafeTemp(out NativeArray<int> values);

        for (int i = 0; i < keys.Length; i++) {
            chunkLookup.Add(keys[i], values[i]);
        }

        NativeHashMap<int, int> lookup = terrain.VoxelEdits.lookup;
        lookup.Clear();
        reader.ReadValueSafeTemp(out NativeArray<int> keys2);
        reader.ReadValueSafeTemp(out NativeArray<int> values2);

        for (int i = 0; i < keys.Length; i++) {
            lookup.Add(keys2[i], values2[i]);
        }

        reader.ReadValueSafe(out int sparseVoxelDataCount);

        var sparse = terrain.VoxelEdits.sparseVoxelData;
        int missing = sparseVoxelDataCount - terrain.VoxelEdits.sparseVoxelData.Count;
        // add if missing
        for (int i = 0; i < Mathf.Max(missing, 0); i++) {
            sparse.Add(new SparseVoxelDeltaData {
                densities = new NativeArray<half>(VoxelUtils.Volume, Allocator.Persistent),
                materials = new NativeArray<ushort>(VoxelUtils.Volume, Allocator.Persistent),
            });
        }

        // remove if extra (gonna get overwritten anyways)
        for (int i = 0; i < -Mathf.Min(missing, 0); i++) {
            sparse[0].densities.Dispose();
            sparse[0].materials.Dispose();
            sparse.RemoveAtSwapBack(0);
        }

        int count = sparse.Count;

        NativeList<byte> compressedDensities = new NativeList<byte>(Allocator.TempJob);
        NativeList<uint> compressedMaterials = new NativeList<uint>(Allocator.TempJob);

        for (int i = 0; i < count; i++) {
            SparseVoxelDeltaData data = sparse[i];

            reader.ReadValueSafe(out float x);
            reader.ReadValueSafe(out float y);
            reader.ReadValueSafe(out float z);
            data.position = new float3(x, y, z);

            reader.ReadValueSafe(out data.scalingFactor);

            compressedDensities.Clear();
            compressedMaterials.Clear();
            reader.ReadValueSafeTemp(out NativeArray<byte> compDensArr);
            reader.ReadValueSafeTemp(out NativeArray<uint> compMatArr);
            compressedDensities.AddRange(compDensArr);
            compressedMaterials.AddRange(compMatArr);

            DecompressionJob decode = new DecompressionJob {
                densitiesIn = compressedDensities,
                materialsIn = compressedMaterials,
                densitiesOut = data.densities,
                materialsOut = data.materials,
            };

            decode.Schedule().Complete();
            sparse[i] = data;
        }

        compressedMaterials.Dispose();
        compressedDensities.Dispose();

        terrain.RequestAll(true);
    }
}

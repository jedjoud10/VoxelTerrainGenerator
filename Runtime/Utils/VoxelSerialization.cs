using System;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Netcode;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEngine;

// General static class we will use for serializing the edits and world seed
public static class VoxelSerialization {
    // Serialize all edits, world region files, and seed and save it
    public static void Serialize(ref FastBufferWriter writer, VoxelTerrain terrain) {
        if (!terrain.Free) {
            Debug.LogWarning("Could not serialize terrain! (busy)");
            return;
        }

        Debug.LogWarning("Serializing terrain using FastBufferWriter...");
        writer.WriteValueSafe(terrain.VoxelGenerator.seed);
        terrain.VoxelEdits.worldEditRegistry.Serialize(writer);
        writer.WriteValueSafe(terrain.VoxelEdits.nodes.AsArray());

        NativeHashMap<VoxelEditOctreeNode.RawNode, int> chunkLookup = terrain.VoxelEdits.chunkLookup;
        writer.WriteValueSafe(chunkLookup.GetKeyArray(Allocator.Temp));
        writer.WriteValueSafe(chunkLookup.GetValueArray(Allocator.Temp));

        NativeHashMap<int, int> lookup = terrain.VoxelEdits.lookup;
        writer.WriteValueSafe(lookup.GetKeyArray(Allocator.Temp));
        writer.WriteValueSafe(lookup.GetValueArray(Allocator.Temp));
        Debug.LogWarning($"{writer.Length} bytes");

        NativeList<uint> compressedMaterials = new NativeList<uint>(Allocator.TempJob);
        NativeList<byte> compressedDensities = new NativeList<byte>(Allocator.TempJob);

        int[] arr = new int[32];

        foreach (var data in terrain.VoxelEdits.sparseVoxelData) {
            CompressionJob encode = new CompressionJob {
                materialsOut = compressedMaterials,
                densitiesOut = compressedDensities,
                materialsIn = data.materials,
                densitiesIn = data.densities,
            };

            encode.Schedule().Complete();

            if (!writer.TryBeginWrite(8)) {
                throw new OverflowException("Not enough space in the buffer");
            }

            int compressedBytes = compressedDensities.Length + compressedMaterials.Length * 4;
            arr[(int)Math.Log(data.scalingFactor, 2.0f)] = compressedBytes;

            writer.WriteValue(compressedDensities.Length);
            writer.WriteValue(compressedMaterials.Length);
            writer.WriteValueSafe(compressedDensities.AsArray());
            writer.WriteValueSafe(compressedMaterials.AsArray());
            compressedDensities.Clear();
            compressedMaterials.Clear();
        }

        compressedMaterials.Dispose();
        compressedDensities.Dispose();

        for (int i = 0; i < arr.Length; i++) {
            Debug.LogWarning($"LOD {i}, compressed size: {arr[i]} bytes");
        }

        Debug.LogWarning($"Finished serializing the terrain! Final size: {writer.Length} bytes");
    }

    // Deserialize the edits and seed and set them in the voxel terrain
    public static void Deserialize(ref FastBufferReader reader, VoxelTerrain terrain) {
        if (!terrain.Free) {
            Debug.LogWarning("Could not deserialize terrain! (busy)");
            return;
        }

        Debug.LogWarning("Deserializing terrain using FastBufferReader...");
        reader.ReadValueSafe(out terrain.VoxelGenerator.seed);
        terrain.VoxelEdits.worldEditRegistry.Deserialize(reader);
        terrain.VoxelGenerator.SeedToPerms();

        /*
        terrain.VoxelEdits.lookup.Dispose();
        reader.ReadValueSafe(out terrain.VoxelEdits.lookup, Allocator.Persistent);

        NativeList<uint> compressedMaterials = new NativeList<uint>(Allocator.TempJob);
        NativeList<byte> compressedDensities = new NativeList<byte>(Allocator.TempJob);

        foreach (var segment in terrain.VoxelEdits.lookup) {
            if (segment.startingIndex == -1)
                continue;

            var index = segment.startingIndex;

            for (int i = 0; i < VoxelUtils.ChunksPerSegmentVolume; i++) {
                if (segment.bitset.IsSet(i)) {
                    if (terrain.VoxelEdits.)


                    DecompressionJob decode = new DecompressionJob {
                        densitiesIn = compressedDensities,
                        materialsIn = compressedMaterials,
                        densitiesOut = uncompressedDensities,
                        materialsOut = uncompressedMaterials,
                    };

                }
            }
        }

        foreach (var data in terrain.VoxelEdits.sparseVoxelData) {
            if (!data.)
                continue;

            DecompressionJob decode = new DecompressionJob {
                densitiesIn = compressedDensities,
                materialsIn = compressedMaterials,
                densitiesOut = uncompressedDensities,
                materialsOut = uncompressedMaterials,
            };

            decode.Schedule().Complete();

            encode.Schedule().Complete();

            compressedBytes += compressedDensities.Length + compressedMaterials.Length * 4;
            uncompressedBytes += VoxelUtils.Volume * 4 * 2;

            if (!writer.TryBeginWrite(8)) {
                throw new OverflowException("Not enough space in the buffer");
            }

            writer.WriteValue(compressedDensities.Length);
            writer.WriteValue(compressedMaterials.Length);
            writer.WriteValueSafe(compressedDensities.AsArray());
            writer.WriteValueSafe(compressedMaterials.AsArray());
            compressedDensities.Clear();
            compressedMaterials.Clear();
        }
        */

        //reader.ReadNetworkSerializable<IDynamicEdit>(out IDynamicEdit value);

        /*
        List<IDynamicEdit> dynamicEdits = new List<IDynamicEdit>();
        reader.ReadValueSafe(out int length);
        for (int i = 0; i < length; i++) {
        }
        writer.WriteNetworkSerializable(dynamicEdit);
        */

        /*
        

         */
        terrain.RequestAll(true);
    }
}

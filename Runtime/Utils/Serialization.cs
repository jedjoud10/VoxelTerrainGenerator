using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// General static class we will use for serializing the edits and world seed
// This will first use RLE encoding for the chunk data in segments and then another compression algo on top of that
public static class Serialization {
    // Serialize all edits, world region files, and seed and save it
    public static void Serialize(VoxelTerrain terrain) {

    }

    // Deserialize the edits and seed and set them in the voxel terrain
    public static void Deserialize(VoxelTerrain terrain) {
    }
}

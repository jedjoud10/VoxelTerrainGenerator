// Chunk offset + scale
float3 chunkOffset;
float chunkScale;

// World parameters
float3 worldOffset;
float3 worldScale;
float isosurfaceOffset;

// Seeding parameters
int3 permuationSeed;
int3 moduloSeed;

// Voxel resolution
int size;
float vertexScaling;
float voxelSize;

// Used for async readback
RWTexture3D<uint> voxels;

float propSegmentWorldSize;
float propSegmentResolution;
float3 propChunkOffset;
RWTexture3D<float> cachedPropDensities;
RWTexture2D<uint> minAxiiY;
RWTexture2D<float2> minAxiiYTest;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/SDF.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Morton.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropUtils.cginc"


// Chunk offset + scale
float3 chunkOffset;
float chunkScale;

// World parameters
float3 worldOffset;
float3 worldScale;
float densityOffset;

// Seeding parameters
int3 permuationSeed;
int3 moduloSeed;

// Voxel resolution
int size;
float vertexScaling;
float voxelSize;

// Used for async readback
RWTexture3D<uint> voxels;

// PREVIEW MODE
RWTexture3D<float> previewVoxels;
float previewDensityFactor;
float previewDensityOffset;

float propSegmentWorldSize;
float propSegmentResolution;
float3 propChunkOffset;
RWTexture3D<float> cachedPropDensities;

RWTexture2DArray<float4> positionIntersections;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/SDF.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Morton.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropUtils.cginc"


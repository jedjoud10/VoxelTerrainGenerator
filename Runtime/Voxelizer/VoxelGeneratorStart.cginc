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

struct Voxel {
	float density;
	uint material;
};

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/sdf.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/morton.cginc"
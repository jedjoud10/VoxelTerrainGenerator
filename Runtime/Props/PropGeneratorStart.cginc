float propSegmentWorldSize;
float propSegmentResolution;
float3 propChunkOffset;
float3 worldOffset;
float3 worldScale;

// Seeding parameters
int3 permuationSeed;
int3 moduloSeed;

struct BlittableProp {
    float4 position_and_scale;
    float4 euler_angles_padding;
};

// Used for async readback AND GPU indirect rendering
AppendStructuredBuffer<BlittableProp> props;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/sdf.cginc"
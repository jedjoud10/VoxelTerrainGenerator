float propSegmentWorldSize;
float propSegmentResolution;
float3 propChunkOffset;
float3 worldOffset;
float3 worldScale;

// Seeding parameters
int3 permuationSeed;
int3 moduloSeed;

struct BlittableProp {
    uint2 packed_position_and_scale;
    uint2 packed_euler_angles_padding;
};

// Used for async readback AND GPU indirect rendering
AppendStructuredBuffer<BlittableProp> props;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/SDF.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropUtils.cginc"

// Spawn a prop in the specified WORLD position (will scale down based on world scale/offset)
void Spawn(float3 position, float scale, float3 rotation, uint3 id) {
	position = position - worldOffset;
	BlittableProp prop;
	position /= worldScale;
	prop.packed_position_and_scale = PackPositionAndScale(position, scale);
	prop.packed_euler_angles_padding = PackRotationAndId(rotation, id);
	props.Append(prop);
}
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

// Used for async readback and GPU indirect rendering
// Contains multiple sections for each prop type
// we can write to specific indices within those sections with the offset buffer and counter buffer
RWStructuredBuffer<BlittableProp> tempProps;
RWStructuredBuffer<int> tempCounters;
StructuredBuffer<int3> propSectionOffsets;

// Voxels texture that we pregenerated
Texture3D<float> _Voxels;
SamplerState sampler_Voxels;

Texture2DArray<float4> _PositionIntersections;
SamplerState sampler_PositionIntersections;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/SDF.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropUtils.cginc"

// Spawn a prop in the specified WORLD position (will scale down based on world scale/offset)
void Spawn(float3 position, float scale, float3 rotation, uint propType, uint propVariant, uint3 id) {
	position = position - worldOffset;
	BlittableProp prop;
	position /= worldScale;
	prop.packed_position_and_scale = PackPositionAndScale(position, scale);
	prop.packed_euler_angles_padding = PackRotationAndId(rotation, id);
	
	int index = 0;
	InterlockedAdd(tempCounters[propType], 1, index);
	int finalIndex = index + propSectionOffsets[propType].x;
	tempProps[finalIndex] = prop;
}

// Get the density value at a specific point (world space)
float GetDensity(float3 position) {
	float3 localPos = WorldToPropSegment(position);
	return _Voxels.SampleLevel(sampler_Voxels, localPos, 0).x;
}

// Check if a ray hits the surface in a specific axis, and returns the hit position
void CheckRay(float3 baseWorldPosition, out float position) {
	position = 100000;


	//float4 baseTest = _PositionIntersections.SampleLevel(sampler_PositionIntersections, float3(test3.xy, 0), 0);
	/*

	*/
	
	/*
	float2 localPos = WorldToPropSegment(baseWorldPosition).xy;

	*/

	float2 localPos = WorldToPropSegment(baseWorldPosition).xy + (0.5f / 32.0f);

	// No ray if we're outside the bounds of the segment
	bool test1 = any(localPos < float2(0, 0));
	bool test2 = any(localPos > float2(1, 1));
	if (test1 || test2) {
		return;
	}

	// Check normal
	float4 normals = _PositionIntersections.Gather(sampler_PositionIntersections, float3(localPos, 0), 0);

	float zNormal = -(normals.x - normals.z);
	float xNormal = -(normals.x - normals.y);
	if (abs(zNormal) + abs(xNormal) > 10.0) {
		return;
	}

	// Check the ray dist by sampling the texture
	float4 baseTest = _PositionIntersections.SampleLevel(sampler_PositionIntersections, float3(localPos, 0), 0);
	if (baseTest.x < 10000) {
		position = baseTest.x;
	}
}
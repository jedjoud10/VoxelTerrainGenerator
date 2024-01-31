#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropPackUtils.cginc"

float3 PropSegmentToWorld(uint3 id) {
	// Calculate the main world position
	float3 position = float3(id.xzy);

	position *= (propSegmentResolution + 1) / propSegmentResolution;
	position *= propSegmentWorldSize / propSegmentResolution;
	position += propChunkOffset;

	// World offset and scale
	position = (position * worldScale) + worldOffset;
	return position;
}

float3 WorldToPropSegment(float3 world) {
	// World offset and scale
	float3 gridPos = world - worldOffset;
	gridPos /= worldScale;

	// Inverse of PropSegmentToWorld
	gridPos -= propChunkOffset;
	gridPos /= propSegmentWorldSize / propSegmentResolution;
	gridPos /= (propSegmentResolution + 1) / propSegmentResolution;
	return float3(gridPos.xzy / propSegmentResolution);
}

float invLerp(float from, float to, float value) {
	return (value - from) / (to - from);
}
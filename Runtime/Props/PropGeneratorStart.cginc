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
    uint2 packed_rotation_dispatch_index_prop_variant_padding;
};

// Used for async readback and GPU indirect rendering
// Contains multiple sections for each prop type
// we can write to specific indices within those sections with the offset buffer and counter buffer
int propCount;
RWStructuredBuffer<BlittableProp> tempProps;
RWStructuredBuffer<int> tempCounters;
StructuredBuffer<uint3> propSectionOffsets;

// Voxels texture that we pregenerated
Texture3D<float> _Voxels;
SamplerState sampler_Voxels;

Texture2DArray<float4> _PositionIntersections;
SamplerState sampler_PositionIntersections;

StructuredBuffer<uint> affectedPropsBitMask;

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/Noises.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/SDF.cginc"
#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropUtils.cginc"

// Spawn a prop in the specified WORLD position (will scale down based on world scale/offset)
void Spawn(float3 position, float scale, float3 rotation, uint propType, uint propVariant, uint3 id) {
	if (propType >= (uint)propCount) {
		return;
	}

	uint searchIndexBase = id.x + id.y * propSegmentResolution + id.z * propSegmentResolution * propSegmentResolution;
	uint searchIndexOffset = propSegmentResolution * propSegmentResolution * propSegmentResolution * propType;
	uint searchIndex = searchIndexBase + searchIndexOffset;
	uint block = searchIndex / 32;
	uint local = searchIndex % 32;

	if (((affectedPropsBitMask[block] >> local) & 1) == 1) {
		return;
	}

	position = position - worldOffset;
	BlittableProp prop;
	position /= worldScale;
	prop.packed_position_and_scale = PackPositionAndScale(position, scale);
	prop.packed_rotation_dispatch_index_prop_variant_padding = PackRotationAndVariantAndId(rotation, propVariant, searchIndexBase);
	
	uint index = 0;
	InterlockedAdd(tempCounters[propType], 1, index);

	if (index >= propSectionOffsets[propType + 1].x && propType < ((uint)propCount-1)) {
		InterlockedAdd(tempCounters[propType], -1, index);
		return;
	}

	uint finalIndex = index + propSectionOffsets[propType].x;
	tempProps[finalIndex] = prop;
}

// Get the density value at a specific point (world space)
float GetDensity(float3 position) {
	float3 localPos = WorldToPropSegment(position);
	return _Voxels.SampleLevel(sampler_Voxels, localPos + (0.5 / propSegmentResolution), 0).x;
}

// Calculates the normals at a specific position using numerical derivation
float3 GetNormal(float3 position) {
	float b = GetDensity(position);
	float x1 = GetDensity(position + float3(20, 0, 0));
	float y1 = GetDensity(position + float3(0, 20, 0));
	float z1 = GetDensity(position + float3(0, 0, 20));
	
	return normalize(float3(x1-b, y1-b, z1-b));
}

// Structure returned from CheckClosestSurface
struct ClosestSurface {
	bool hit;
	float3 normals;
	float3 position;
};

// Check if the current position contains an surface that contains both air and terrain
// Could be used for spawning props horizontally, or in cases where there are a lot
// of intersection within a single axis
ClosestSurface CheckClosestSurface(float3 position, int dir, float thickness) {
	ClosestSurface res;
	res.hit = false;
	res.position = float3(0, 0, 0);
	res.normals = float3(0, 0, 0);

	// Convert world position to local position (in ID space)
	float3 localPosTest = WorldToPropSegment(position) + (0.5f / propSegmentResolution);
	float2 localPos = float2(0, 0);
	float3 offset = float3(0, 0, 0);

	if (dir == 0) {
		localPos = localPosTest.xy;
		offset.y = thickness;
	} else if (dir == 1) {
		localPos = localPosTest.xz;
		offset.z = thickness;
	} else {
		localPos = localPosTest.yz;
		offset.x = thickness;
	}

	// No surface if we're outside the bounds of the segment
	bool test1 = any(localPos <= float2(0, 0) + (0.5f / propSegmentResolution));
	bool test2 = any(localPos >= float2(1, 1) - (0.5f / propSegmentResolution));
	if (test1 || test2) {
		return res;
	}

	// Sample the density twice and check for intersection
	float d0 = GetDensity(position - offset);
	float d1 = GetDensity(position + offset);

	// Make use of inv lerp to find the exact position
	if (d0 > 0 ^ d1 > 0) {
		float inv = invLerp(d0, d1, 0);
		float3 finalPosition = lerp(position - offset, position + offset, inv);
		res.hit = true;
		res.position = finalPosition;
		res.normals = GetNormal(finalPosition);
		return res;
	}

	return res;
}

/*
// Structure returned from a hit ray
struct HitRay {
	bool hit;
	float3 position;
	float3 normals;
};

// Check if a ray hits the surface in a specific axis, and returns the hit position
HitRay CheckRay(float3 position, int dir) {
	HitRay ray;
	ray.hit = false;
	ray.position = float3(0, 0, 0);
	ray.normals = float3(0, 0, 0);

	// Convert world position to local position (in ID space)
	float3 localPosTest = WorldToPropSegment(position) + (0.5f / propSegmentResolution);
	float2 localPos = float2(0, 0);

	if (dir == 0) {
		localPos = localPosTest.xy;
	} else if (dir == 1) {
		localPos = localPosTest.xz;
	} else {
		localPos = localPosTest.yz;
	}

	// No ray if we're outside the bounds of the segment
	bool test1 = any(localPos <= float2(0, 0) + (0.5f / propSegmentResolution));
	bool test2 = any(localPos >= float2(1, 1) - (0.5f / propSegmentResolution));
	if (test1 || test2) {
		return ray;
	}

	// Check normal
	float4 normals = _PositionIntersections.Gather(sampler_PositionIntersections, float3(localPos, dir), 0);
	float zNormal = -(normals.x - normals.z);
	float xNormal = -(normals.x - normals.y);
	ray.normals = normalize(float3(xNormal, 1, zNormal));

	// Check the ray dist by sampling the texture
	float4 baseTest = _PositionIntersections.SampleLevel(sampler_PositionIntersections, float3(localPos, dir), 0);
	float value = baseTest.x;
	
	if (value < 1000) {
		if (dir == 0) {
			ray.position = float3(position.x, value, position.z);
		} else if (dir == 1) {
			ray.position = float3(position.x, position.y, value);
		} else {
			ray.position = float3(value, position.y, position.z);
		}

		ray.hit = true;
	}

	return ray;
}
*/
uint2 PackPositionAndScale(float3 position, float scale) {
	uint x = f32tof16(position.x);
	uint y = f32tof16(position.y);
	uint z = f32tof16(position.z);
	uint w = f32tof16(scale);

	uint first = x | (y << 16);
	uint second = z | (w << 16);
	return uint2(first, second);
}

uint2 PackRotationAndId(float3 rotation, uint3 id) {
	return uint2(0, 0);
}

float4 UnpackPositionAndScale(uint2 packed) {
	float x = f16tof32(packed.x & 0xFFFF);
	float y = f16tof32(packed.x >> 16);
	float z = f16tof32(packed.y & 0xFFFF);
	float w = f16tof32(packed.y >> 16);
	return float4(x, y, z, w);
}

float3 UnpackRotation(uint2 packed) {
	return float3(0, 0, 0);
}
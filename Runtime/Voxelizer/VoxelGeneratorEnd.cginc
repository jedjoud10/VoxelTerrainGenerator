[numthreads(4, 4, 4)]
void CSVoxelizer(uint3 id : SV_DispatchThreadID)
{
	// Calculate the main world position
	float3 position = float3(id.xzy);
	position -= 1.0;
	position *= voxelSize;

	// Chunk offsets + vertex scaling
	position *= vertexScaling;
	position *= chunkScale;
	position += (chunkOffset - ((chunkScale * size) / (size - 3.0)) * 0.5);
	
	// World offset and scale
	position = ((position * worldScale) / voxelSize) + worldOffset;
	float density = 0.0;
	uint material = 0;
	
	VoxelAt(position, density, material);

	// Morton encode the texture data
	uint packedDensity = f32tof16(density);
	uint packedData = packedDensity | (material << 16);
	uint mortonIndex = encodeMorton32(id.xzy);
	uint3 mortonPos = indexToPos(mortonIndex);
	voxels[mortonPos.xzy] = packedData;
}

// Generates the prop cached voxel data
[numthreads(4, 4, 4)]
void CSPropVoxelizer(uint3 id : SV_DispatchThreadID)
{
	float3 position = PropSegmentToWorld(id);
	float density = 0.0;
	uint material = 0;
	VoxelAt(position, density, material);
	cachedPropDensities[id.xyz] = density;

	if (density < 0.0) {
		InterlockedMax(minAxiiY[id.xy], id.z+1);
	}
}

// Raycasts to get the position of the surface in a specific axis
[numthreads(4, 4, 1)]
void CSPropRaycaster(uint2 id : SV_DispatchThreadID)
{
	uint pos = minAxiiY[id.xy]-1;

	if (pos > ((uint)propSegmentResolution)) {
		minAxiiYTest[id.xy] = float2(asfloat(0xffffffff), -10000);
		return;
	}

	float d1 = cachedPropDensities[uint3(id.x, id.y, pos)];
	float d2 = cachedPropDensities[uint3(id.x, id.y, pos + 1)];
	float p1 = PropSegmentToWorld(uint3(id.x, id.y, pos)).y;
	float p2 = PropSegmentToWorld(uint3(id.x, id.y, pos + 1)).y;

	if ((d1 < 0) && (d2 > 0)) {
		minAxiiYTest[id.xy] = float2(lerp(p1, p2, invLerp(d1, d2, 0)), -d1);
	}
	else {
		minAxiiYTest[id.xy] = float2(asfloat(0xffffffff), -10000);
	}
}
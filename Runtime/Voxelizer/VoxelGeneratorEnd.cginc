// Seems to work without this janky thing below
//position += (chunkOffset - ((chunkScale * size) / (size - 3.0)) * 0.5);

[numthreads(4, 4, 4)]
void CSVoxelizer(uint3 id : SV_DispatchThreadID)
{
	// Calculate the main world position
	float3 position = float3(id.xzy);
	position *= voxelSize;
	position -= (1.5 * voxelSize);

	// Chunk offsets + vertex scaling
	position *= vertexScaling;
	position *= chunkScale;
	position += chunkOffset;

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
	
	// TODO: Understand why we need this
	// Props are constant size so why would voxel size affect them?
	position *= (1 / voxelSize);
	float density = 0.0;
	uint material = 0;
	VoxelAt(position, density, material);
	cachedPropDensities[id.xyz] = density;
}

// Raycasts to get the position of the surface in a specific axis
// xy
// xz
// yx
[numthreads(4, 4, 4)]
void CSPropRaycaster(uint3 id : SV_DispatchThreadID)
{
	float d0 = cachedPropDensities[id];
	float3 p0 = PropSegmentToWorld(id);

	float d1 = cachedPropDensities[id + uint3(1, 0, 0)];
	float3 p1 = PropSegmentToWorld(id + uint3(1, 0, 0));

	float d2 = cachedPropDensities[id + uint3(0, 1, 0)];
	float3 p2 = PropSegmentToWorld(id + uint3(0, 1, 0));

	float d3 = cachedPropDensities[id + uint3(0, 0, 1)];
	float3 p3 = PropSegmentToWorld(id + uint3(0, 0, 1));

	if (d0 < 0 && d3 > 0) {
		float inv = invLerp(d0, d3, 0);
		float3 finalPosition = lerp(p0, p3, inv);
		positionIntersections[uint3(id.xy, 0)] = finalPosition.y;
	}
}

#pragma warning( disable: 4008 )

// Clears the textures
[numthreads(4, 4, 1)]
void CSClearRayCastData(uint3 id : SV_DispatchThreadID)
{
	float maxValue = 0.0f / 0.0f;
	positionIntersections[id] = float4(maxValue, maxValue, maxValue, maxValue);
}
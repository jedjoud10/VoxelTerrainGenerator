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
	// TODO: Optimize in its own pass
	if (id.z == 0) {
		int test;
		InterlockedExchange(broadPhaseIntersections[uint3(id.xy, 0)], 0, test);
	}

	if (id.y == 0) {
		int test;
		InterlockedExchange(broadPhaseIntersections[uint3(id.xz, 1)], 0, test);
	}

	if (id.x == 0) {
		int test;
		InterlockedExchange(broadPhaseIntersections[uint3(id.yz, 2)], 0, test);
	}

	float3 position = PropSegmentToWorld(id);
	
	// TODO: Understand why we need this
	// Props are constant size so why would voxel size affect them?
	position *= (1 / voxelSize);
	float density = 0.0;
	uint material = 0;
	VoxelAt(position, density, material);
	cachedPropDensities[id.xyz] = density;

	if (density < 0.0) {
		InterlockedMax(broadPhaseIntersections[uint3(id.xy, 0)], id.z + 1);
		InterlockedMax(broadPhaseIntersections[uint3(id.xz, 1)], id.y + 1);
		InterlockedMax(broadPhaseIntersections[uint3(id.yz, 2)], id.x + 1);
	}
}

#pragma warning( disable: 4008 )

// Raycasts to get the position of the surface in a specific axis
[numthreads(4, 4, 1)]
void CSPropRaycaster(uint3 id : SV_DispatchThreadID)
{
	float maxValue = 0.0f / 0.0f;
	positionIntersections[id] = float4(maxValue, maxValue, maxValue, maxValue);
	uint pos = broadPhaseIntersections[uint3(id.x, id.y, id.z)]-1;

	if (pos > ((uint)propSegmentResolution)) {
		return;
	}
	
	float d1, d2;
	float3 p1, p2;
	if (id.z == 0) {
		d1 = cachedPropDensities[uint3(id.x, id.y, pos)];
		d2 = cachedPropDensities[uint3(id.x, id.y, pos + 1)];
		p1 = PropSegmentToWorld(float3(id.x, id.y, pos));
		p2 = PropSegmentToWorld(float3(id.x, id.y, pos + 1));
	} else if (id.z == 1) {
		d1 = cachedPropDensities[uint3(id.x, pos, id.y)];
		d2 = cachedPropDensities[uint3(id.x, pos + 1, id.y)];
		p1 = PropSegmentToWorld(float3(id.x, pos, id.y));
		p2 = PropSegmentToWorld(float3(id.x, pos + 1, id.y));
	} else {
		d1 = cachedPropDensities[uint3(pos, id.y, id.x)];
		d2 = cachedPropDensities[uint3(pos + 1, id.y, id.x)];
		p1 = PropSegmentToWorld(float3(pos, id.y, id.x));
		p2 = PropSegmentToWorld(float3(pos + 1, id.y, id.x));
	}

	if ((d1 < 0) && (d2 > 0)) {
		float inv = invLerp(d1, d2, 0);
		float3 newTest2 = lerp(p1, p2, inv);

		float value = 0;
		if (id.z == 0) {
			value = newTest2.y;
		} else if (id.z == 1) {
			value = newTest2.z;
		} else {
			value = newTest2.x;
		}

		positionIntersections[id] = float4(value, 0, 0, 0);
		return;
	}
}
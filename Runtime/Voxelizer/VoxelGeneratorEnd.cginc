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
		InterlockedExchange(broadPhaseIntersections[uint3(id.zy, 2)], 0, test);
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

// Raycasts to get the position of the surface in a specific axis
[numthreads(4, 4, 1)]
void CSPropRaycaster(uint3 id : SV_DispatchThreadID)
{
	positionIntersections[id] = float4(100000, 100000, 100000, 100000);
	uint pos = broadPhaseIntersections[uint3(id.x, id.y, id.z)]-1;

	if (pos > ((uint)propSegmentResolution)) {
		positionIntersections[id] = float4(100000, 100000, 100000, 100000);
		return;
	}
	
	float d1 = cachedPropDensities[uint3(id.x, id.y, pos)];
	float d2 = cachedPropDensities[uint3(id.x, id.y, pos + 1)];
	float3 p1 = PropSegmentToWorld(float3(id.x, id.y, pos));
	float3 p2 = PropSegmentToWorld(float3(id.x, id.y, pos + 1));

	if ((d1 < 0) && (d2 > 0)) {
		float inv = invLerp(d1, d2, 0);
		float3 newTest2 = lerp(p1, p2, inv);
		positionIntersections[id] = float4(newTest2.y, 0, 0, 0);
		
		/*
		//float density = -d1;
		float base = p1.y;

		float3 lastPosition = base;
		float3 newPosition = base;
		float lastDensity = 1;
		float newDensity = -1;
		uint mat = 0;
		for (float i = 0; i < 1.0; i+=0.1)
		{
			newPosition = lerp(p1, p2, i);
			VoxelAt(newPosition * (1 / voxelSize), newDensity, mat);

			if (newDensity > 0 && lastDensity < 0 && lastDensity < -0.5) {
				float inv = invLerp(lastDensity, newDensity, 0);
				float3 newTest2 = lerp(lastPosition, newPosition, inv);
				//float newPos = lerp(p1, p2, i).y;
				positionIntersections[id] = float4(newTest2.y, 0, 0, 0);
				return;
			}

			lastPosition = newPosition;
			lastDensity = newDensity;
		}

		positionIntersections[id] = float4(100000, 100000, 100000, 100000);
		*/
	}
	else {
		positionIntersections[id] = float4(100000, 100000, 100000, 100000);
	}
}
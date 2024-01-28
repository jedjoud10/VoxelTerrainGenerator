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

void InterlockedMax(int2 index, float val)
{
	int uval = asint(val);
	int tmp0 = 0;
	int tmp1;

	[allow_uav_condition]
	while (true)
	{
		InterlockedCompareExchange(minAxiiY[index], tmp0, uval, tmp1);

		if (tmp1 == tmp0 || asfloat(tmp0) >= val)
		{
			break;
		}

		tmp0 = tmp1;
		uval = asint(max(val, asfloat(tmp1)));
	}
}

// Generates the prop cached voxel data
[numthreads(4, 4, 4)]
void CSPropVoxelizer(uint3 id : SV_DispatchThreadID)
{
	// Calculate the main world position
	float3 position = float3(id.xzy);
	position *= propSegmentWorldSize / propSegmentResolution;
	position += propChunkOffset;

	// World offset and scale
	position = (position * worldScale) + worldOffset;

	float density = 0.0;
	uint material = 0;
	VoxelAt(position, density, material);
	cachedPropDensities[id.xyz] = density;

	if (density < -2.0) {
		// keep track of the min / max values for this in the textures
		InterlockedMax(id.xy, id.z);
	}
	else {
		//InterlockedMin(minAxiiY[id.xy].y, position.y);
	}

	//minAxiiY[id.xy] = asuint(0.0);

	//minAxiiY[id.xy] = (density - position.y) * 1000;

	//minAxiiY[id.xy] = id.y;
}
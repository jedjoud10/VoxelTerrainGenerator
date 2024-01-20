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
	Voxel voxel = GetVoxelAt(position);

	// Morton encode the texture data
	uint packedDensity = f32tof16(voxel.density);
	uint packedData = packedDensity | (voxel.material << 16);
	uint mortonIndex = encodeMorton32(id.xzy);
	uint3 mortonPos = indexToPos(mortonIndex);
	voxels[mortonPos.xzy] = packedData;
}
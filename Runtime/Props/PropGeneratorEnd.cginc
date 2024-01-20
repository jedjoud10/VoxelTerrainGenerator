// Generates the prop data on the GPU (executed for EACH prop type available)
[numthreads(4, 4, 4)]
void CSPropenator(uint3 id : SV_DispatchThreadID)
{
	// Calculate the main world position
	float3 position = float3(id.xzy);
	position *= propSegmentWorldSize / propSegmentResolution;
	position += propChunkOffset;
	
	// World offset and scale
	position = (position * worldScale) + worldOffset;
	CheckSpawnProps(position);
}
// Generates the prop data on the GPU (executed for EACH prop type available)
[numthreads(4, 4, 4)]
void CSPropenator(uint3 id : SV_DispatchThreadID)
{
	float3 position = PropSegmentToWorld(id);
	CheckSpawnProps(id, position);
}
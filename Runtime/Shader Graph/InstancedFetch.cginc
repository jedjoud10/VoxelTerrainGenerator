#ifndef INSTANCED_FETCH_INCLUDED
#define INSTANCED_FETCH_INCLUDED

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropPackUtils.cginc"

struct BlittableProp {
    uint2 packed_position_and_scale;
    uint2 packed_euler_angles_id;
};

StructuredBuffer<BlittableProp> _BlittablePropBuffer;

StructuredBuffer<uint3> _PropSectionOffsets;
float _PropType;

void MyFunctionA_float(float i, out float variant, out float3 position, out float scale, out float3 rotation)
{
    BlittableProp prop = _BlittablePropBuffer[(int)i + _PropSectionOffsets[(int)_PropType].z];

    float4 unpackedPosScale = UnpackPositionAndScale(prop.packed_position_and_scale);
    variant = UnpackVariant(prop.packed_euler_angles_id);
    position = unpackedPosScale.xyz;
    scale = unpackedPosScale.w;
    rotation = UnpackRotation(prop.packed_euler_angles_id);
}

#endif
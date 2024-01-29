#ifndef INSTANCED_FETCH_INCLUDED
#define INSTANCED_FETCH_INCLUDED

#include "Packages/com.jedjoud.voxelterraingenerator/Runtime/Utils/PropPackUtils.cginc"

struct BlittableProp {
    uint2 packed_position_and_scale;
    uint2 packed_euler_angles_id;
};

StructuredBuffer<BlittableProp> _BlittablePropBuffer;

void MyFunctionA_float(float i, out float3 position, out float scale, out float3 rotation)
{
    BlittableProp prop = _BlittablePropBuffer[(int)i];

    float4 unpackedPosScale = UnpackPositionAndScale(prop.packed_position_and_scale);
    //float4 unpackedRotation = UnpackRotationAndId(prop.packed_euler_angles_padding);
    position = unpackedPosScale.xyz;
    scale = unpackedPosScale.w;
    rotation = float3(0, 0, 0);
}

#endif
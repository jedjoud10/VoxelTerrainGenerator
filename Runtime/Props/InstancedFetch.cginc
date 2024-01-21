#ifndef INSTANCED_FETCH_INCLUDED
#define INSTANCED_FETCH_INCLUDED

struct BlittableProp {
    float4 position_and_scale;
    float4 euler_angles_padding;
};

StructuredBuffer<BlittableProp> _BlittablePropBuffer;

void MyFunctionA_float(float i, out float3 position, out float scale, out float3 rotation)
{
    BlittableProp prop = _BlittablePropBuffer[(int)i];
    position = prop.position_and_scale.xyz;
    scale = prop.position_and_scale.w;
    rotation = prop.euler_angles_padding.xyz;
}

#endif
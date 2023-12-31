#define PI 3.14159265358979

float3 mod289(float3 x) {
    x += moduloSeed.x;
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

// Modulo 289 without a division (only multiplications)
float2 mod289(float2 x) {
    x += moduloSeed.y;
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 mod289(float4 x) {
    x += moduloSeed.z;
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 permute(float4 x) {
    return mod289(((x * 34.0) + 1.0) * x);
}

float3 permute(float3 x) {
    return mod289((34.0 * x + 1.0) * x);
}

float3 mod7(float3 x) {
    return x - floor(x * (1.0 / 7.0)) * 7.0;
}

float2 hash(in float2 x)
{
    const float2 k = float2(0.3183099, 0.3678794);
    x = x * k + k.yx;
    return -1.0 + 2.0 * frac(16.0 * k * frac(x.x * x.y * (x.x + x.y)));
}

float3 hash3( float2 p )
{
    float3 q = float3( dot(p,float2(127.1,311.7)), 
				   dot(p,float2(269.5,183.3)), 
				   dot(p,float2(419.2,371.9)) );
	return frac(sin(q)*43758.5453);
}

/*
original_author: Patricio Gonzalez Vivo
description: pass a value and get some random normalize value between 0 and 1
use: float random[2|3](<float|float2|float3> value)
examples:
    - /shaders/generative_random.frag
*/

float random(float x) {
  return frac(sin(x) * 43758.5453);
}

float random(float2 st) {
  return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453);
}

float random(float3 pos) {
  return frac(sin(dot(pos.xyz, float3(70.9898, 78.233, 32.4355))) * 43758.5453123);
}

float random(float4 pos) {
    float dot_product = dot(pos, float4(12.9898,78.233,45.164,94.673));
    return frac(sin(dot_product) * 43758.5453);
}

// Hash function from https://www.shadertoy.com/view/4djSRW
float3 RANDOM_SCALE3 = float3(0.1031, 0.1030, 0.0973);

float4 RANDOM_SCALE4 = float4(1031, 0.1030, 0.0973, 0.1099);

float2 random2(float p) {
    float3 p3 = frac(float3(p, p, p) * RANDOM_SCALE3);
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}

float2 random2(float2 p) {
    float3 p3 = frac(p.xyx * RANDOM_SCALE3);
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}

float2 random2(float3 p3) {
    p3 = frac(p3 * RANDOM_SCALE3);
    p3 += dot(p3, p3.yzx+19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}

float3 random3(float p) {
    float3 p3 = frac(float3(p, p, p) * RANDOM_SCALE3);
    p3 += dot(p3, p3.yzx+19.19);
    return frac((p3.xxy+p3.yzz)*p3.zyx); 
}

float3 random3(float2 p) {
    float3 p3 = frac(float3(p.xyx) * RANDOM_SCALE3);
    p3 += dot(p3, p3.yxz+19.19);
    return frac((p3.xxy+p3.yzz)*p3.zyx);
}

float3 random3(float3 p) {
    p = frac(p * RANDOM_SCALE3);
    p += dot(p, p.yxz+19.19);
    return frac((p.xxy + p.yzz)*p.zyx);
}

float4 random4(float p) {
    float4 p4 = frac(float4(p, p, p, p) * RANDOM_SCALE4);
    p4 += dot(p4, p4.wzxy+19.19);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);   
}

float4 random4(float2 p) {
    float4 p4 = frac(float4(p.xyxy) * RANDOM_SCALE4);
    p4 += dot(p4, p4.wzxy+19.19);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
}

float4 random4(float3 p) {
    float4 p4 = frac(float4(p.xyzx)  * RANDOM_SCALE4);
    p4 += dot(p4, p4.wzxy+19.19);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
}

float4 random4(float4 p4) {
    p4 = frac(p4  * RANDOM_SCALE4);
    p4 += dot(p4, p4.wzxy+19.19);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
}

// Common primitive SDF functions taken from https://iquilezles.org/articles/distfunctions/
// Author: Inigo Quilez

float sdSphere(float3 p, float s)
{
	return length(p) - s;
}

float sdBox(float3 p, float3 b)
{
	float3 q = abs(p) - b;
	return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float3 repeat(float3 p, float3 b) {
	return float3(fmod(p.x, b.x), fmod(p.y, b.y), fmod(p.z, b.z));
}

float sdOctahedron(float3 p, float s)
{
	p = abs(p);
	float m = p.x + p.y + p.z - s;
	float3 q;
	if (3.0 * p.x < m) q = p.xyz;
	else if (3.0 * p.y < m) q = p.yzx;
	else if (3.0 * p.z < m) q = p.zxy;
	else return m * 0.57735027;

	float k = clamp(0.5 * (q.z - q.y + s), 0.0, s);
	return length(float3(q.x, q.y - s + k, q.z - k));
}

float sdPlane(float3 p, float3 n, float h)
{
	// n must be normalized
	return dot(p, n) + h;
}

float sdPyramid(float3 p, float h)
{
	float m2 = h * h + 0.25;

	p.xz = abs(p.xz);
	p.xz = (p.z > p.x) ? p.zx : p.xz;
	p.xz -= 0.5;

	float3 q = float3(p.z, h * p.y - 0.5 * p.x, h * p.x + 0.5 * p.y);

	float s = max(-q.x, 0.0);
	float t = clamp((q.y - 0.5 * p.z) / (m2 + 0.25), 0.0, 1.0);

	float a = m2 * (q.x + s) * (q.x + s) + q.y * q.y;
	float b = m2 * (q.x + 0.5 * t) * (q.x + 0.5 * t) + (q.y - m2 * t) * (q.y - m2 * t);

	float d2 = min(q.y, -q.x * m2 - q.y * 0.5) > 0.0 ? 0.0 : min(a, b);

	return sqrt((d2 + q.z * q.z) / m2) * sign(max(q.z, -p.y));
}

float sdRoundBox(float3 p, float3 b, float r)
{
	float3 q = abs(p) - b;
	return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - r;
}

float opUnion(float d1, float d2) { return min(d1, d2); }
float opSubtraction(float d1, float d2) { return max(-d1, d2); }
float opIntersection(float d1, float d2) { return max(d1, d2); }


float opSmoothUnion(float d1, float d2, float k) {
	float h = clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
	return lerp(d2, d1, h) - k * h * (1.0 - h);
}

float opSmoothSubtraction(float d1, float d2, float k) {
	float h = clamp(0.5 - 0.5 * (d2 + d1) / k, 0.0, 1.0);
	return lerp(d2, -d1, h) + k * h * (1.0 - h);
}

float opSmoothIntersection(float d1, float d2, float k) {
	float h = clamp(0.5 - 0.5 * (d2 - d1) / k, 0.0, 1.0);
	return lerp(d2, d1, h) + k * h * (1.0 - h);
}
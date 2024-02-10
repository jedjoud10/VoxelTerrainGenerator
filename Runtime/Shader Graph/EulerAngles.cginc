#ifndef EULER_TO_MAT_INCLUDED
#define EULER_TO_MAT_INCLUDED

void MyFunctionB_float(float3 v, out float3x3 output)
{
    float sx, cx;
    float sy, cy;
    float sz, cz;

    sincos(v.x, sx, cx);
    sincos(v.y, sy, cy);
    sincos(v.z, sz, cz);

    float3 row1 = float3(sx*sy*sz + cy*cz, sx*sy*cz - cy*sz, cx*sy);
    float3 row3 = float3(sx*cy*sz - sy*cz, sx*cy*cz + sy*sz, cx*cy);
    float3 row2 = float3(cx*sz, cx*cz, -sx);

    output = float3x3(row1, row2, row3);
}

#endif
//Copyright 2020 Clay John

//Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
//and associated documentation files (the "Software"), to deal in the Software without restriction, 
//including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
//and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do 
//so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all copies or 
//substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT 
//NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
//IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
//WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
//SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

// from https://www.shadertoy.com/view/XdXBRH
//name:Noise - Gradient - 2D - Deriv
//Author: iq
//License: MIT
// return gradient noise (in x) and its derivatives (in yz)
float3 noised(in float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);

    float2 ga = hash(i + float2(0.0, 0.0));
    float2 gb = hash(i + float2(1.0, 0.0));
    float2 gc = hash(i + float2(0.0, 1.0));
    float2 gd = hash(i + float2(1.0, 1.0));

    float va = dot(ga, f - float2(0.0, 0.0));
    float vb = dot(gb, f - float2(1.0, 0.0));
    float vc = dot(gc, f - float2(0.0, 1.0));
    float vd = dot(gd, f - float2(1.0, 1.0));

    return float3(va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd),   // value
        ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) +  // derivatives
        du * (u.yx * (va - vb - vc + vd) + float2(vb, vc) - va));
}


// code adapted from https://www.shadertoy.com/view/llsGWl
// name: Gavoronoise
// author: guil
// license: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License
//Code has been modified to return analytic derivatives and to favour 
//direction quite a bit.
float3 erosion(in float2 p, float2 dir) {
    float2 ip = floor(p);
    float2 fp = frac(p);
    float f = 2. * PI;
    float3 va = 0;
    float wt = 0.0;
    for (int i = -2; i <= 1; i++) {
        for (int j = -2; j <= 1; j++) {
            float2 o = float2(i, j);
            float2 h = hash(ip - o) * 0.5;
            float2 pp = fp + o - h;
            float d = dot(pp, pp);
            float w = exp(-d * 2.0);
            wt += w;
            float mag = dot(pp, dir);
            va += float3(cos(mag * f), -sin(mag * f) * (pp + dir)) * w;
        }
    }
    return va / wt;
}


//This is where the magic happens
float3 mountain(float2 p, float s) {
    //First generate a base heightmap
    //it can be based on any type of noise
    //so long as you also generate normals
    //Im just doing basic FBM based terrain using
    //iq's analytic derivative gradient noise
    float3 n = 0;
    float nf = 1.0;
    float na = 0.6;
    for (int i = 0; i < 2; i++) {
        n += noised(p * s * nf) * na * float3(1.0, nf, nf);
        na *= 0.5;
        nf *= 2.0;
    }

    //take the curl of the normal to get the gradient facing down the slope
    float2 dir = n.zy * float2(1.0, -1.0);

    //Now we compute another fbm type noise
    // erosion is a type of noise with a strong directionality
    //we pass in the direction based on the slope of the terrain
    //erosion also returns the slope. we add that to a running total
    //so that the direction of successive layers are based on the
    //past layers
    float3 h = 0;
    float a = 0.7 * (smoothstep(0.3, 0.5, n.x * 0.5 + 0.5)); //smooth the valleys
    float f = 1.0;
    for (int k = 0; k < 5; k++) {
        h += erosion(p * f, dir + h.zy * float2(1.0, -1.0)) * a * float3(1.0, f, f);
        a *= 0.4;
        f *= 2.0;
    }
    //remap height to [0,1] and add erosion
    //looks best when erosion amount is small
    //not sure about adding the normals together, but it looks okay
    return float3(smoothstep(-1.0, 1.0, n.x) + h.x * 0.05, (n.yz + h.yz) * 0.5 + 0.5);
}

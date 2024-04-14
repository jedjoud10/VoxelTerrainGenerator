// The MIT License
// https://www.youtube.com/c/InigoQuilez
// https://iquilezles.org/
// Copyright © 2014 Inigo Quilez
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


// This is a procedural pattern that has 2 parameters, that generalizes cell-noise, 
// perlin-noise and voronoi, all of which can be written in terms of the former as:
//
// cellnoise(x) = pattern(0,0,x)
// perlin(x) = pattern(0,1,x)
// voronoi(x) = pattern(1,0,x)
//
// From this generalization of the three famouse patterns, a new one (which I call 
// "Voronoise") emerges naturally. It's like perlin noise a bit, but within a jittered 
// grid like voronoi):
//
// voronoise(x) = pattern(1,1,x)
//
// Not sure what one would use this generalization for, because it's slightly slower 
// than perlin or voronoise (and certainly much slower than cell noise), and in the 
// end as a shading TD you just want one or another depending of the type of visual 
// features you are looking for, I can't see a blending being needed in real life.  
// But well, if only for the math fun it was worth trying. And they say a bit of 
// mathturbation can be healthy anyway!
//
// More info here: https://iquilezles.org/articles/voronoise

// More Voronoi shaders:
//
// Exact edges:  https://www.shadertoy.com/view/ldl3W8
// Hierarchical: https://www.shadertoy.com/view/Xll3zX
// Smooth:       https://www.shadertoy.com/view/ldB3zc
// Voronoise:    https://www.shadertoy.com/view/Xd23Dh

// All noise functions here:
//
// https://www.shadertoy.com/playlist/fXlXzf&from=0&num=12

float voronoise( in float2 p, float u, float v )
{
	float k = 1.0+63.0*pow(1.0-v,6.0);

    float2 i = floor(p);
    float2 f = frac(p);
    
	float2 a = float2(0.0,0.0);
    for( int y=-2; y<=2; y++ )
    for( int x=-2; x<=2; x++ )
    {
        float2  g = float2( x, y );
		float3  o = hash32( i + g )*float3(u,u,1.0);
		float2  d = g - f + o.xy;
		float w = pow( 1.0-smoothstep(0.0,1.414,length(d)), k );
		a += float2(o.z*w,w);
    }

    if (a.y == 0.0) {
        return a.x;
    } else {
        return a.x/a.y;
    }
}
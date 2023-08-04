
// fBM noise, uses 2D simplex noise
float fbm(float2 pos, uint octaves, float persistence, float lacunarity) {
    float final = 0.0;
    float scale = 1.0;
    float amplitude = 1.0;

    for(uint i = 0; i < octaves; i++) {
        final += snoise(pos * scale + random2(float(i))) * amplitude;
        scale *= lacunarity;
        amplitude *= persistence;
    }

    return final;
}

// fBM noise, uses 3D simplex noise
float fbm(float3 pos, uint octaves, float persistence, float lacunarity) {
    float final = 0.0;
    float scale = 1.0;
    float amplitude = 1.0;

    for(uint i = 0; i < octaves; i++) {
        final += snoise(pos * scale + random3(float(i))) * amplitude;
        scale *= lacunarity;
        amplitude *= persistence;
    }

    return final;
}

// fBM noise, uses 2D worley noise
float2 fbmCellular(float2 pos, uint octaves, float persistence, float lacunarity) {
    float2 final = float2(0.0, 0.0);
    float scale = 1.0;
    float amplitude = 1.0;

    for(uint i = 0; i < octaves; i++) {
        final += (cellular(pos * scale + random2(float(i)))-float2(0.5, 0.5)) * amplitude;
        scale *= lacunarity;
        amplitude *= persistence;
    }

    return final;
}

// fBM noise, uses 3D worley noise
float2 fbmCellular(float3 pos, uint octaves, float persistence, float lacunarity) {
    float2 final = float2(0.0, 0.0);
    float scale = 1.0;
    float amplitude = 1.0;

    for(uint i = 0; i < octaves; i++) {
        final += (cellular(pos * scale + random3(float(i)))-float2(0.5, 0.5)) * amplitude;
        scale *= lacunarity;
        amplitude *= persistence;
    }
    
    return final;
}
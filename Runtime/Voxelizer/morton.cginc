// Morton encoding from
// Stolen from https://github.com/johnsietsma/InfPoints/blob/master/com.infpoints/Runtime/Morton.cs
uint part1By2_32(uint x)
{
    x &= 0x3FF;  // x = ---- ---- ---- ---- ---- --98 7654 3210
    x = (x ^ (x << 16)) & 0xFF0000FF;  // x = ---- --98 ---- ---- ---- ---- 7654 3210
    x = (x ^ (x << 8)) & 0x300F00F;  // x = ---- --98 ---- ---- 7654 ---- ---- 3210
    x = (x ^ (x << 4)) & 0x30C30C3;  // x = ---- --98 ---- 76-- --54 ---- 32-- --10
    x = (x ^ (x << 2)) & 0x9249249;  // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
    return x;
}

uint encodeMorton32(uint3 coordinate)
{
    return (part1By2_32(coordinate.z) << 2) + (part1By2_32(coordinate.y) << 1) + part1By2_32(coordinate.x);
}


// taken from the voxels utils class
uint3 indexToPos(uint index)
{
    // N(ABC) -> N(A) x N(BC)
    uint y = index / (size * size);   // x in N(A)
    uint w = index % (size * size);  // w in N(BC)

    // N(BC) -> N(B) x N(C)
    uint z = w / size;// y in N(B)
    uint x = w % size;        // z in N(C)
    return uint3(x, y, z);
}
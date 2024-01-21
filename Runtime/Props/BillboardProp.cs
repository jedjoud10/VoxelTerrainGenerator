using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Data container for how billboard props are to be rendered
// Simply contains the textures to be used and the material to use
public struct BillboardProp {
    public Texture2D albedoTexture;
    public Texture2D normalTexture;
    public Material material;
}
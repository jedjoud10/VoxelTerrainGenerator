using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif
using UnityEngine;

// The monobehaviour that will actually compile the transpiled code into a shader in the editor and automatically apply it
[ExecuteInEditMode]
public class VoxelGraphCompiler: MonoBehaviour {
    // Takes in the voxel graph, transpiles it, and compiles it to a proper compute shader
    public void Compile(VoxelGraph graph) {
#if UNITY_EDITOR
        string source = graph.Transpile();
        //AssetImportContext ctx = new AssetImportContext();
        //ShaderUtil.CreateComputeShaderAsset(, source);
#else
        Debug.LogError("Cannot transpile code at runtime");
#endif
    }
}

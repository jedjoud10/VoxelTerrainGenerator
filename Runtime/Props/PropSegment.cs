using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// A prop segment that either contains gameobject props or indirectly drawn props
public class PropSegment : MonoBehaviour {
    // 0 => spawn gameobjects 
    // 1 => indirectly draw the props
    // 2 => billboard
    public int lod;

    // Used for indirect instanced rendering of props
    public List<(int, ComputeBuffer, Prop)> instancedIndirectProps;

    // Used for indirect instanced rendering of billboard of props
    public List<(int, ComputeBuffer, BillboardProp)> billboardProps;


    // Render the indirect props or billboards if necessary
    public void Update() {
        if (VoxelTerrain.Instance.VoxelProps.renderInstancedMeshes) {
            if (instancedIndirectProps != null) {
                foreach (var prop in instancedIndirectProps) {
                    RenderParams renderParams = new RenderParams(prop.Item3.instancedMeshMaterial);
                    renderParams.worldBounds = new Bounds {
                        min = transform.position,
                        max = transform.position + Vector3.one * VoxelUtils.PropSegmentSize,
                    };

                    renderParams.matProps = new MaterialPropertyBlock();
                    renderParams.matProps.SetBuffer("_BlittablePropBuffer", prop.Item2);
                    renderParams.matProps.SetVector("_BoundsOffset", renderParams.worldBounds.center);

                    Mesh mesh = prop.Item3.instancedMesh;

                    for (int i = 0; i < mesh.subMeshCount; i++) {
                        Graphics.RenderMeshPrimitives(renderParams, mesh, i, prop.Item1);
                    }
                }
            }

            if (billboardProps != null) {
                foreach (var prop in billboardProps) {
                    RenderParams renderParams = new RenderParams(prop.Item3.material);
                    renderParams.worldBounds = new Bounds {
                        min = transform.position,
                        max = transform.position + Vector3.one * VoxelUtils.PropSegmentSize,
                    };

                    renderParams.matProps = new MaterialPropertyBlock();
                    renderParams.matProps.SetBuffer("_BlittablePropBuffer", prop.Item2);
                    renderParams.matProps.SetVector("_BoundsOffset", renderParams.worldBounds.center);
                    renderParams.matProps.SetTexture("_Albedo", prop.Item3.albedoTexture);
                    renderParams.matProps.SetTexture("_Normal_Map", prop.Item3.normalTexture);
                    
                    Mesh mesh = VoxelTerrain.Instance.VoxelProps.quadBillboard;
                    Graphics.RenderMeshPrimitives(renderParams, mesh, 0, prop.Item1);
                }
            }
        }
    }
}
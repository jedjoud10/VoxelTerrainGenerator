using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A prop segment that either contains gameobject props or indirectly drawn props
public class PropSegment : MonoBehaviour {
    // 0 => spawn gameobjects 
    // 1 => indirectly draw the props
    // 2 => billboard
    public int lod;

    // List of compute buffers (containg the blittable prop type) and the corresponding prop type
    // Used solely for indirectly rendered props
    public List<(int, ComputeBuffer, Prop)> instancedIndirectProps;

    // Render the indirect props or billboards if necessary
    public void Update() {
        if (instancedIndirectProps != null && VoxelTerrain.Instance.VoxelProps.renderInstancedMeshes) {
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
    }
}
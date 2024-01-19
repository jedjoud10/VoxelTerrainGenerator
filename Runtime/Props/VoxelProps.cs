using System.Collections.Generic;
using Unity.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    public bool debugGizmos = false;

    // Each "prop" chunk will be roughly 8 times the size of a normal chunk
    // This depicts the number of prop voxels within that chunk
    [Range(4, 32)]
    public int propChunkResolution = 32;
    [Range(8, 64)]
    public int propChunkScale = 4;
    
    // List of props that we will generated based on their index
    [SerializeField]
    public List<Prop> props;

    // Called when we generate the prop voxel data for a prop chunk
    public delegate void PropVoxelDataGenerated();
    public event PropVoxelDataGenerated onPropChunkGeneration;

    // Called when we generate the prefab of a new prop (or reuse one)
    public delegate void PropAddedToWorld(GameObject go);
    public event PropAddedToWorld onPropSpawned;

    // Compute shader that will be responsble for prop  generation
    public ComputeShader propShader;

    // List of compute buffers used to contain generated prop data
    private List<ComputeBuffer> computeBuffers;

    private void OnValidate() {
        if (terrain == null) {
            propChunkResolution = Mathf.ClosestPowerOfTwo(propChunkResolution);
            propChunkScale = Mathf.ClosestPowerOfTwo(propChunkScale);
            VoxelUtils.PropChunkResolution = propChunkResolution;
            VoxelUtils.PropChunkScale = propChunkScale;
        }
    }

    internal override void Init() {
        computeBuffers = new List<ComputeBuffer>();

        for (int i = 0; i < props.Count; i++) {
            var buffer = new ComputeBuffer(500, VoxelUtils.GPUPropStride, ComputeBufferType.Append);



            computeBuffers.Add(buffer);
        }
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            float test = ((float)VoxelUtils.PropChunkScale);
            int half = VoxelUtils.PropChunkCount / 2;
            for (int x = -half; x < half; x++) {
                for (int y = -half; y < half; y++) {
                    for (int z = -half; z < half; z++) {
                        Vector3 center = new Vector3(x, y, z) * VoxelUtils.PropChunkScale - Vector3.one * test;
                        Gizmos.DrawWireCube(center, Vector3.one * test * 2.0f);
                    }
                }
            }
        }
    }

    internal override void Dispose() {
    }
}

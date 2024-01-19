using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    public bool debugGizmos = false;

    // Prop resolution per segment
    [Range(4, 64)]
    public int propSegmentResolution = 32;
    
    // How many voxel chunks fit in a prop segment
    [Range(8, 64)]
    public int voxelChunksInPropSegment = 8;
    
    // List of props that we will generated based on their index
    [SerializeField]
    public List<Prop> props;

    // Compute shader that will be responsble for prop  generation
    public ComputeShader propShader;

    // List of compute buffers used to contain generated prop data
    // Contains the append buffer and the count buffer
    private List<(ComputeBuffer, ComputeBuffer)> computeBuffers;

    // Dictionary for all the prop segments that are in use by LOds
    private HashSet<int3> propSegments;

    private void OnValidate() {
        if (terrain == null) {
            propSegmentResolution = Mathf.ClosestPowerOfTwo(propSegmentResolution);
            voxelChunksInPropSegment = Mathf.ClosestPowerOfTwo(voxelChunksInPropSegment);
            VoxelUtils.PropChunkResolution = propSegmentResolution;
            VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        }
    }

    // Update the static world generation fields (will also update the seed)
    public void UpdateStaticComputeFields() {
        propShader.SetVector("worldOffset", terrain.VoxelGenerator.worldOffset);
        propShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale * VoxelUtils.VoxelSizeFactor);
        var permutationSeed = terrain.VoxelGenerator.permutationSeed;
        var moduloSeed = terrain.VoxelGenerator.moduloSeed;
        propShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        propShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
    }

    // How to generate props:
    // 3 different lods, based on camera distance
    // lod 0: prefabs generated on highest res chunk. needed for player interaction
    //     needed per high lod chunk
    // lod 1: indirectly drawn, no prefabs, still uses full mesh
    //     second lod of the prop chunk stuff
    // lod 2: billboards!!!! fully indirectly drawn
    //     everything else? maybe with dist limit   

    internal override void Init() {
        UpdateStaticComputeFields();
        propSegments = new HashSet<int3>();
        terrain.VoxelOctree.onOctreeChanged += UpdatePropSegments;
        computeBuffers = new List<(ComputeBuffer, ComputeBuffer)>();

        for (int i = 0; i < props.Count; i++) {
            var appendBuffer = new ComputeBuffer(50, Marshal.SizeOf(new BlittableProp()), ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            appendBuffer.SetCounterValue(0);
            computeBuffers.Add((appendBuffer, countBuffer));
        }
    }

    // Called when the octree changes to update the currently active prop segments
    private void UpdatePropSegments(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed) {
        foreach (var item in removed) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                propSegments.Remove((int3)item.position / VoxelUtils.PropSegmentSize);
            }
        }

        foreach (var item in added) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                propSegments.Add((int3)item.position / VoxelUtils.PropSegmentSize);
            }
        }
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            Gizmos.color = Color.green;
            foreach (var item in propSegments) {
                Vector3 center = new Vector3(item.x, item.y, item.z) * size + Vector3.one * size / 2.0f;
                Gizmos.DrawWireCube(center, Vector3.one * size);
            }
        }
    }

    internal override void Dispose() {
    }
}

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Linq;
using UnityEditor;

// Utilities for GPU stuff and shits
public partial class VoxelProps {
    // Prop GPU copy, block search, and bitmask removal (basically a GPU allocator at this point lol)
    [Header("Prop GPU allocator and culler")]
    public ComputeShader propFreeBlockSearch;
    public ComputeShader propFreeBlockCopy;
    public ComputeShader propCullingCopy;
    public ComputeShader propCullingApply;
    public ComputeShader removePropSegments;

    // Buffer containing the temp offset, perm offset, and culled offset for all prop types
    private ComputeBuffer propSectionOffsetsBuffer;
    private uint3[] propSectionOffsets;

    // Max distance clamping
    private ComputeBuffer maxDistanceBuffer;
    private float[] maxDistances;

    // Mesh index count buffer
    private ComputeBuffer meshIndexCountBuffer;
    private uint[] meshIndexCount;

    // Temp generation
    private ComputeBuffer tempPropBuffer;
    private ComputeBuffer tempCountBuffer;
    private ComputeBuffer tempIndexBuffer;

    // Perm copy
    private ComputeBuffer permBitmaskBuffer;
    private ComputeBuffer permPropBuffer;

    // Culling and drawing
    private ComputeBuffer culledPropBuffer;
    private ComputeBuffer culledCountBuffer;
    private GraphicsBuffer drawArgsBuffer;

    // Used for lookup and management
    private ComputeBuffer segmentsToRemoveBuffer;
    private ComputeBuffer segmentIndexCountBuffer;
    private NativeBitArray unusedSegmentLookupIndices;

    private ComputeBuffer tempCount;

    // Update the static world generation fields (will also update the seed)
    public void UpdateStaticComputeFields() {
        var permutationSeed = terrain.VoxelGenerator.permutationSeed;
        var moduloSeed = terrain.VoxelGenerator.moduloSeed;
        var voxelShader = terrain.VoxelGenerator.voxelShader;

        // Set temp buffers used for basic generation
        propShader.SetBuffer(0, "tempProps", tempPropBuffer);
        propShader.SetBuffer(0, "tempCounters", tempCountBuffer);
        propShader.SetBuffer(0, "propSectionOffsets", propSectionOffsetsBuffer);
        propShader.SetBuffer(0, "affectedPropsBitMask", ignorePropBitmaskBuffer);

        // Set generation/world settings
        voxelShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        voxelShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        propShader.SetVector("worldOffset", terrain.VoxelGenerator.worldOffset);
        propShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale);
        propShader.SetFloat("voxelSize", VoxelUtils.VoxelSizeFactor);
        propShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        propShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        propShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        propShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        propShader.SetInt("propCount", props.Count);

        // Set shared voxel shader and ray-tracing shader
        propShader.SetTexture(0, "_Voxels", propSegmentDensityVoxels);

        voxelShader.SetTexture(1, "cachedPropDensities", propSegmentDensityVoxels);
        voxelShader.SetTexture(2, "cachedPropDensities", propSegmentDensityVoxels);
        onInitComputeCustom?.Invoke(propShader);
    }

    // Initialize CPU and GPU buffers
    private void InitGpuRelatedStuff() {
        // Temp buffers used for first step in prop generation
        int tempSum = props.Select(x => x.maxPropsPerSegment).Sum();
        tempCountBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        tempPropBuffer = new ComputeBuffer(tempSum, BlittableProp.size, ComputeBufferType.Structured);

        // Secondary buffers used for temp -> perm data copy
        tempCount = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Structured);
        tempIndexBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        int permSum = props.Select(x => x.maxPropsInTotal).Sum();
        int permMax = props.Select(x => x.maxPropsInTotal).Max();
        maxPermPropCount = permMax;
        permPropBuffer = new ComputeBuffer(permSum, BlittableProp.size, ComputeBufferType.Structured);
        permBitmaskBuffer = new ComputeBuffer(permMax, sizeof(uint), ComputeBufferType.Structured);

        // Tertiary buffers used for culling
        culledCountBuffer = new ComputeBuffer(props.Count, sizeof(int));
        drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, props.Count, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        int visibleSum = props.Select(x => x.maxVisibleProps).Sum();
        culledPropBuffer = new ComputeBuffer(visibleSum, BlittableProp.size);

        // More settings!!!
        maxDistances = new float[props.Count];
        meshIndexCount = new uint[props.Count];
        maxDistanceBuffer = new ComputeBuffer(props.Count, sizeof(float));
        meshIndexCountBuffer = new ComputeBuffer(props.Count, sizeof(uint));

        // Other stuff (still related to prop gen and GPU alloc)
        propSectionOffsetsBuffer = new ComputeBuffer(props.Count, sizeof(int) * 3);
        propSectionOffsets = new uint3[props.Count];
        segmentIndexCountBuffer = new ComputeBuffer(VoxelUtils.MaxSegments * props.Count, sizeof(uint) * 2, ComputeBufferType.Structured);
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(VoxelUtils.PropSegmentResolution, GraphicsFormat.R32_SFloat);
        unusedSegmentLookupIndices = new NativeBitArray(VoxelUtils.MaxSegments, Allocator.Persistent);
        unusedSegmentLookupIndices.Clear();
        segmentsToRemoveBuffer = new ComputeBuffer(VoxelUtils.MaxSegmentsToRemove, sizeof(int));
    }

    // Called by VoxelRegion when we should remove the regions (and thus hide their billboarded props) on the GPU
    private void RemoveRegionsGpu(ref NativeList<int4> removedSegments) {
        if (removedSegments.Length == 0)
            return;

        int[] indices = Enumerable.Repeat(-1, VoxelUtils.MaxSegmentsToRemove).ToArray();
        for (int i = 0; i < removedSegments.Length; i++) {
            var pos = removedSegments[i];
            if (terrain.VoxelSegments.propSegmentsDict.TryGetValue(pos, out Segment val)) {
                indices[i] = val.indexRangeLookup;
            } else {
                indices[i] = -1;
            }
        }

        // TODO: Test fluke? One time when tested (spaz) shit didn't work. DEBUG PLS
        // Also figure out why this causes a dx11 driver crash lel
        segmentsToRemoveBuffer.SetData(indices);
        removePropSegments.SetInt("segmentsToRemoveCount", removedSegments.Length);
        removePropSegments.SetBuffer(0, "usedBitmask", permBitmaskBuffer);
        removePropSegments.SetBuffer(0, "segmentIndices", segmentsToRemoveBuffer);
        removePropSegments.SetBuffer(0, "segmentIndexCount", segmentIndexCountBuffer);
        removePropSegments.SetInt("propCount", props.Count);
        removePropSegments.Dispatch(0, Mathf.CeilToInt((float)removedSegments.Length / 32.0f), props.Count, 1);
    }

    // Clear out all old GPU data for props and reset it
    internal void ResetPropData() {
        // Secondary buffers used for temp -> perm data copy
        tempIndexBuffer.SetData(new int[props.Count]);
        int permSum = props.Select(x => x.maxPropsInTotal).Sum();
        int permMax = props.Select(x => x.maxPropsInTotal).Max();
        int visibleSum = props.Select(x => x.maxVisibleProps).Sum();

        permPropBuffer.SetData(new BlittableProp[permSum]);
        permBitmaskBuffer.SetData(new uint[permMax]);

        // Tertiary buffers used for culling
        culledCountBuffer.SetData(new int[props.Count]);
        drawArgsBuffer.SetData(new GraphicsBuffer.IndirectDrawIndexedArgs[props.Count]);
        culledPropBuffer.SetData(new BlittableProp[visibleSum]);

        // Other stuff (still related to prop gen and GPU alloc)
        segmentIndexCountBuffer.SetData(new uint[VoxelUtils.MaxSegments * props.Count * 2]);
        unusedSegmentLookupIndices.Clear();
    }

    // Copies the necessary data from temporary to permanent memory
    private void CopyTempMemToPermGpuSide(Segment segment) {
        // Create a mask for only billboarded props
        int billboardMask = 0;

        for (int i = 0; i < props.Count; i++) {
            bool prefabsSpawn = props[i].WillSpawnPrefab && segment.spawnPrefabs;
            bool billboardRender = props[i].WillRenderIndirectInstances;
            if (!prefabsSpawn && billboardRender) {
                billboardMask |= 1 << i;
            }
        }

        // Find a free index that we can for indirectly referencing ranges and count buffers
        int lookup = unusedSegmentLookupIndices.Find(0, 1);

        if (lookup < unusedSegmentLookupIndices.Length) {
            segment.indexRangeLookup = lookup;
            unusedSegmentLookupIndices.Set(segment.indexRangeLookup, true);

            // Run the "find" shader that will find free indices that we can copy our temp memory into
            tempIndexBuffer.SetData(Enumerable.Repeat(uint.MaxValue, props.Count).ToArray());
            propFreeBlockSearch.SetInt("enabledProps", billboardMask);
            propFreeBlockSearch.SetBuffer(0, "tempCounters", tempCountBuffer);
            propFreeBlockSearch.SetBuffer(0, "tempIndices", tempIndexBuffer);
            propFreeBlockSearch.SetBuffer(0, "propSectionOffsets", propSectionOffsetsBuffer);
            propFreeBlockSearch.SetBuffer(0, "usedBitmask", permBitmaskBuffer);
            int count = Mathf.CeilToInt((float)permBitmaskBuffer.count / (32.0f));
            propFreeBlockSearch.Dispatch(0, count, props.Count, 1);

            // Copy the generated prop data to the perm data using a single compute dispatch
            propFreeBlockCopy.SetInt("propCount", props.Count);
            propFreeBlockCopy.SetBuffer(0, "propSectionOffsets", propSectionOffsetsBuffer);
            propFreeBlockCopy.SetInt("segmentLookup", segment.indexRangeLookup);
            propFreeBlockCopy.SetBuffer(0, "segmentIndexCount", segmentIndexCountBuffer);
            propFreeBlockCopy.SetBuffer(0, "tempCounters", tempCountBuffer);

            propFreeBlockCopy.SetBuffer(0, "tempIndices", tempIndexBuffer);
            propFreeBlockCopy.SetBuffer(0, "usedBitmask", permBitmaskBuffer);

            propFreeBlockCopy.SetBuffer(0, "tempProps", tempPropBuffer);
            propFreeBlockCopy.SetBuffer(0, "permProps", permPropBuffer);

            int count2 = Mathf.CeilToInt((float)permBitmaskBuffer.count / 32.0f);
            propFreeBlockCopy.Dispatch(0, maxPermPropCount / 32, props.Count, 1);
        } else {
            segment.indexRangeLookup = -1;
            Debug.LogWarning("Could not find a free bit, skipping segment gen (blame Jed)");
        }
    }
}

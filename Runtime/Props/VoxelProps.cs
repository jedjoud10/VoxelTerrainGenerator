using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    public bool debugGizmos = false;

    [Min(0)]
    public int maxComputeBufferSize = 1;

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
    private Dictionary<int3, GameObject> propSegments;

    // When we load in a prop segment
    public delegate void PropSegmentLoaded(int3 position, GameObject segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // When we unload a prop segment
    public delegate void PropSegmentUnloaded(int3 position, GameObject segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Pooled prop segments that we can reuse
    internal List<GameObject> pooledPropSegments;

    private void OnValidate() {
        if (terrain == null) {
            propSegmentResolution = Mathf.ClosestPowerOfTwo(propSegmentResolution);
            voxelChunksInPropSegment = Mathf.ClosestPowerOfTwo(voxelChunksInPropSegment);
            VoxelUtils.PropSegmentResolution = propSegmentResolution;
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
        propShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        propShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
    }

    internal override void Init() {
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        UpdateStaticComputeFields();
        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;
        propSegments = new Dictionary<int3, GameObject>();
        pooledPropSegments = new List<GameObject>();
        terrain.VoxelOctree.onOctreeChanged += UpdatePropSegments;
        computeBuffers = new List<(ComputeBuffer, ComputeBuffer)>();

        for (int i = 0; i < props.Count; i++) {
            var appendBuffer = new ComputeBuffer(maxComputeBufferSize, Marshal.SizeOf(new BlittableProp()), ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            appendBuffer.SetCounterValue(0);
            computeBuffers.Add((appendBuffer, countBuffer));
        }
    }

    // Fetches a pooled prop segment, or creates a new one from scratch
    private GameObject FetchPooledPropSegment() {
        GameObject go;

        if (pooledPropSegments.Count == 0) {
            GameObject obj = new GameObject("Prop Segment");
            obj.transform.SetParent(transform, false);
            go = obj;
        } else {
            go = pooledPropSegments[0];
            pooledPropSegments.RemoveAt(0);
        }

        go.SetActive(true);
        return go;
    }

    // Called when the octree changes to update the currently active prop segments
    private void UpdatePropSegments(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed) {
        Dictionary<int3, GameObject> copy = new Dictionary<int3, GameObject>(propSegments);

        foreach (var item in removed) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                propSegments.Remove((int3)item.position / VoxelUtils.PropSegmentSize);
            }
        }

        foreach (var item in added) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                GameObject propSegment = FetchPooledPropSegment();
                propSegment.transform.position = item.position;
                propSegments.Add((int3)item.position / VoxelUtils.PropSegmentSize, propSegment);
            }
        }

        // Extremely stupid and naive but eh will fix later
        // check removed
        foreach (var item in copy) {
            if (!propSegments.Contains(item)) {
                onPropSegmentUnloaded?.Invoke(item.Key, item.Value);
            }
        }

        // check added
        foreach (var item in propSegments) {
            if (!copy.Contains(item)) {
                onPropSegmentLoaded?.Invoke(item.Key, item.Value);
            }
        }
    }

    // Called when a new prop segment is loaded
    private void OnPropSegmentLoad(int3 position, GameObject segment) {
        foreach (var propType in props) {
            (ComputeBuffer propsBuffer, ComputeBuffer countBuffer) = computeBuffers[0];
            propsBuffer.SetCounterValue(0);
            countBuffer.SetData(new int[] { 0 });
            propShader.SetBuffer(0, "props", propsBuffer);
            propShader.SetVector("propChunkOffset", segment.transform.position);


            int _count = VoxelUtils.PropSegmentResolution / 4;
            propShader.Dispatch(0, _count, _count, _count);

            ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);
            int[] count = new int[1] { 0 };
            countBuffer.GetData(count);

            BlittableProp[] generatedProps = new BlittableProp[count[0]];
            propsBuffer.GetData(generatedProps);

            /*
            RenderParams r = new RenderParams();
            Graphics.RenderMeshInstanced(r, test, 0, generatedProps);
            */

            foreach (var prop in generatedProps) {
                GameObject propGameObject = Instantiate(propType.prefab);
                propGameObject.transform.SetParent(segment.transform);
                float3 propPosition = prop.position_and_scale.xyz;
                float3 propRotation = prop.euler_angles_padding.xyz;
                float propScale = prop.position_and_scale.w;
                propGameObject.transform.position = propPosition;
                propGameObject.transform.localScale = Vector3.one * propScale;
                propGameObject.transform.eulerAngles = propRotation;
            }
        }
    }

    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(int3 position, GameObject segment) {
        segment.SetActive(false);

        for (int i = 0; i < segment.transform.childCount; i++) {
            Destroy(segment.transform.GetChild(i).gameObject);
        }

        pooledPropSegments.Add(segment);
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            Gizmos.color = Color.green;
            foreach (var item in propSegments) {
                var key = item.Key;
                Vector3 center = new Vector3(key.x, key.y, key.z) * size + Vector3.one * size / 2.0f;
                Gizmos.DrawWireCube(center, Vector3.one * size);
            }
        }
    }

    internal override void Dispose() {
    }
}

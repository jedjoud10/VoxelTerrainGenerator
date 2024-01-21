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
    private Dictionary<int3, PropSegment> propSegments;

    // When we load in a prop segment
    public delegate void PropSegmentLoaded(int3 position, PropSegment segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // When we unload a prop segment
    public delegate void PropSegmentUnloaded(int3 position, PropSegment segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Pooled prop segments that we can reuse
    public GameObject propSegmentPrefab;
    internal List<GameObject> pooledPropSegments;

    // Edited externally
    internal HashSet<TerrainLoader> targets;

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
        targets = new HashSet<TerrainLoader>();
        propSegments = new Dictionary<int3, PropSegment>();
        pooledPropSegments = new List<GameObject>();
        terrain.VoxelOctree.onOctreeChanged += UpdatePropSegments;
        computeBuffers = new List<(ComputeBuffer, ComputeBuffer)>();
    }

    // Fetches a pooled prop segment, or creates a new one from scratch
    private GameObject FetchPooledPropSegment() {
        GameObject go;

        if (pooledPropSegments.Count == 0) {
            GameObject obj = Instantiate(propSegmentPrefab);
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
        Dictionary<int3, PropSegment> copy = new Dictionary<int3, PropSegment>(propSegments);

        foreach (var item in removed) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                propSegments.Remove((int3)item.position / VoxelUtils.PropSegmentSize);
            }
        }

        foreach (var item in added) {
            if (item.size == VoxelUtils.PropSegmentSize) {
                GameObject propSegment = FetchPooledPropSegment();
                propSegment.transform.position = item.position;
                propSegments.Add((int3)item.position / VoxelUtils.PropSegmentSize, propSegment.GetComponent<PropSegment>());
            }
        }

        // Extremely stupid and naive but eh will fix later
        // check removed
        foreach (var item in copy) {
            if (!propSegments.ContainsKey(item.Key)) {
                onPropSegmentUnloaded?.Invoke(item.Key, item.Value);
            }
        }

        // check added
        foreach (var item in propSegments) {
            if (!copy.ContainsKey(item.Key)) {
                onPropSegmentLoaded?.Invoke(item.Key, item.Value);
            }
        }
    }

    // Called when a new prop segment is loaded
    // If the segment is LOD0, spawn the props directly as gameobjects
    // If the segment is LOD1, render the props as instanced indirect
    // If the segment is LOD2, render the props as billboarded instanced indirect
    private void OnPropSegmentLoad(int3 position, PropSegment segment) {
        segment.gameObject.SetActive(true);
        
        int minLod = 2;
        Vector3 center = segment.transform.position + Vector3.one * VoxelUtils.PropSegmentSize / 2.0f;

        foreach (var target in targets) {
            float distance = Vector3.Distance(target.transform.position, center);
            int lod = 2;

            if (distance < target.propSegmentPrefabSpawnerMaxDistance) {
                lod = 0;
            } else if (distance < target.propSegmentInstancedRendererLodMaxDistance) {
                lod = 1;
            }

            minLod = Mathf.Min(lod, minLod);
        }
        segment.lod = minLod;

        if (segment.lod == 1) {
            segment.instancedIndirectProps = new List<(int, ComputeBuffer, Prop)>();
        }

        foreach (var propType in props) {
            var propsBuffer = new ComputeBuffer(maxComputeBufferSize, Marshal.SizeOf(new BlittableProp()), ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            propsBuffer.SetCounterValue(0);

            propsBuffer.SetCounterValue(0);
            countBuffer.SetData(new int[] { 0 });
            propShader.SetBuffer(0, "props", propsBuffer);
            propShader.SetVector("propChunkOffset", segment.transform.position);
            int _count = VoxelUtils.PropSegmentResolution / 4;
            propShader.Dispatch(0, _count, _count, _count);

            if (segment.lod == 0) {
                SpawnPropPrefabs(segment, propType, propsBuffer, countBuffer);
            } else if (segment.lod == 1) {
                SetPropInstancedIndirect(segment, propType, propsBuffer, countBuffer);
            }
        }
    }

    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(int3 position, PropSegment segment) {
        segment.gameObject.SetActive(false);
        segment.instancedIndirectProps = null;

        for (int i = 0; i < segment.transform.childCount; i++) {
            Destroy(segment.transform.GetChild(i).gameObject);
        }

        pooledPropSegments.Add(segment.gameObject);
    }


    private void SpawnPropPrefabs(PropSegment segment, Prop propType, ComputeBuffer propsBuffer, ComputeBuffer countBuffer) {
        ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);
        int[] count = new int[1] { 0 };
        countBuffer.GetData(count);

        BlittableProp[] generatedProps = new BlittableProp[count[0]];
        propsBuffer.GetData(generatedProps);
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

        if (count[0] == 0) {
            segment.gameObject.SetActive(false);
        }
    }

    private void SetPropInstancedIndirect(PropSegment segment, Prop propType, ComputeBuffer propsBuffer, ComputeBuffer countBuffer) {
        ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);
        int[] count = new int[1] { 0 };
        countBuffer.GetData(count);
        segment.instancedIndirectProps.Add((count[0], propsBuffer, propType));

        if (count[0] == 0) {
            segment.gameObject.SetActive(false);
        }
    }


    // Will be responsible for rendering the instanced meshes and billboarded mesh
    // Also will update the LOD of prop segments based on terrain loaders around in the world
    void Update() {
        // mmm yeas I love frying my poor 3750h
        foreach (var segment in propSegments) {
            int minLod = 2;
            Vector3 center = segment.Value.transform.position + Vector3.one * VoxelUtils.PropSegmentSize / 2.0f;

            foreach (var target in targets) {
                float distance = Vector3.Distance(target.transform.position, center);
                int lod = 2;

                if (distance < target.propSegmentPrefabSpawnerMaxDistance) {
                    lod = 0;
                } else if (distance < target.propSegmentInstancedRendererLodMaxDistance) {
                    lod = 1;
                }

                minLod = Mathf.Min(lod, minLod);
            }
            segment.Value.lod = minLod;
        }    
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            foreach (var item in propSegments) {
                var key = item.Key;
                Vector3 center = new Vector3(key.x, key.y, key.z) * size + Vector3.one * size / 2.0f;

                switch (item.Value.lod) {
                    case 0:
                        Gizmos.color = Color.green;
                        break;
                    case 1:
                        Gizmos.color = Color.yellow;
                        break;
                    case 2:
                        Gizmos.color = Color.red;
                        break;
                    default:
                        break;
                }

                Gizmos.DrawWireCube(center, Vector3.one * size);
            }
        }
    }

    internal override void Dispose() {
    }
}

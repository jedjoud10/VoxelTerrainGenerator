using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;
using Unity.Netcode;
using UnityEditor;

// Responsible for generating the voxel props on the terrain
// Handles generating the prop data on the GPU and renders it as billboards 
public partial class VoxelProps : VoxelBehaviour {
    // List of props that we will generated based on their index
    [SerializeField]
    public List<PropType> props;

    // Generation shade that will generate the many types of props and their variants
    [Header("Generation")]
    public ComputeShader propShader;

    // Custom delegate that can be used to send custom data to the prop shader
    public delegate void InitComputeCustom(ComputeShader shader);
    public delegate void UpdateComputeCustom(ComputeShader shader, Segment segment);
    public event InitComputeCustom onInitComputeCustom;
    public event UpdateComputeCustom onUpdateComputeCustom;

    // Maximum value of the perm count of all prop types
    private int maxPermPropCount;
        
    // Pooled props and prop owners
    // TODO: Optimize
    private List<List<GameObject>>[] pooledPropGameObjects;
    private GameObject propOwner;

    // Used for collision and GPU based raycasting (supports up to 4 intersections within a ray)
    RenderTexture propSegmentDensityVoxels;

    private bool lastSegmentWasModified = false;
    int asyncRequestsInProcess = 0;

    // Checks if we completed prop generation
    public bool Free {
        get {
            return asyncRequestsInProcess == 0;
        }
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        // Pooling game object stuff
        pooledPropGameObjects = new List<List<GameObject>>[props.Count];
        propOwner = new GameObject("Props Owner GameObject");
        propOwner.transform.SetParent(transform);
        InitGpuRelatedStuff();

        // For now we are going to assume we will spawn only 1 variant of a prop type per segment
        ignorePropsBitmasks = new NativeHashMap<int3, NativeBitArray>(0, Allocator.Persistent);
        ignorePropBitmaskBuffer = new ComputeBuffer((VoxelUtils.PropSegmentResolution * VoxelUtils.PropSegmentResolution * VoxelUtils.PropSegmentResolution * props.Count) / 32, sizeof(uint));
        globalBitmaskIndexToLookup = new NativeHashMap<int4, int>(0, Allocator.Persistent);
        propTypeSerializedData = new NativeArray<PropTypeSerializedData>(props.Count, Allocator.Persistent);

        // Register to the voxel region events
        terrain.VoxelSegments.onPropSegmentLoaded += OnPropSegmentLoad;
        terrain.VoxelSegments.onPropSegmentUnloaded += OnPropSegmentUnload;
        terrain.VoxelSegments.onPropSegmetsPreRemoval += RemoveRegionsGpu;
        terrain.VoxelSegments.onSerializePropSegment += SerializePropsOnSegmentUnload;

        // Fetch the temp offset, perm offset, visible culled offset
        // Also spawns the object prop type owners and attaches them to the terrain
        uint3 last = uint3.zero;
        for (int i = 0; i < props.Count; i++) {
            pooledPropGameObjects[i] = new List<List<GameObject>>();
            PropType propType = props[i];
            for (int j = 0; j < propType.variants.Count; j++) {
                pooledPropGameObjects[i].Add(new List<GameObject>());
            }

            // We do a considerable amount of trolling
            propSectionOffsets[i] = last;
            var offset = new uint3(
                (uint)propType.maxPropsPerSegment,
                (uint)propType.maxPropsInTotal,
                (uint)propType.maxVisibleProps
            );
            last += offset;

            // Make sure all the variants have the same stride
            if (propType.variants.Count > 0) {
                SerializableProp first = propType.variants[0].prefab.GetComponent<SerializableProp>();
                Type type = first.GetType();
                if (propType.variants.Any(x => x.prefab.GetComponent<SerializableProp>().GetType() != type)) {
                    Debug.LogError("Variants MUST have the same SerializableProp script!");
                }

                // Initialize the serialized prop data buffers
                propTypeSerializedData[i] = new PropTypeSerializedData {
                    rawBytes = new NativeList<byte>(Allocator.Persistent),
                    set = new NativeBitArray((int)offset.x, Allocator.Persistent),
                    stride = first.Stride,
                    valid = true,
                };
            } else {
                propTypeSerializedData[i] = new PropTypeSerializedData() { valid = false };
            }

            // We do a considerable amount of trolling
            uint indexCount;
            if (propType.propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForInstancedMeshes) && propType.instancedMesh != null) {
                indexCount = propType.instancedMesh.GetIndexCount(0);
            } else {
                indexCount = 6;
            }

            meshIndexCount[i] = indexCount;
            maxDistances[i] = propType.maxInstancingDistance;
        }

        // Update static settings and capture the billboards
        propSectionOffsetsBuffer.SetData(propSectionOffsets);
        maxDistanceBuffer.SetData(maxDistances);
        meshIndexCountBuffer.SetData(meshIndexCount);
        UpdateStaticComputeFields();
        CaptureBillboards();
    }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(Segment segment) {
        segment.props = new Dictionary<int, (List<GameObject>, List<ushort>)>();

        // Quit early if we shouldn't do shit
        if (!props.Any(x => x.WillRenderIndirectInstances | (x.WillSpawnPrefab && segment.spawnPrefabs))) {
            return;
        }

        // Set the prop ignore bitmask buffer if needed
        if (ignorePropsBitmasks.ContainsKey(segment.regionPosition)) {
            var bitmask = ignorePropsBitmasks[segment.regionPosition];
            lastSegmentWasModified = true;
            ignorePropBitmaskBuffer.SetData(bitmask.AsNativeArrayExt<int>());
        } else if (lastSegmentWasModified) {
            ignorePropBitmaskBuffer.SetData(new int[ignorePropBitmaskBuffer.count]);
            lastSegmentWasModified = false;
        }

        // Execute the prop segment voxel cache compute shader
        int _count = VoxelUtils.PropSegmentResolution / 4;
        var voxelShader = terrain.VoxelGenerator.voxelShader;
        voxelShader.SetVector("propChunkOffset", segment.worldPosition);
        voxelShader.Dispatch(1, _count, _count, _count);

        // Set compute properties and run the compute shader
        tempCountBuffer.SetData(new int[props.Count]);
        onUpdateComputeCustom?.Invoke(propShader, segment);
        propShader.SetVector("propChunkOffset", segment.worldPosition);
        propShader.Dispatch(0, _count, _count, _count);

        // Create an async callback if we have ANY prop that must be spawned as a game object
        // props.Any(x => x.WillSpawnPrefab) && segment.spawnPrefabs || props.Any(x => x.propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForPrefabs))
        if (true) {
            for (int i = 0; i < props.Count; i++) {
                bool spawn = props[i].WillSpawnPrefab && segment.spawnPrefabs;
                bool forceInstanced = props[i].propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForInstancedMeshes);
                bool forcePrefab = props[i].propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForPrefabs);
                if ((spawn && !forceInstanced) || forcePrefab) {
                    segment.props.Add(i, (new List<GameObject>(), new List<ushort>()));
                }
            }

            asyncRequestsInProcess++;

            // TODO: Use this to eliminate reading back props that won't be spawned
            // must sort out the propSectionOffsets in order of "spawn" order first to do such a thing tho
            int size = tempPropBuffer.count * BlittableProp.size;
            int offset = 0;

            // Spawn the prefabs when we get the data back asynchronously
            AsyncGPUReadback.Request(
                tempPropBuffer,
                size, offset,
                delegate (AsyncGPUReadbackRequest asyncRequest) {
                    OnAsyncRequestDone(segment, asyncRequest);
                }
            );
        }

        CopyTempMemToPermGpuSide(segment);
    }


    // Fetches a pooled prop, or creates a new one from scratch
    // This will also handle *loading* in custom prop types from memory if there were modified
    GameObject FetchPooledProp(int i, int variant, PropType propType) {
        GameObject go;

        if (pooledPropGameObjects[i][variant].Count == 0) {
            GameObject obj = Instantiate(propType.variants[variant].prefab);
            obj.transform.SetParent(propOwner.transform, false);
            go = obj;
        } else {
            go = pooledPropGameObjects[i][variant][0];
            pooledPropGameObjects[i][variant].RemoveAt(0);
        }

        go.SetActive(true);
        return go;
    }

    // Called when the async request from the GPU to fetch prop data has completed
    private void OnAsyncRequestDone(Segment segment, AsyncGPUReadbackRequest asyncRequest) {
        if (segment.props == null)
            return;

        int[] count = new int[props.Count];
        tempCountBuffer.GetData(count);

        NativeArray<BlittableProp> data = asyncRequest.GetData<BlittableProp>();

        // Spawn all the props for all types
        for (int i = 0; i < props.Count; i++) {
            PropType propType = props[i];
            int offset = (int)propSectionOffsets[i].x;
            var serializedData = propTypeSerializedData[i];
            FastBufferReader? reader = null;
            if (serializedData.valid && serializedData.rawBytes.Length > 0)
                reader = new FastBufferReader(serializedData.rawBytes.AsArray(), Allocator.None, serializedData.rawBytes.Length, 0);

            if (!segment.props.ContainsKey(i))
                continue;

            // Spawn all the props of this specific type
            for (int k = 0; k < count[i]; k++) {
                BlittableProp prop = data[k + offset];

                // This will automatically handle deserialization for us
                ushort dispatchIndex = prop.dispatchIndex;
                byte variant = prop.variant;
                GameObject propGameObject = FetchPooledProp(i, variant, propType);
                SerializableProp serializableProp = propGameObject.GetComponent<SerializableProp>();

                // Deserialize any prop data that we might have
                int index = VoxelUtils.FetchPropBitmaskIndex(i, dispatchIndex);
                if (globalBitmaskIndexToLookup.TryGetValue(new int4(segment.regionPosition, index), out int elementLookup)) {
                    serializableProp.Variant = variant;
                    reader?.Seek(elementLookup * serializedData.stride);
                    reader?.ReadNetworkSerializableInPlace(ref serializableProp);
                    serializableProp.ElementIndex = elementLookup;
                }

                serializableProp.OnPropSpawn(prop);

                // Uncompress GPU data
                float3 position = new float3(prop.pos_x, prop.pos_y, prop.pos_z);
                float scale = prop.scale;
                float3 rotation = VoxelUtils.UncompressPropRotation(ref prop);

                // Set data
                propGameObject.transform.position = position;
                propGameObject.transform.localScale = Vector3.one * scale;
                propGameObject.transform.eulerAngles = rotation;
                segment.props[i].Item1.Add(propGameObject);
                segment.props[i].Item2.Add(dispatchIndex);
            }
        }

        asyncRequestsInProcess--;
    }

    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(Segment segment) {
        foreach (var collection in segment.props) {
            foreach (var item in collection.Value.Item1) {
                if (item != null) {
                    // TODO: Optimize
                    SerializableProp prop = item.GetComponent<SerializableProp>();
                    item.SetActive(false);
                    pooledPropGameObjects[collection.Key][prop.Variant].Add(item);
                }
            }
        }

        if (segment.indexRangeLookup != -1) {
            unusedSegmentLookupIndices.Set(segment.indexRangeLookup, false);
        }
        segment.indexRangeLookup = -1;
        segment.props = null;
    }
    internal override void Dispose() {
        drawArgsBuffer.Dispose();
        culledCountBuffer.Dispose();
        tempCountBuffer.Dispose();
        tempIndexBuffer.Dispose();
        tempPropBuffer.Dispose();
        propSectionOffsetsBuffer.Dispose();
        unusedSegmentLookupIndices.Dispose();
        ignorePropBitmaskBuffer.Dispose();

        foreach (var item in ignorePropsBitmasks) {
            item.Value.Dispose();
        }

        ignorePropsBitmasks.Dispose();
        globalBitmaskIndexToLookup.Dispose();

        foreach (var item in propTypeSerializedData) {
            item.rawBytes.Dispose();
            item.set.Dispose();
        }
        propTypeSerializedData.Dispose();
        permBitmaskBuffer.Dispose();
        segmentsToRemoveBuffer.Dispose();
        segmentIndexCountBuffer.Dispose();
        culledPropBuffer.Dispose();
        maxDistanceBuffer.Dispose();
        meshIndexCountBuffer.Dispose();
        permPropBuffer.Dispose();
    }
}

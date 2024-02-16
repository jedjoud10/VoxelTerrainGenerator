using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;
using Unity.Netcode;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using static VoxelRegions;

// Responsible for generating the voxel props on the terrain
// Handles generating the prop data on the GPU and renders it as billboards 
public class VoxelProps : VoxelBehaviour {
    // List of props that we will generated based on their index
    [SerializeField]
    public List<PropType> props;

    // Used for prop billboard captures
    [Header("Billboard Capture & Rendering")]
    public GameObject propCaptureCameraPrefab;
    public Material propCaptureFullscreenMaterial;
    public Mesh quadBillboard;
    public Material billboardMaterialBase;

    // Extra prop data that is shared with the prop segments
    private List<IndirectExtraPropData> extraPropData;

    // Generation shade that will generate the many types of props and their variants
    [Header("Generation")]
    public ComputeShader propShader;

    // Custom delegate that can be used to send custom data to the prop shader
    public delegate void InitComputeCustom(ComputeShader shader);
    public delegate void UpdateComputeCustom(ComputeShader shader, VoxelRegion segment);
    public event InitComputeCustom onInitComputeCustom;
    public event UpdateComputeCustom onUpdateComputeCustom;

    // Prop GPU copy, block search, and bitmask removal (basically a GPU allocator at this point lol)
    [Header("Prop GPU allocator and culler")]
    public ComputeShader propFreeBlockSearch;
    public ComputeShader propFreeBlockCopy;
    public ComputeShader propCullingCopy;
    public ComputeShader propCullingApply;
    public ComputeShader removePropSegments;

    // Modifiers and the ruleset/modifier bufer
    internal HashSet<VoxelPropsSpawnModifier> modifiersHashSet;
    private ComputeBuffer modifiersBuffer;

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

    // Maximum value of the perm count of all prop types
    private int maxPermPropCount;

    // Affected segment (those that contain modified props)
    internal NativeHashMap<int3, NativeBitArray> ignorePropsBitmasks;
    internal ComputeBuffer ignorePropBitmaskBuffer;
        
    // Lookup used for storing and referncing modified but not deleted props
    internal NativeHashMap<int4, int> globalBitmaskIndexToLookup;

    // Actual data that will be stored per prop type
    internal struct PropTypeSerializedData {
        public NativeList<byte> rawBytes;
        public int stride;
        public NativeBitArray set;
    }
    internal NativeArray<PropTypeSerializedData> propTypeSerializedData; 
    
    // Pooled props and prop owners
    // TODO: Optimize
    private List<List<GameObject>>[] pooledPropGameObjects;
    private GameObject propOwner;

    // Used for collision and GPU based raycasting (supports up to 4 intersections within a ray)
    RenderTexture propSegmentDensityVoxels;

    // 3 R textures (FLOAT4) that contain the position data for the respective intersection
    RenderTexture positionIntersectingTexture;

    private bool lastSegmentWasModified = false;
    int asyncRequestsInProcess = 0;

    // Checks if we completed prop generation
    public bool Free {
        get {
            return asyncRequestsInProcess == 0;
        }
    }

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
        propShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        propShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        propShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        propShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        propShader.SetInt("propCount", props.Count);

        // Set shared voxel shader and ray-tracing shader
        propShader.SetTexture(0, "_Voxels", propSegmentDensityVoxels);

        // It's GPU raytracing time!!!!
        voxelShader.SetTexture(1, "cachedPropDensities", propSegmentDensityVoxels);
        voxelShader.SetTexture(2, "cachedPropDensities", propSegmentDensityVoxels);
        voxelShader.SetTexture(2, "positionIntersections", positionIntersectingTexture);
        voxelShader.SetTexture(3, "positionIntersections", positionIntersectingTexture);
        propShader.SetTexture(0, "_PositionIntersections", positionIntersectingTexture);
        onInitComputeCustom?.Invoke(propShader);
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        int maxSegments = terrain.VoxelRegions.maxSegments;
        int maxSegmentsToRemove = terrain.VoxelRegions.maxSegmentsToRemove;

        // Pooling game object stuff
        pooledPropGameObjects = new List<List<GameObject>>[props.Count];
        propOwner = new GameObject("Props Owner GameObject");
        propOwner.transform.SetParent(transform);

        // Temp buffers used for first step in prop generation
        int tempSum = props.Select(x => x.maxPropsPerSegment).Sum();
        tempCountBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        tempPropBuffer = new ComputeBuffer(tempSum, BlittableProp.size, ComputeBufferType.Structured);

        // Secondary buffers used for temp -> perm data copy
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
        segmentIndexCountBuffer = new ComputeBuffer(maxSegments * props.Count, sizeof(uint) * 2, ComputeBufferType.Structured);
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(VoxelUtils.PropSegmentResolution, GraphicsFormat.R32_SFloat);
        unusedSegmentLookupIndices = new NativeBitArray(maxSegments, Allocator.Persistent);
        unusedSegmentLookupIndices.Clear();
        segmentsToRemoveBuffer = new ComputeBuffer(maxSegmentsToRemove, sizeof(int)); 

        // For now we are going to assume we will spawn only 1 variant of a prop type per segment
        ignorePropsBitmasks = new NativeHashMap<int3, NativeBitArray>(0, Allocator.Persistent);
        ignorePropBitmaskBuffer = new ComputeBuffer((VoxelUtils.PropSegmentResolution * VoxelUtils.PropSegmentResolution * VoxelUtils.PropSegmentResolution * props.Count) / 32, sizeof(uint));
        globalBitmaskIndexToLookup = new NativeHashMap<int4, int>(0, Allocator.Persistent);
        propTypeSerializedData = new NativeArray<PropTypeSerializedData>(props.Count, Allocator.Persistent);

        // Register to the voxel region events
        terrain.VoxelRegions.onPropSegmentLoaded += OnPropSegmentLoad;
        terrain.VoxelRegions.onPropSegmentUnloaded += OnPropSegmentUnload;
        terrain.VoxelRegions.onPropSegmetsPreRemoval += RemoveRegionsGpu;

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
                };
            } else {
                propTypeSerializedData[i] = new PropTypeSerializedData();
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

        // Used to create our textures
        RenderTexture CreateRayCastTexture(int size, GraphicsFormat format) {
            RenderTexture texture = new RenderTexture(size, size, 3, format);
            texture.height = size;
            texture.width = size;
            texture.depth = 0;
            texture.volumeDepth = 3;
            texture.dimension = TextureDimension.Tex2DArray;
            texture.enableRandomWrite = true;
            texture.Create();
            return texture;
        }

        // Create textures used for intersection tests and GPU raycasting
        positionIntersectingTexture = CreateRayCastTexture(VoxelUtils.PropSegmentResolution, GraphicsFormat.R16G16B16A16_SFloat);

        // Update static settings and capture the billboards
        propSectionOffsetsBuffer.SetData(propSectionOffsets);
        maxDistanceBuffer.SetData(maxDistances);
        meshIndexCountBuffer.SetData(meshIndexCount);
        UpdateStaticComputeFields();
        CaptureBillboards();
    }

    // Capture the billboards of all props sequentially
    private void CaptureBillboards() {
        // Create a prop capture camera to 
        extraPropData = new List<IndirectExtraPropData>();
        GameObject captureGo = Instantiate(propCaptureCameraPrefab);
        Camera cam = captureGo.GetComponent<Camera>();
        captureGo.layer = 31;
        cam.cullingMask = 1 << 31;

        // Capture all props (including variant types)
        foreach (var prop in props) {
            IndirectExtraPropData data = null;
            
            if (prop.WillRenderIndirectInstances && prop.variants.Count > 0) {
                data = CaptureBillboard(cam, prop);
            }
            
            extraPropData.Add(data);
        }

        Destroy(captureGo);
    }

    // Capture the albedo and normal array textures by spawning its variants temporarily
    public IndirectExtraPropData CaptureBillboard(Camera camera, PropType prop) {
        int width = prop.billboardTextureWidth;
        int height = prop.billboardTextureHeight;
        var temp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        camera.targetTexture = temp;

        Texture2DArray albedoTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, false);
        Texture2DArray normalTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, false);
        Texture2DArray maskTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, false);
        Texture2DArray[] tempOut = new Texture2DArray[3] { albedoTextureOut, normalTextureOut, maskTextureOut };

        for (int i = 0; i < prop.variants.Count; i++) {
            PropType.PropVariantType variant = prop.variants[i];
            camera.orthographicSize = variant.billboardCaptureCameraScale;

            GameObject faker = Instantiate(variant.prefab);
            faker.GetComponent<SerializableProp>().OnSpawnCaptureFake(camera, tempOut, i);
            faker.layer = 31;
            foreach (Transform item in faker.transform) {
                item.gameObject.layer = 31;
            }

            // Move the prop to the appropriate position
            faker.transform.position = variant.billboardCapturePosition;
            faker.transform.eulerAngles = variant.billboardCaptureRotation;

            // I love for looping inside a for loop inside a for loop inside a for loop yes yes yes
            for (int j = 0; j < 3; j++) {
                tempOut[j].filterMode = prop.billboardTextureFilterMode;
                propCaptureFullscreenMaterial.SetInteger("_TextureType", j);
                camera.Render();
                Graphics.CopyTexture(temp, 0, tempOut[j], i);
            }

            faker.GetComponent<SerializableProp>().OnDestroyCaptureFake();
            DestroyImmediate(faker);
            temp.DiscardContents(true, true);
            temp.Release();
        }

        return new IndirectExtraPropData {
            billboardAlbedoTexture = albedoTextureOut,
            billboardNormalTexture = normalTextureOut,
            billboardMaskTexture = maskTextureOut,
        };
    }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(VoxelRegion segment) {
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

        // Generate structures first

        // Fetch all the voxel prop spawners in this segment and apply them first
        // TODO: Actually implement this
        
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
                    if (segment.props == null)
                        return;

                    int[] count = new int[props.Count];
                    tempCountBuffer.GetData(count);

                    NativeArray<BlittableProp> data = asyncRequest.GetData<BlittableProp>();

                    // Spawn all the props for all types
                    for (int i = 0; i < props.Count; i++) {
                        PropType propType = props[i];
                        int offset = (int)propSectionOffsets[i].x;

                        if (!segment.props.ContainsKey(i))
                            continue;

                        // Spawn all the props of this specific type
                        for (int k = 0; k < count[i]; k++) {
                            BlittableProp prop = data[k + offset];

                            // This will automatically handle deserialization for us
                            ushort dispatchIndex = prop.dispatchIndex;
                            byte variant = prop.variant;
                            GameObject propGameObject = FetchPooledProp(i, variant, propType);

                            // TODO: Cache this, for perf reasons
                            SerializableProp serializableProp = propGameObject.GetComponent<SerializableProp>();
                            int index = VoxelUtils.FetchPropBitmaskIndex(i, dispatchIndex);
                            if (globalBitmaskIndexToLookup.TryGetValue(new int4(segment.regionPosition, index), out int elementLookup)) {
                                serializableProp.Variant = variant;
                                var serializedData = propTypeSerializedData[i];
                                FastBufferReader reader = new FastBufferReader(serializedData.rawBytes.AsArray(), Allocator.Temp, serializedData.stride, serializedData.stride * elementLookup);
                                reader.ReadNetworkSerializableInPlace(ref serializableProp);
                                reader.Dispose();
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
            );
        }

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
            Debug.LogWarning("Could not find a free bit, skipping segment gen");
        }
    }

    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(VoxelRegion segment) {
        foreach (var collection in segment.props) {
            foreach (var item in collection.Value.Item1) {
                if (item != null) {
                    // TODO: Optimize
                    SerializableProp prop = item.GetComponent<SerializableProp>();
                    item.SetActive(false);
                    pooledPropGameObjects[collection.Key][prop.Variant].Add(item);
                    Debug.Log("add back");
                }
            }
        }

        if (segment.indexRangeLookup != -1) {
            unusedSegmentLookupIndices.Set(segment.indexRangeLookup, false);
        }
        segment.indexRangeLookup = -1;
        segment.props = null;
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
            Debug.Log("fetch pooled");
        }

        go.SetActive(true);
        return go;
    }

    // Render the indirectly rendered (billboard / instanced) props 
    private void Update() {
        if (terrain.VoxelOctree.target == null)
            return;

        // Fetch camera from the terrain loader to use for prop billboard culling
        Camera camera = terrain.VoxelOctree.target.viewCamera;
        if (camera == null) {
            Debug.LogWarning("Terrain Loader does not have a viewCamera assigned. Will not render props correctly!");
            return;
        }

        // Cull the props all in one dispatch call
        culledCountBuffer.SetData(new int[props.Count]);
        propCullingCopy.SetBuffer(0, "propSectionOffsets", propSectionOffsetsBuffer);

        int count = Mathf.CeilToInt((float)maxPermPropCount / 32.0f);
        propCullingCopy.SetBuffer(0, "usedBitmask", permBitmaskBuffer);
        propCullingCopy.SetBuffer(0, "permProps", permPropBuffer);
        propCullingCopy.SetBuffer(0, "culledProps", culledPropBuffer);
        propCullingCopy.SetBuffer(0, "culledCount", culledCountBuffer);
        propCullingCopy.SetVector("cameraForward", camera.transform.forward);
        propCullingCopy.SetVector("cameraPosition", camera.transform.position);
        propCullingCopy.SetBuffer(0, "maxDistances", maxDistanceBuffer);
        propCullingCopy.Dispatch(0, count, props.Count, 1);

        // Apply culling counts to the indirect draw args
        propCullingApply.SetBuffer(0, "culledCount", culledCountBuffer);
        propCullingApply.SetBuffer(0, "drawArgs", drawArgsBuffer);
        propCullingApply.SetBuffer(0, "meshIndexCountPerInstance", meshIndexCountBuffer);
        propCullingApply.SetInt("propCount", props.Count);
        propCullingApply.Dispatch(0, 1, 1, 1);

        // Render all billboarded/instanced prop types using a single command per type
        for (int i = 0; i < props.Count; i++) {
            IndirectExtraPropData extraData = extraPropData[i];
            PropType prop = props[i];
            
            if (prop.WillRenderIndirectInstances)
                RenderInstancesOfType(i, extraData, prop);
        }
    }

    // Render the instanced mesh for a specific type of prop type
    // This is either the billboard or the given instanced mesh
    private void RenderInstancesOfType(int i, IndirectExtraPropData extraData, PropType prop) {
        bool meshed = prop.propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForInstancedMeshes);

        if (meshed && (prop.instancedMeshMaterial == null || prop.instancedMesh == null))
            return;

        Material material = meshed ? prop.instancedMeshMaterial : billboardMaterialBase;

        ShadowCastingMode shadowCastingMode = prop.billboardCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        RenderParams renderParams = new RenderParams(material);
        renderParams.shadowCastingMode = shadowCastingMode;
        renderParams.worldBounds = new Bounds {
            min = -Vector3.one * VoxelUtils.PropSegmentSize * 100000,
            max = Vector3.one * VoxelUtils.PropSegmentSize * 100000,
        };

        var mat = new MaterialPropertyBlock();
        renderParams.matProps = mat;
        mat.SetBuffer("_PropSectionOffsets", propSectionOffsetsBuffer);
        mat.SetBuffer("_BlittablePropBuffer", culledPropBuffer);
        mat.SetFloat("_PropType", (float)i);

        if (!meshed) {
            mat.SetTexture("_AlbedoArray", extraData.billboardAlbedoTexture);
            mat.SetTexture("_NormalMapArray", extraData.billboardNormalTexture);
            mat.SetTexture("_MaskMapArray", extraData.billboardMaskTexture);
            mat.SetVector("_BillboardSize", prop.billboardSize);
            mat.SetVector("_BillboardSizeOrigin", prop.billboardSizeOrigin);
            mat.SetVector("_BillboardOffset", prop.billboardOffset);
            mat.SetInt("_RECEIVE_SHADOWS_OFF", prop.billboardCastShadows ? 0 : 1);
            mat.SetInt("_Lock_Rotation_Y", prop.billboardRestrictRotationY ? 1 : 0);
        }

        Mesh mesh = meshed ? prop.instancedMesh : VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshIndirect(renderParams, mesh, drawArgsBuffer, 1, i);
    }

    // Called whenever we want to serialize the data for a prop and save it to memory/disk
    // Is called automatically when we unload the segment, but also when we serialize the terrain
    internal void SerializePropsOnSegmentUnload(int4 removed) {
        if (terrain.VoxelRegions.propSegmentsDict.TryGetValue(removed, out VoxelRegion segment)) {
            foreach (var collection in segment.props) {
                var propData = propTypeSerializedData[collection.Key];
                int stride = propData.stride;
                NativeList<byte> rawBytes = propData.rawBytes;
                NativeBitArray free = propData.set;

                // Create a writer that we will reuse
                FastBufferWriter writer = new FastBufferWriter(stride, Allocator.Temp);

                for (int i = 0; i < collection.Value.Item1.Count; i++) {
                    GameObject prop = collection.Value.Item1[i];
                    ushort dispatchIndex = collection.Value.Item2[i];
                    int index = VoxelUtils.FetchPropBitmaskIndex(collection.Key, dispatchIndex);
                    int4 indexer = new int4(segment.regionPosition, index);

                    // Set the prop as "destroyed"
                    if (prop == null) {
                        // Initialize this segment as a "modified" segment that will read from the prop ignore bitmask
                        if (!ignorePropsBitmasks.ContainsKey(segment.regionPosition)) {
                            ignorePropsBitmasks.Add(segment.regionPosition, new NativeBitArray(ignorePropBitmaskBuffer.count * 32, Allocator.Persistent));
                        }

                        // Write the affected bit to the buffer to tell the compute shader
                        // to no longer spawn this prop
                        NativeBitArray bitmask = ignorePropsBitmasks[segment.regionPosition];
                        bitmask.Set(index, true);

                        // Set the bit back to "free" since we're deleting the prop
                        if (globalBitmaskIndexToLookup.TryGetValue(index, out int lookup)) {
                            free.Set(lookup, false);
                        }

                        globalBitmaskIndexToLookup.Remove(indexer);
                    } else {
                        // Check if the prop was "modified"
                        var serializableProp = prop.GetComponent<SerializableProp>();

                        if (serializableProp.wasModified) {
                            // If we don't have an index, find a free one using the bitmask
                            if (serializableProp.ElementIndex == -1) {
                                serializableProp.ElementIndex = free.Find(0, 1);
                                free.Set(serializableProp.ElementIndex, true);
                            }

                            // Write the prop data
                            writer.WriteNetworkSerializable(serializableProp);

                            // Either copy the memory (update) or add it
                            int currentElementCount = rawBytes.Length / stride;
                            int currentByteOffset = serializableProp.ElementIndex * stride;

                            // Unsafe needed for raw mem cpy
                            unsafe {
                                if (serializableProp.ElementIndex >= currentElementCount) {
                                    rawBytes.AddRange(writer.GetUnsafePtr(), stride);
                                } else {
                                    UnsafeUtility.MemCpy((byte*)writer.GetUnsafePtr(), (byte*)rawBytes.GetUnsafePtr() + currentByteOffset, stride);
                                }
                            }

                            writer.Seek(0);
                            globalBitmaskIndexToLookup.TryAdd(indexer, serializableProp.ElementIndex);
                        }

                        serializableProp.wasModified = false;
                    }
                }

                writer.Dispose();
            }
        }
    }

    // Called by VoxelRegion when we should remove the regions (and thus hide their billboarded props) on the GPU
    private void RemoveRegionsGpu(ref NativeList<int4> removedSegments) {
        int[] indices = Enumerable.Repeat(-1, VoxelUtils.MaxSegmentsToRemove).ToArray();
        for (int i = 0; i < removedSegments.Length; i++) {
            var pos = removedSegments[i];
            if (terrain.VoxelRegions.propSegmentsDict.TryGetValue(pos, out VoxelRegion val)) {
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

    // Cause the voxel prop modifiers to re-sort and recompute their compute buffer data
    internal void ResortSpawnModifiers() {
        if (modifiersBuffer.count < modifiersHashSet.Count) {
            modifiersBuffer.Dispose();
            modifiersBuffer = null;
            modifiersBuffer = new ComputeBuffer(modifiersHashSet.Count, VoxelPropsSpawnModifier.BlittableSpawnModifier.size);
        }

        // Create the new buffer with the new size
        var array = modifiersHashSet.Select(x => x.ConvertToBlittable()).ToList();
        array.Sort((a, b) => a.priority.CompareTo(b.priority));
        modifiersBuffer.SetData(array, 0, 0, modifiersHashSet.Count);

        // TODO: Set the property of the prop gen comp shader to this buffer
    }

    // Destroy all props in a specific position around a radius
    // Needed since we cannot access the generated props on the GPU directly
    // Only use this for effects as there is no way of knowing exactly how many props where affected
    public void DestroyInRadius(Vector3 position, float radius) {
        throw new NotImplementedException();
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

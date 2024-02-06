using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;
using static UnityEngine.Rendering.HableCurve;
using static VoxelEdits;
using Unity.Netcode;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.UIElements;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    
    // Toggles for debugging
    public bool debugGizmos = false;

    // Prop resolution per segment
    [Range(4, 32)]
    public int propSegmentResolution = 32;

    // How many voxel chunks fit in a prop segment
    [Range(1, 64)]
    public int voxelChunksInPropSegment = 8;

    // Max number of active segments possible at any given time
    [Min(128)]
    public int maxSegments = 512;

    // Max number of segments that we can unload / remove
    [Min(128)]
    public int maxSegmentsToRemove = 512;

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

    // Prop GPU copy, block search, and bitmask removal (basically a GPU allocator at this point lol)
    [Header("Prop GPU allocator and culler")]
    public ComputeShader propFreeBlockSearch;
    public ComputeShader propFreeBlockCopy;
    public ComputeShader propCullingCopy;
    public ComputeShader propCullingApply;
    public ComputeShader removePropSegments;

    // Buffer containing the temp offset, perm offset, and culled offset for all prop types
    private ComputeBuffer propSectionOffsetsBuffer;
    private int3[] propSectionOffsets;

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

    // Prop segment management and diffing
    private NativeHashSet<int4> propSegments;
    private NativeHashSet<int4> oldPropSegments;
    private NativeList<int4> addedSegments;
    private NativeList<int4> removedSegments;

    // Prop segment classes bounded to their positions
    internal Dictionary<int4, PropSegment> propSegmentsDict;

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
    
    // When we load in a prop segment
    public delegate void PropSegmentLoaded(PropSegment segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // When we unload a prop segment
    public delegate void PropSegmentUnloaded(PropSegment segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Pooled props and prop owners
    // TODO: Optimize
    private List<List<GameObject>>[] pooledPropGameObjects;
    private GameObject propOwner;

    // Pending prop segment to generated and to be deleted
    internal Queue<PropSegment> pendingSegments;
    internal bool segmentsAwaitingRemoval;

    // Used for collision and GPU based raycasting (supports up to 4 intersections within a ray)
    RenderTexture propSegmentDensityVoxels;

    // 3 R textures (4 bytes) where each byte represents density voxel near the surface
    RenderTexture broadPhaseIntersectingTexture;

    // 3 R textures (FLOAT4) that contain the position data for the respective intersection
    RenderTexture positionIntersectingTexture;

    private bool mustUpdate = false;
    private bool lastSegmentWasModified = false;
    int asyncRequestsInProcess = 0;

    // Checks if we completed prop generation
    public bool Free {
        get {
            return !mustUpdate && pendingSegments.Count == 0 && asyncRequestsInProcess == 0;
        }
    }

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
        propShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale * VoxelUtils.VoxelSizeFactor);
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
        voxelShader.SetTexture(1, "broadPhaseIntersections", broadPhaseIntersectingTexture);
        voxelShader.SetTexture(2, "broadPhaseIntersections", broadPhaseIntersectingTexture);
        voxelShader.SetTexture(2, "positionIntersections", positionIntersectingTexture);
        propShader.SetTexture(0, "_PositionIntersections", positionIntersectingTexture);
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        propSegmentResolution = Mathf.ClosestPowerOfTwo(propSegmentResolution);
        voxelChunksInPropSegment = Mathf.ClosestPowerOfTwo(voxelChunksInPropSegment);
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;

        // Pooling game object stuff
        segmentsAwaitingRemoval = false;
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

        // Other stuff (still related to prop gen and GPU alloc)
        propSectionOffsetsBuffer = new ComputeBuffer(props.Count, sizeof(int) * 3);
        propSectionOffsets = new int3[props.Count];
        segmentIndexCountBuffer = new ComputeBuffer(maxSegments * props.Count, sizeof(uint) * 2, ComputeBufferType.Structured);
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(propSegmentResolution, GraphicsFormat.R32_SFloat);
        unusedSegmentLookupIndices = new NativeBitArray(maxSegments, Allocator.Persistent);
        unusedSegmentLookupIndices.Clear();
        segmentsToRemoveBuffer = new ComputeBuffer(maxSegmentsToRemove, sizeof(int));

        // Stuff used for management and addition/removal detection of segments
        propSegmentsDict = new Dictionary<int4, PropSegment>();
        propSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        oldPropSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        addedSegments = new NativeList<int4>(Allocator.Persistent);
        removedSegments = new NativeList<int4>(Allocator.Persistent);
        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;
        pendingSegments = new Queue<PropSegment>();

        // For now we are going to assume we will spawn only 1 variant of a prop type per segment
        ignorePropsBitmasks = new NativeHashMap<int3, NativeBitArray>(0, Allocator.Persistent);
        ignorePropBitmaskBuffer = new ComputeBuffer((propSegmentResolution * propSegmentResolution * propSegmentResolution * props.Count) / 32, sizeof(uint));
        globalBitmaskIndexToLookup = new NativeHashMap<int4, int>(0, Allocator.Persistent);
        propTypeSerializedData = new NativeArray<PropTypeSerializedData>(props.Count, Allocator.Persistent);

        // Fetch the temp offset, perm offset, visible culled offset
        // Also spawns the object prop type owners and attaches them to the terrain
        int3 last = int3.zero;
        for (int i = 0; i < props.Count; i++) {
            pooledPropGameObjects[i] = new List<List<GameObject>>();
            PropType propType = props[i];
            for (int j = 0; j < propType.variants.Count; j++) {
                pooledPropGameObjects[i].Add(new List<GameObject>());
            }

            // We do a considerable amount of trolling
            propSectionOffsets[i] = last;
            var offset = new int3(
                propType.maxPropsPerSegment,
                propType.maxPropsInTotal,
                propType.maxVisibleProps
            );
            last += offset;

            // Make sure all the variants have the same stride
            SerializableProp first = propType.variants[0].prefab.GetComponent<SerializableProp>();
            Type type = first.GetType();
            if (propType.variants.Any(x => x.prefab.GetComponent<SerializableProp>().GetType() != type)) {
                Debug.LogError("Variants MUST have the same SerializableProp script!");
            }

            // Initialize the serialized prop data buffers
            propTypeSerializedData[i] = new PropTypeSerializedData {
                rawBytes = new NativeList<byte>(Allocator.Persistent),
                set = new NativeBitArray(offset.x, Allocator.Persistent),
                stride = first.Stride,
            };
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
        broadPhaseIntersectingTexture = CreateRayCastTexture(propSegmentResolution, GraphicsFormat.R32_UInt);
        positionIntersectingTexture = CreateRayCastTexture(propSegmentResolution, GraphicsFormat.R16G16B16A16_SFloat);

        // Update static settings and capture the billboards
        propSectionOffsetsBuffer.SetData(propSectionOffsets);
        UpdateStaticComputeFields();
        CapturePropsBillboards();
    }

    // Capture the billboards of all props sequentially
    private void CapturePropsBillboards() {
        // Create a prop capture camera to 
        extraPropData = new List<IndirectExtraPropData>();
        GameObject captureGo = Instantiate(propCaptureCameraPrefab);
        Camera cam = captureGo.GetComponent<Camera>();
        captureGo.layer = 31;
        cam.cullingMask = 1 << 31;

        // Capture all props (including variant types)
        foreach (var prop in props) {
            extraPropData.Add(CaptureBillboard(cam, prop));
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
        albedoTextureOut.filterMode = prop.billboardTextureFilterMode;
        normalTextureOut.filterMode = prop.billboardTextureFilterMode;

        for (int i = 0; i < prop.variants.Count; i++) {
            PropType.PropVariantType variant = prop.variants[i];
            camera.orthographicSize = variant.billboardCaptureCameraScale;

            GameObject faker = Instantiate(variant.prefab);
            faker.GetComponent<SerializableProp>().OnSpawnCaptureFake();
            faker.layer = 31;
            foreach (Transform item in faker.transform) {
                item.gameObject.layer = 31;
            }

            // Move the prop to the appropriate position
            faker.transform.position = variant.billboardCapturePosition;
            faker.transform.eulerAngles = variant.billboardCaptureRotation;

            // Render the albedo map only of the prefab
            propCaptureFullscreenMaterial.SetInteger("_RenderAlbedo", 1);
            camera.Render();
            Graphics.CopyTexture(temp, 0, albedoTextureOut, i);

            // Render the normal map only of the prefab
            propCaptureFullscreenMaterial.SetInteger("_RenderAlbedo", 0);
            camera.Render();
            Graphics.CopyTexture(temp, 0, normalTextureOut, i);

            faker.GetComponent<SerializableProp>().OnDestroyCaptureFake();
            DestroyImmediate(faker);
            temp.DiscardContents(true, true);
            temp.Release();
        }

        return new IndirectExtraPropData {
            billboardAlbedoTexture = albedoTextureOut,
            billboardNormalTexture = normalTextureOut,
        };
    }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(PropSegment segment) {
        segment.props = new Dictionary<int, (List<GameObject>, List<ushort>)>();

        // Quit early if we shouldn't do shit
        if (!props.Any(x => x.WillRenderBillboard | (x.WillSpawnPrefab && segment.spawnPrefabs))) {
            return;
        }

        // Set the prop ignore bitmask buffer if needed
        if (ignorePropsBitmasks.ContainsKey(segment.segmentPosition)) {
            var bitmask = ignorePropsBitmasks[segment.segmentPosition];
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

        // Execute the ray casting shader that will store the position of the rays
        voxelShader.Dispatch(2, _count, _count, 3);

        // Set compute properties and run the compute shader
        tempCountBuffer.SetData(new int[props.Count]);
        propShader.SetVector("propChunkOffset", segment.worldPosition);
        propShader.Dispatch(0, _count, _count, _count);

        // Create an async callback if we have ANY prop that must be spawned as a game object
        if (props.Any(x => x.WillSpawnPrefab) && segment.spawnPrefabs) {
            for (int i = 0; i < props.Count; i++) {
                if (props[i].WillSpawnPrefab) {
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
                        int offset = propSectionOffsets[i].x;

                        if (!propType.WillSpawnPrefab)
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
                            if (globalBitmaskIndexToLookup.TryGetValue(new int4(segment.segmentPosition, index), out int elementLookup)) {
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
            bool billboardRender = props[i].WillRenderBillboard;
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
    private void OnPropSegmentUnload(PropSegment segment) {
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

    // Updates the prop segments LOD and renders instanced/billboarded instances for props
    private void Update() {
        mustUpdate |= terrain.VoxelOctree.mustUpdate;

        // Only update if we can and if we finished generating
        if (mustUpdate && pendingSegments.Count == 0 && !segmentsAwaitingRemoval) {
            NativeList<TerrainLoaderTarget> targets = terrain.VoxelOctree.targets;

            PropSegmentSpawnDiffJob job = new PropSegmentSpawnDiffJob {
                addedSegments = addedSegments,
                removedSegments = removedSegments,
                oldPropSegments = oldPropSegments,
                propSegments = propSegments,
                target = targets[0],
                maxSegmentsInWorld = VoxelUtils.PropSegmentsCount / 2,
                propSegmentSize = VoxelUtils.PropSegmentSize,
            };

            job.Schedule().Complete();

            for (int i = 0; i < addedSegments.Length; i++) {
                var segment = new PropSegment();
                var pos = addedSegments[i];
                segment.worldPosition = new Vector3(pos.x, pos.y, pos.z) * VoxelUtils.PropSegmentSize;
                segment.segmentPosition = pos.xyz;
                segment.spawnPrefabs = pos.w == 0;
                segment.indexRangeLookup = -1;
                propSegmentsDict.Add(pos, segment);
                pendingSegments.Enqueue(segment);
            }

            mustUpdate = false;
            segmentsAwaitingRemoval = removedSegments.Length > 0;

            foreach (var removed in removedSegments) {
                SerializePropsOnSegmentUnload(removed);
            }
        }

        // When we finished generating all pending segments delete the ones that are pending removal
        if (pendingSegments.Count == 0 && segmentsAwaitingRemoval && asyncRequestsInProcess == 0) {
            segmentsAwaitingRemoval = false;
            if (removedSegments.Length == 0) {
                return;
            }

            int[] indices = Enumerable.Repeat(-1, maxSegmentsToRemove).ToArray();
            for (int i = 0; i < removedSegments.Length; i++) {
                var pos = removedSegments[i];
                if (propSegmentsDict.Remove(pos, out PropSegment val)) {
                    indices[i] = val.indexRangeLookup;
                    onPropSegmentUnloaded.Invoke(val);
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
            removePropSegments.Dispatch(0, Mathf.CeilToInt((float)removedSegments.Length / 32.0f), 1, 1);
        }

        // Start generating the first pending segment we find
        PropSegment result;
        if (pendingSegments.TryPeek(out result)) {
            if (asyncRequestsInProcess == 0) {
                pendingSegments.Dequeue();
                onPropSegmentLoaded.Invoke(result);
            }
        }

        // Fetch camera from the terrain loader to use for prop billboard culling
        Camera camera = null;
        if (terrain.VoxelOctree.targetsLookup.Count > 0) {
            camera = terrain.VoxelOctree.targetsLookup.First().Key.viewCamera;
        }
        if (camera == null)
            return;

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
        propCullingCopy.Dispatch(0, count, props.Count, 1);

        // Apply culling counts to the indirect draw args
        propCullingApply.SetBuffer(0, "culledCount", culledCountBuffer);
        propCullingApply.SetBuffer(0, "drawArgs", drawArgsBuffer);
        propCullingApply.SetInt("propCount", props.Count);
        propCullingApply.Dispatch(0, 1, 1, 1);

        // Render all billboarded/instanced prop types using a single command per type
        for (int i = 0; i < props.Count; i++) {
            IndirectExtraPropData extraData = extraPropData[i];
            PropType prop = props[i];
            RenderBillboardsOfType(i, extraData, prop);
        }
    }

    // Called whenever we want to serialize the data for a prop and save it to memory/disk
    // Is called automatically when we unload the segment, but also when we serialize the terrain
    internal void SerializePropsOnSegmentUnload(int4 removed) {
        if (propSegmentsDict.TryGetValue(removed, out PropSegment segment)) {
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
                    int4 indexer = new int4(segment.segmentPosition, index);

                    // Set the prop as "destroyed"
                    if (prop == null) {
                        // Initialize this segment as a "modified" segment that will read from the prop ignore bitmask
                        if (!ignorePropsBitmasks.ContainsKey(segment.segmentPosition)) {
                            ignorePropsBitmasks.Add(segment.segmentPosition, new NativeBitArray(ignorePropBitmaskBuffer.count * 32, Allocator.Persistent));
                        }

                        // Write the affected bit to the buffer to tell the compute shader
                        // to no longer spawn this prop
                        NativeBitArray bitmask = ignorePropsBitmasks[segment.segmentPosition];
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

    // Clear out all the props and regenerate them
    // This will reset all buffers, bitmasks, EVERYTHING
    public void RegenerateProps() {
        if (mustUpdate)
            return;

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
        segmentIndexCountBuffer.SetData(new uint[maxSegments * props.Count * 2]);
        unusedSegmentLookupIndices.Clear();
        
        // WARNING: This causes GC.Collect spikes since we set a bunch of stuff null so it collects them automatically
        // what we should do instead is only regenerate the chunks that have been modified instead
        foreach (var item in propSegmentsDict) {
            onPropSegmentUnloaded?.Invoke(item.Value);
            pendingSegments.Enqueue(item.Value);
        }
    }

    // Render the billboards for a specific type of prop type
    private void RenderBillboardsOfType(int i, IndirectExtraPropData extraData, PropType prop) {
        ShadowCastingMode shadowCastingMode = prop.billboardCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        RenderParams renderParams = new RenderParams(billboardMaterialBase);
        renderParams.shadowCastingMode = shadowCastingMode;
        renderParams.worldBounds = new Bounds {
            min = -Vector3.one * VoxelUtils.PropSegmentSize * 100000,
            max = Vector3.one * VoxelUtils.PropSegmentSize * 100000,
        };

        var mat = new MaterialPropertyBlock();
        renderParams.matProps = mat;
        mat.SetFloat("_PropType", (float)i);
        mat.SetBuffer("_PropSectionOffsets", propSectionOffsetsBuffer);
        mat.SetBuffer("_BlittablePropBuffer", culledPropBuffer);
        mat.SetTexture("_AlbedoArray", extraData.billboardAlbedoTexture);
        mat.SetTexture("_NormalMapArray", extraData.billboardNormalTexture);
        mat.SetFloat("_Alpha_Clip_Threshold", 0.5f);
        mat.SetVector("_BillboardSize", prop.billboardSize);
        mat.SetVector("_BillboardOffset", prop.billboardOffset);
        mat.SetInt("_RECEIVE_SHADOWS_OFF", prop.billboardCastShadows ? 0 : 1);
        mat.SetInt("_Lock_Rotation_Y", prop.billboardRestrictRotationY ? 1 : 0);

        Mesh mesh = VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshIndirect(renderParams, mesh, drawArgsBuffer, 1, i);
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            foreach (var item in propSegmentsDict) {
                var key = item.Key.xyz;
                Vector3 center = new Vector3(key.x, key.y, key.z) * size + Vector3.one * size / 2.0f;

                if (item.Value.spawnPrefabs) {
                    Gizmos.color = Color.green;
                } else {
                    Gizmos.color = Color.red;
                }

                Gizmos.DrawWireCube(center, Vector3.one * size);
            }
        }
    }

    internal override void Dispose() {
        propSegments.Dispose();
        oldPropSegments.Dispose();
        addedSegments.Dispose();
        removedSegments.Dispose();
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
    }
}

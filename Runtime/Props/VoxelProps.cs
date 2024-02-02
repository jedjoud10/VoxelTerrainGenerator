using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;

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

    // List of props that we will generated based on their index
    [SerializeField]
    public List<Prop> props;

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

    private ComputeBuffer tempPropBuffer;
    private ComputeBuffer tempCountBuffer;
    private ComputeBuffer tempIndexBuffer;
    
    private ComputeBuffer permBitmaskBuffer;
    private ComputeBuffer permPropBuffer;
    
    private ComputeBuffer culledPropBuffer;
    private ComputeBuffer culledCountBuffer;
    private GraphicsBuffer drawArgsBuffer;

    private ComputeBuffer segmentsToRemoveBuffer;
    private ComputeBuffer segmentIndexCountBuffer;
    private NativeBitArray unusedSegmentLookupIndices;

    private int maxTestino;

    // Prop segment management and diffing
    private NativeHashSet<int4> propSegments;
    private NativeHashSet<int4> oldPropSegments;
    private NativeList<int4> addedSegments;
    private NativeList<int4> removedSegments;

    // Prop segment classes bounded to their positions
    private Dictionary<int4, PropSegment> propSegmentsDict;

    // When we load in a prop segment
    public delegate void PropSegmentLoaded(PropSegment segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // When we unload a prop segment
    public delegate void PropSegmentUnloaded(PropSegment segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Called when a prop prefab gets spawned in (might be pooled)
    public delegate void PropPrefabSpawned(Prop type, GameObject prop);
    public event PropPrefabSpawned onPropPrefabSpawned;

    // Called when a prop prefab gets pooled back (deactivated)
    public delegate void PropPrefabPooled(Prop type, GameObject prop);
    public event PropPrefabPooled onPropPrefabPooled;

    // Pooled props and prop owners
    private List<GameObject>[] pooledPropGameObjects;
    private GameObject[] propOwners;

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
    int bruhCounter = 0;

    // Checks if we completed prop generation
    public bool Free {
        get {
            return !mustUpdate && pendingSegments.Count == 0;
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

    // Clear out all the props and regenerate them
    public void RegenerateProps() {
        if (mustUpdate)
            return;

        foreach (var item in propSegmentsDict) {
            onPropSegmentUnloaded?.Invoke(item.Value);
            pendingSegments.Enqueue(item.Value);
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

        // Set generation/world settings
        voxelShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        voxelShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        propShader.SetVector("worldOffset", terrain.VoxelGenerator.worldOffset);
        propShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale * VoxelUtils.VoxelSizeFactor);
        propShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        propShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        propShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        propShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);

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
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;

        // Pooling game object stuff
        segmentsAwaitingRemoval = false;
        pooledPropGameObjects = new List<GameObject>[props.Count];
        propOwners = new GameObject[props.Count];

        // Temp buffers used for first step in prop generation
        int tempSum = props.Select(x => x.maxPropsPerSegment).Sum();
        tempCountBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        tempPropBuffer = new ComputeBuffer(tempSum, BlittableProp.size, ComputeBufferType.Structured);

        // Secondary buffers used for temp -> perm data copy
        tempIndexBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        int permSum = props.Select(x => x.maxPropsInTotal).Sum();
        int permMax = props.Select(x => x.maxPropsInTotal).Max();
        maxTestino = permMax;
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
        segmentIndexCountBuffer = new ComputeBuffer(4096 * props.Count, sizeof(uint) * 2, ComputeBufferType.Structured);
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(propSegmentResolution, GraphicsFormat.R32_SFloat);
        unusedSegmentLookupIndices = new NativeBitArray(4096, Allocator.Persistent);
        unusedSegmentLookupIndices.Clear();
        segmentsToRemoveBuffer = new ComputeBuffer(4096, sizeof(int));

        // Stuff used for management and addition/removal detection of segments
        propSegmentsDict = new Dictionary<int4, PropSegment>();
        propSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        oldPropSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        addedSegments = new NativeList<int4>(Allocator.Persistent);
        removedSegments = new NativeList<int4>(Allocator.Persistent);
        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;
        pendingSegments = new Queue<PropSegment>();

        // Fetch the temp offset, perm offset, visible culled offset
        // Also spawns the object prop type owners and attaches them to the terrain
        int3 last = int3.zero;
        for (int i = 0; i < props.Count; i++) {
            pooledPropGameObjects[i] = new List<GameObject>();
            var obj = new GameObject();
            obj.transform.SetParent(transform);
            propOwners[i] = obj;
            Prop propType = props[i];
            obj.name = $"'{propType.name}' Props GameObjects";

            // We do a considerable amount of trolling
            propSectionOffsets[i] = last;
            var offset = new int3(
                propType.maxPropsPerSegment,
                propType.maxPropsInTotal,
                propType.maxVisibleProps
            );
            last += offset;
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

        // Capture all props
        foreach (var prop in props) {
            extraPropData.Add(CaptureBillboard(cam, prop));
        }

        Destroy(captureGo);
    }

    // Capture the albedo, normal, and mask maps from billboarding a prop by spawning it temporarily
    public IndirectExtraPropData CaptureBillboard(Camera camera, Prop prop) {
        int width = prop.billboardTextureWidth;
        int height = prop.billboardTextureHeight;
        var temp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        camera.orthographicSize = prop.billboardCaptureCameraScale;
        camera.targetTexture = temp;
        
        RenderTexture rt = RenderTexture.active;
        RenderTexture.active = temp;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = rt;

        // Create the output texture 2Ds
        Texture2D albedoTextureOut = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Texture2D normalTextureOut = new Texture2D(width, height, TextureFormat.ARGB32, false);
        albedoTextureOut.filterMode = prop.billboardTextureFilterMode;
        normalTextureOut.filterMode = prop.billboardTextureFilterMode;

        // Create a prop fake game object and render the camera
        GameObject faker = Instantiate(prop.prefab);
        faker.layer = 31;
        foreach (Transform item in faker.transform) {
            item.gameObject.layer = 31;
        }

        // Move the prop to the appropriate position
        faker.transform.position = prop.billboardCapturePosition;
        faker.transform.eulerAngles = prop.billboardCaptureRotation;

        // Render the albedo map only of the prefab
        propCaptureFullscreenMaterial.SetInteger("_RenderAlbedo", 1);
        camera.Render();
        Graphics.CopyTexture(temp, albedoTextureOut);

        // Render the normal map only of the prefab
        propCaptureFullscreenMaterial.SetInteger("_RenderAlbedo", 0);
        camera.Render();
        Graphics.CopyTexture(temp, normalTextureOut);

        DestroyImmediate(faker);
        temp.DiscardContents(true, true);
        temp.Release();
        camera.targetTexture = null;

        return new IndirectExtraPropData {
            billboardAlbedoTexture = albedoTextureOut,
            billboardNormalTexture = normalTextureOut,
        };
    }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(PropSegment segment) {
        segment.props = new Dictionary<int, List<GameObject>>();

        // Quit early if we shouldn't do shit
        if (!props.Any(x => x.WillRenderBillboard | (x.WillSpawnPrefab && segment.spawnPrefabs))) {
            return;
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
                    segment.props.Add(i, new List<GameObject>());
                }
            }

            bruhCounter++;

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

                    for (int i = 0; i < props.Count; i++) {
                        Prop propType = props[i];
                        int offset = propSectionOffsets[i].x;

                        if (!propType.WillSpawnPrefab)
                            continue;

                        for (int k = 0; k < count[i]; k++) {
                            var prop = data[k + offset];
                            GameObject propGameObject = FetchPooledProp(i, propType);
                            onPropPrefabSpawned?.Invoke(propType, propGameObject);
                            propGameObject.transform.SetParent(propOwners[i].transform);
                            float3 propPosition = prop.packed_position_and_scale.xyz;

                            float propScale = prop.packed_position_and_scale.w;
                            propGameObject.transform.position = propPosition;
                            propGameObject.transform.localScale = Vector3.one * propScale;
                            propGameObject.transform.eulerAngles = VoxelUtils.UncompressPropRotation(ref prop);
                            segment.props[i].Add(propGameObject);
                        }
                    }

                    bruhCounter--;
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
        segment.indexRangeLookup = unusedSegmentLookupIndices.Find(0, 1);
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
        propFreeBlockCopy.Dispatch(0, maxTestino / 32, props.Count, 1);
    }

    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(PropSegment segment) {
        foreach (var collection in segment.props) {
            foreach (var item in collection.Value) {
                item.SetActive(false);
                onPropPrefabPooled?.Invoke(props[collection.Key], item);
                pooledPropGameObjects[collection.Key].Add(item);
            }
        }

        if (segment.indexRangeLookup != -1) {
            unusedSegmentLookupIndices.Set(segment.indexRangeLookup, false);
        }
        segment.props = null;
    }

    // Fetches a pooled prop, or creates a new one from scratch
    GameObject FetchPooledProp(int i, Prop propType) {
        GameObject go;

        if (pooledPropGameObjects[i].Count == 0) {
            GameObject obj = Instantiate(propType.prefab);
            obj.transform.SetParent(transform, false);
            go = obj;
        } else {
            go = pooledPropGameObjects[i][0];
            pooledPropGameObjects[i].RemoveAt(0);
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
        }

        // When we finished generating all pending segments delete the ones that are pending removal
        if (pendingSegments.Count == 0 && segmentsAwaitingRemoval && bruhCounter == 0) {
            int[] arr = new int[4096];

            for (int i = 0; i < 4096; i++) {
                arr[i] = -1;
            }

            for (int i = 0; i < removedSegments.Length; i++) {
                var pos = removedSegments[i];
                if (propSegmentsDict.Remove(pos, out PropSegment val)) {
                    arr[i] = val.indexRangeLookup;
                    onPropSegmentUnloaded.Invoke(val);
                } else {
                    arr[i] = -1;
                }
            }

            // TODO: Test fluke? One time when tested (spaz) shit didn't work. DEBUG PLS
            if (removedSegments.Length > 0) {
                segmentsToRemoveBuffer.SetData(arr);
                removePropSegments.SetBuffer(0, "usedBitmask", permBitmaskBuffer);
                removePropSegments.SetBuffer(0, "segmentIndices", segmentsToRemoveBuffer);
                removePropSegments.SetBuffer(0, "segmentIndexCount", segmentIndexCountBuffer);
                removePropSegments.SetInt("propCount", props.Count);
                removePropSegments.Dispatch(0, Mathf.CeilToInt((float)removedSegments.Length / 32.0f), 1, 1);
            }

            segmentsAwaitingRemoval = false;
        }

        // Start generating the first pending segment we find
        PropSegment result;
        if (pendingSegments.TryPeek(out result)) {
            if (bruhCounter == 0) {
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

        int count = Mathf.CeilToInt((float)maxTestino / 32.0f);
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
            Prop prop = props[i];
            RenderBillboardsOfType(i, extraData, prop);
        }
    }

    // Render the billboards for a specific type of prop type
    private void RenderBillboardsOfType(int i, IndirectExtraPropData extraData, Prop prop) {
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
        mat.SetTexture("_Albedo", extraData.billboardAlbedoTexture);
        mat.SetTexture("_Normal_Map", extraData.billboardNormalTexture);
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
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Linq;
using static UnityEditor.MaterialProperty;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

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
    public ComputeShader generationShader;

    // Prop GPU copy, block search, and bitmask removal (basically a GPU allocator at this point lol)
    [Header("Prop GPU allocator and culler")]
    public ComputeShader propFreeBlockSearch;
    public ComputeShader propFreeBlockCopy;
    public ComputeShader propCullingCopy;
    public ComputeShader propCullingApply;
    public ComputeShader removePropSegments;

    // Unculled pos scale, culled pos scale, and indirect args

    private ComputeBuffer[] unculledPosScaleBuffers;
    private ComputeBuffer[] culledPosScaleBuffers;
    private ComputeBuffer[] usedBitmaskBuffers;
    private ComputeBuffer[] segmentIndexCountBuffer;
    private ComputeBuffer culledCountBuffer;
    private ComputeBuffer rawIndexBuffer;
    private GraphicsBuffer drawArgsBuffer;
    private ComputeBuffer segmentsToRemoveBuffer;

    // shit we will actually use
    private ComputeBuffer tempPropPosScaleBuffer;
    private ComputeBuffer rawCountBuffer;
    private ComputeBuffer propSectionOffsetsBuffer;
    private int[] propSectionOffsets;

    private BitField64 free = new BitField64(0);

    private NativeBitArray unusedBbSegments;

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

    // 3 R textures where each byte represents density voxel near the surface
    // 4 bytes => 4 possible intersections
    RenderTexture[] intersectingTextures;
    RenderTexture minAxiiPos;

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

        generationShader.SetVector("worldOffset", terrain.VoxelGenerator.worldOffset);
        generationShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale * VoxelUtils.VoxelSizeFactor);
        generationShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        generationShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        generationShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        generationShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);

        generationShader.SetBuffer(0, "tempProps", tempPropPosScaleBuffer);
        generationShader.SetBuffer(0, "tempCounters", rawCountBuffer);
        generationShader.SetBuffer(0, "propSectionOffsets", propSectionOffsetsBuffer);

        var voxelShader = terrain.VoxelGenerator.voxelShader;
        voxelShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        voxelShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        voxelShader.SetTexture(1, "cachedPropDensities", propSegmentDensityVoxels);
        voxelShader.SetTexture(2, "cachedPropDensities", propSegmentDensityVoxels);
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;

        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;

        int sizeBlittableProp = Marshal.SizeOf(new BlittableProp { });
        int sum = props.Select(x => x.maxPropsPerSegment).Sum();

        segmentsAwaitingRemoval = false;
        pooledPropGameObjects = new List<GameObject>[props.Count];
        propOwners = new GameObject[props.Count];
        
        unculledPosScaleBuffers = new ComputeBuffer[props.Count];
        culledPosScaleBuffers = new ComputeBuffer[props.Count];
        drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, props.Count, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        usedBitmaskBuffers = new ComputeBuffer[props.Count];

        segmentIndexCountBuffer = new ComputeBuffer[props.Count];
        culledCountBuffer = new ComputeBuffer(props.Count, sizeof(int));

        // shit we will actually use
        rawCountBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Structured);
        propSectionOffsetsBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Structured);
        tempPropPosScaleBuffer = new ComputeBuffer(sum, sizeBlittableProp, ComputeBufferType.Structured);

        pendingSegments = new Queue<PropSegment>();
        rawIndexBuffer = new ComputeBuffer(props.Count, sizeof(int), ComputeBufferType.Raw);
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(propSegmentResolution, GraphicsFormat.R32_SFloat);
        //minAxii = VoxelUtils.Create2DRenderTexture(propSegmentResolution, GraphicsFormat.R32_UInt);
        //minAxiiPos = VoxelUtils.Create2DRenderTexture(propSegmentResolution, GraphicsFormat.R16G16_SFloat);
        UpdateStaticComputeFields();

        propSectionOffsets = new int[props.Count];
        int cur = 0;
        for (int i = 0; i < props.Count; i++) {
            pooledPropGameObjects[i] = new List<GameObject>();
            var obj = new GameObject();
            obj.transform.SetParent(transform);
            propOwners[i] = obj;

            Prop propType = props[i];


            unculledPosScaleBuffers[i] = new ComputeBuffer(propType.maxPropsInTotal, sizeBlittableProp, ComputeBufferType.Structured);
            culledPosScaleBuffers[i] = new ComputeBuffer(propType.maxVisibleProps, sizeBlittableProp, ComputeBufferType.Structured);
            usedBitmaskBuffers[i] = new ComputeBuffer(Mathf.CeilToInt((float)propType.maxPropsInTotal / (32.0f)), sizeof(uint), ComputeBufferType.Structured);
            segmentIndexCountBuffer[i] = new ComputeBuffer(4096, sizeof(int) * 2);
            propSectionOffsets[i] = cur;
            cur += propType.maxPropsPerSegment;
        }

        propSectionOffsetsBuffer.SetData(propSectionOffsets);

        unusedBbSegments = new NativeBitArray(4096, Allocator.Persistent);
        unusedBbSegments.Clear();

        segmentsToRemoveBuffer = new ComputeBuffer(4096, sizeof(int));
        propSegmentsDict = new Dictionary<int4, PropSegment>();
        propSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        oldPropSegments = new NativeHashSet<int4>(0, Allocator.Persistent);
        addedSegments = new NativeList<int4>(Allocator.Persistent);
        removedSegments = new NativeList<int4>(Allocator.Persistent);

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
            (Texture2D albedo, Texture2D normal, Material mat) = CaptureBillboard(cam, prop);

            extraPropData.Add(new IndirectExtraPropData {
                billboardAlbedoTexture = albedo,
                billboardNormalTexture = normal,
                billboardMaterial = mat,
            });
        }

        Destroy(captureGo);
    }

    // Capture the albedo, normal, and mask maps from billboarding a prop by spawning it temporarily
    public (Texture2D, Texture2D, Material) CaptureBillboard(Camera camera, Prop prop) {
        int width = prop.billboardTextureWidth;
        int height = prop.billboardTextureHeight;
        var temp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        camera.orthographicSize = prop.billboardCaptureCameraScale;
        camera.targetTexture = temp;

        // Create the output texture 2Ds
        Texture2D albedoTextureOut = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Texture2D normalTextureOut = new Texture2D(width, height, TextureFormat.ARGB32, false);

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

        // Create a material used for billboard rendering
        // All the params will be set using the RenderParams struct
        Material mat = new Material(billboardMaterialBase);
        Destroy(faker);
        temp.Release();

        return (albedoTextureOut, normalTextureOut, mat);
    }

    // Custom class we use to send data to the delegate because it seems
    // as if sharing values by value doesn't really work out, wtv
    class Test {
        public int val = 0;
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
        //voxelShader.SetTexture(1, "minAxiiY", minAxii);
        voxelShader.Dispatch(1, _count, _count, _count);

        /*
        // Execute the ray casting shader that will store thickness and position of the rays
        voxelShader.SetTexture(2, "minAxiiY", minAxii);
        voxelShader.SetTexture(2, "minAxiiYTest", minAxiiPos);
        voxelShader.Dispatch(2, _count, _count, 1);
        */

        // Set compute properties and run the compute shader
        rawCountBuffer.SetData(new int[props.Count]);
        generationShader.SetVector("propChunkOffset", segment.worldPosition);
        generationShader.Dispatch(0, _count, _count, _count);

        // Create an async callback if we have ANY prop that must be spawned as a game object
        if (props.Any(x => x.WillSpawnPrefab) && segment.spawnPrefabs) {
            for (int i = 0; i < props.Count; i++) {
                if (props[i].WillSpawnPrefab) {
                    segment.props.Add(i, new List<GameObject>());
                }
            }

            bruhCounter++;

            // Spawn the prefabs when we get the data back asynchronously
            AsyncGPUReadback.Request(
                tempPropPosScaleBuffer,
                delegate (AsyncGPUReadbackRequest asyncRequest) {
                    if (segment.props == null)
                        return;

                    int[] count = new int[props.Count];
                    rawCountBuffer.GetData(count);

                    NativeArray<BlittableProp> data = asyncRequest.GetData<BlittableProp>();

                    for (int i = 0; i < props.Count; i++) {
                        Prop propType = props[i];
                        int offset = propSectionOffsets[i];

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

        // Copy the generated prop data to the perm data using a single compute dispatch
        /*
        rawIndexBuffer.SetData(new uint[] { uint.MaxValue });
        segment.indexRangeLookup = unusedBbSegments.Find(0, 1);
        unusedBbSegments.Set(segment.indexRangeLookup, true);

        // Find a free block that we can use
        propFreeBlockSearch.SetBuffer(0, "counter", rawCountBuffer);
        propFreeBlockSearch.SetBuffer(0, "index", rawIndexBuffer);
        propFreeBlockSearch.SetBuffer(0, "usedBitmask", usedBitmaskBuffers[i]);
        propFreeBlockSearch.Dispatch(0, Mathf.CeilToInt((float)propType.maxPropsInTotal / (32.0f*128.0f)), 1, 1);

        // Copy the temporary data to the permanent data that we will cull
        propFreeBlockCopy.SetInt("segmentLookup", segment.indexRangeLookup);
        propFreeBlockCopy.SetBuffer(0, "segmentIndexCount", segmentIndexCountBuffer[i]);
        propFreeBlockCopy.SetBuffer(0, "counter", rawCountBuffer);
        propFreeBlockCopy.SetBuffer(0, "index", rawIndexBuffer);
        propFreeBlockCopy.SetBuffer(0, "usedBitmask", usedBitmaskBuffers[i]);
        propFreeBlockCopy.SetBuffer(0, "tempProps", tempBuf);
        propFreeBlockCopy.SetBuffer(0, "permProps", unculledPosScaleBuffers[i]);
        propFreeBlockCopy.Dispatch(0, Mathf.CeilToInt((float)propType.maxPropsPerSegment / 32.0f), 1, 1);
        */
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
            unusedBbSegments.Set(segment.indexRangeLookup, false);
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
                removePropSegments.SetBuffer(0, "usedBitmask", usedBitmaskBuffers[0]);
                removePropSegments.SetBuffer(0, "segmentIndices", segmentsToRemoveBuffer);
                removePropSegments.SetBuffer(0, "segmentIndexCount", segmentIndexCountBuffer[0]);
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

        culledCountBuffer.SetData(new int[props.Count]);
        for (int i = 0; i < props.Count; i++) {
            Prop prop = props[i];
            ComputeBuffer unculled = unculledPosScaleBuffers[i];
            ComputeBuffer culled = culledPosScaleBuffers[i];

            propCullingCopy.SetBuffer(0, "usedBitmask", usedBitmaskBuffers[i]);
            propCullingCopy.SetBuffer(0, "unculledProps", unculled);
            propCullingCopy.SetBuffer(0, "culledProps", culled);
            propCullingCopy.SetBuffer(0, "culledCount", culledCountBuffer);
            propCullingCopy.SetVector("cameraForward", camera.transform.forward);
            propCullingCopy.SetVector("cameraPosition", camera.transform.position);
            propCullingCopy.Dispatch(0, Mathf.CeilToInt((float)prop.maxVisibleProps / (32.0f)), 1, 1);
        }

        // Apply culling counts to the indirect draw args
        propCullingApply.SetBuffer(0, "culledCount", culledCountBuffer);
        propCullingApply.SetBuffer(0, "drawArgs", drawArgsBuffer);
        propCullingApply.SetInt("maxPropTypes", props.Count);
        propCullingApply.Dispatch(0, Mathf.CeilToInt((float)props.Count / 32.0f), 1, 1);

        // Render all billboarded/instanced prop types using a single command per type
        for (int i = 0; i < props.Count; i++) {
            IndirectExtraPropData extraData = extraPropData[i];
            Prop prop = props[i];
            ComputeBuffer culled = culledPosScaleBuffers[i];
            RenderBillboardsOfType(i, extraData, prop, culled);
        }
    }

    // Render the billboards for a specific type of prop type
    private void RenderBillboardsOfType(int i, IndirectExtraPropData extraData, Prop prop, ComputeBuffer posScale) {
        ShadowCastingMode shadowCastingMode = prop.billboardCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        RenderParams renderParams = new RenderParams(extraData.billboardMaterial);
        renderParams.shadowCastingMode = shadowCastingMode;
        renderParams.worldBounds = new Bounds {
            min = -Vector3.one * VoxelUtils.PropSegmentSize * 100000,
            max = Vector3.one * VoxelUtils.PropSegmentSize * 100000,
        };

        var mat = new MaterialPropertyBlock();
        renderParams.matProps = mat;
        mat.SetBuffer("_BlittablePropBuffer", posScale);
        mat.SetVector("_BoundsOffset", renderParams.worldBounds.center);
        mat.SetTexture("_Albedo", extraData.billboardAlbedoTexture);
        mat.SetTexture("_Normal_Map", extraData.billboardNormalTexture);
        mat.SetFloat("_Alpha_Clip_Threshold", prop.billboardAlphaClipThreshold);
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
        rawCountBuffer.Dispose();
        rawIndexBuffer.Dispose();
        for (int i = 0; i < props.Count; i++) {
            unculledPosScaleBuffers[i].Dispose();
            culledPosScaleBuffers[i].Dispose();
            usedBitmaskBuffers[i].Dispose();
        }
        tempPropPosScaleBuffer.Dispose();
        propSectionOffsetsBuffer.Dispose();
        unusedBbSegments.Dispose();
    }
}

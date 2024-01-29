using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    
    // Toggles for debugging
    public bool debugGizmos = false;

    [Min(0)]
    public int maxComputeBufferSize = 1;

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
    public GameObject propCaptureCameraPrefab;
    public Material propCaptureFullscreenMaterial;
    public Mesh quadBillboard;
    public Material billboardMaterialBase;

    // Extra prop data that is shared with the prop segments
    private List<IndirectExtraPropData> extraPropData;

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

    // Pooled props that we can reuse for each prop type
    private List<GameObject>[] pooledPropGameObjects;

    // Create a game object attached to the main terrain that will store pooled props
    private GameObject[] pooledPropOwners;

    // Unculled compute buffers for position/scale for each prop type
    private ComputeBuffer[] posScaleBuffers;

    // Texture3D that will be used to store intermediate voxel types for a whole prop segment
    private RenderTexture propSegmentDensityVoxels;

    // Pending prop segment to generated
    internal Queue<PropSegment> pendingSegments;

    private bool mustUpdate = false;

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

        foreach (var propType in props) {
            ComputeShader compShader = propType.generationShader;
            compShader.SetVector("worldOffset", terrain.VoxelGenerator.worldOffset);
            compShader.SetVector("worldScale", terrain.VoxelGenerator.worldScale * VoxelUtils.VoxelSizeFactor);
            compShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
            compShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
            compShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
            compShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        }

        terrain.VoxelGenerator.voxelShader.SetFloat("propSegmentWorldSize", VoxelUtils.PropSegmentSize);
        terrain.VoxelGenerator.voxelShader.SetFloat("propSegmentResolution", VoxelUtils.PropSegmentResolution);
        terrain.VoxelGenerator.voxelShader.SetTexture(1, "cachedPropDensities", propSegmentDensityVoxels);
        terrain.VoxelGenerator.voxelShader.SetTexture(2, "cachedPropDensities", propSegmentDensityVoxels);
    }

    // Create captures of the props, and register main settings
    internal override void Init() {
        propSegmentDensityVoxels = VoxelUtils.Create3DRenderTexture(propSegmentResolution, GraphicsFormat.R32_SFloat);
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        UpdateStaticComputeFields();
        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;
        pooledPropGameObjects = new List<GameObject>[props.Count];
        pooledPropOwners = new GameObject[props.Count];
        pendingSegments = new Queue<PropSegment>();

        for (int i = 0; i < props.Count; i++) {
            pooledPropGameObjects[i] = new List<GameObject>();
            var obj = new GameObject();
            obj.transform.SetParent(transform);
            pooledPropOwners[i] = obj;
        }

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

    class Test { public int val = 0; }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(PropSegment segment) {
        segment.test = new Dictionary<int, (ComputeBuffer, int)>();
        segment.props = new Dictionary<int, List<GameObject>>();

        // Call the compute shader for each prop type
        for (int i = 0; i < props.Count; i++) {
            Prop propType = props[i];

            var minAxii = VoxelUtils.Create2DRenderTexture(propSegmentResolution, GraphicsFormat.R32_UInt);
            var minAxiiPos = VoxelUtils.Create2DRenderTexture(propSegmentResolution, GraphicsFormat.R16G16_SFloat);

            // Execute the prop segment voxel cache compute shader
            int _count = VoxelUtils.PropSegmentResolution / 4;
            var voxelShader = terrain.VoxelGenerator.voxelShader;
            voxelShader.SetVector("propChunkOffset", segment.worldPosition);
            voxelShader.SetTexture(1, "minAxiiY", minAxii);
            voxelShader.Dispatch(1, _count, _count, _count);

            // Execute the ray casting shader that will store thickness and position of the rays
            voxelShader.SetTexture(2, "minAxiiY", minAxii);
            voxelShader.SetTexture(2, "minAxiiYTest", minAxiiPos);
            voxelShader.Dispatch(2, _count, _count, 1);

            var propsBuffer = new ComputeBuffer(maxComputeBufferSize, Marshal.SizeOf(new BlittableProp()), ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);

            // Set compute properties and run the compute shader
            propsBuffer.SetCounterValue(0);
            countBuffer.SetData(new int[] { 0 });
            propType.generationShader.SetBuffer(0, "props", propsBuffer);
            propType.generationShader.SetVector("propChunkOffset", segment.worldPosition);
            propType.generationShader.SetTexture(0, "_Voxels", propSegmentDensityVoxels);
            propType.generationShader.SetTexture(0, "_MinAxii", minAxiiPos);
            propType.generationShader.Dispatch(0, _count, _count, _count);

            // Set this as a generated prop
            segment.generatedProps.SetBits(i, true);
            ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);

            // Either start waiting for the buffer asynchronously or simply set it to render using GPU instanced indirect
            if (segment.spawnPrefabs && propType.propSpawnBehavior.HasFlag(PropSpawnBehavior.SpawnPrefabs)) {
                segment.props.Add(i, new List<GameObject>());
                var test123 = new Test { val = i };

                AsyncGPUReadback.Request(
                    propsBuffer,
                    delegate (AsyncGPUReadbackRequest asyncRequest) {
                        SpawnPropPrefabs(test123.val, segment, propType, asyncRequest.GetData<BlittableProp>(), countBuffer);
                        //Debug.Log(asyncRequest.GetData());
                    }
                );

                //
            } else if (propType.propSpawnBehavior.HasFlag(PropSpawnBehavior.RenderBillboards)) {
                int[] count = new int[1] { 0 };
                countBuffer.GetData(count);
                segment.test.Add(i, (propsBuffer, count[0]));
            }
        }
    }

    // First runs the compute shader on a temporary buffer (max sized)
    // Then copies the buffer into a bigger one (max sized * number of currently active segments)
    // Main "segment" culling shader does frustum culling over prop segment regions first (broad phase)
    // Secondary culling shader does frustum culling over props individually (maybe occlusion?)
    //      Optionally do all billboard vertex logic here as well
    //      Optionally select between "instanced" and "billboarded" meshes to render
    
    // Called when an old prop segment is unloaded
    private void OnPropSegmentUnload(PropSegment segment) {
        foreach (var collection in segment.props) {
            foreach (var item in collection.Value) {
                item.SetActive(false);
                onPropPrefabPooled?.Invoke(props[collection.Key], item);
                pooledPropGameObjects[collection.Key].Add(item);
            }
        }

        segment.test = null;
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

    // Spawn the necessary prop gameobjects for a prop segment at lod 0
    private void SpawnPropPrefabs(int i, PropSegment segment, Prop propType, NativeArray<BlittableProp> data, ComputeBuffer countBuffer) {
        int[] count = new int[1] { 0 };
        countBuffer.GetData(count);

        for (int k = 0; k < count[0]; k++) {
            var prop = data[k];
            GameObject propGameObject = FetchPooledProp(i, propType);
            onPropPrefabSpawned?.Invoke(propType, propGameObject);
            propGameObject.transform.SetParent(pooledPropOwners[i].transform);
            float3 propPosition = prop.packed_position_and_scale.xyz;
            float3 propRotation = prop.packed_euler_angles_padding.xyz;
            float propScale = prop.packed_position_and_scale.w;
            propGameObject.transform.position = propPosition;
            propGameObject.transform.localScale = Vector3.one * propScale;
            propGameObject.transform.eulerAngles = propRotation;
            segment.props[i].Add(propGameObject);
        }
    }

    // Updates the prop segments LOD and renders instanced/billboarded instances for props
    private void Update() {
        mustUpdate |= terrain.VoxelOctree.mustUpdate;

        if (mustUpdate && pendingSegments.Count == 0) {
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
                segment.generatedProps.Clear();
                propSegmentsDict.Add(pos, segment);
                pendingSegments.Enqueue(segment);
            }

            for (int i = 0; i < removedSegments.Length; i++) {
                var pos = removedSegments[i];
                if (propSegmentsDict.Remove(pos, out PropSegment val)) {
                    onPropSegmentUnloaded.Invoke(val);
                }
            }

            mustUpdate = false;
        }

        PropSegment result;
        if (pendingSegments.TryDequeue(out result)) {
            onPropSegmentLoaded.Invoke(result);
        }
        
        foreach (var segment in propSegmentsDict) {
            if (segment.Value.test == null)
                continue;

            foreach (var item in segment.Value.test) {
                IndirectExtraPropData extraData = extraPropData[item.Key];
                Prop prop = props[item.Key];
                
                if (prop.propSpawnBehavior.HasFlag(PropSpawnBehavior.RenderBillboards)) 
                    RenderBillboardsForSegment(segment.Value.worldPosition, extraData, prop, item.Value.Item1, item.Value.Item2);
            }
        }
    }

    // Render the billboards for a specific prop segment
    private void RenderBillboardsForSegment(Vector3 position, IndirectExtraPropData extraData, Prop prop, ComputeBuffer buffer, int count) {
        if (count == 0)
            return;

        ShadowCastingMode shadowCastingMode = prop.billboardCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        RenderParams renderParams = new RenderParams(extraData.billboardMaterial);
        renderParams.shadowCastingMode = shadowCastingMode; 
        renderParams.worldBounds = new Bounds {
            min = position,
            max = position + Vector3.one * VoxelUtils.PropSegmentSize,
        };

        var mat = new MaterialPropertyBlock();
        renderParams.matProps = mat;
        mat.SetBuffer("_BlittablePropBuffer", buffer);
        mat.SetVector("_BoundsOffset", renderParams.worldBounds.center);
        mat.SetTexture("_Albedo", extraData.billboardAlbedoTexture);
        mat.SetTexture("_Normal_Map", extraData.billboardNormalTexture);
        mat.SetFloat("_Alpha_Clip_Threshold", prop.billboardAlphaClipThreshold);
        mat.SetVector("_BillboardSize", prop.billboardSize);
        mat.SetVector("_BillboardOffset", prop.billboardOffset);
        mat.SetInt("_RECEIVE_SHADOWS_OFF", prop.billboardCastShadows ? 0 : 1);
        mat.SetInt("_Lock_Rotation_Y", prop.billboardRestrictRotationY ? 1 : 0);

        Mesh mesh = VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshPrimitives(renderParams, mesh, 0, count);
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
    }
}

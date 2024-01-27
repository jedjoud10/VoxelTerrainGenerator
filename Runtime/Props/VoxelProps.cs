using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Responsible for generating the voxel props on the terrain
// For this, we must force voxel generation to happen on the CPU so we can execute
// custom code when the voxel edit must generate on world / voxel edits
public class VoxelProps : VoxelBehaviour {
    
    // Toggles for debugging
    public bool debugGizmos = false;
    public bool renderInstancedMeshes = true;
    public bool renderBillboards = true;
    public bool spawnPropPrefabs = true;

    [Min(0)]
    public int maxComputeBufferSize = 1;

    // Prop resolution per segment
    [Range(4, 64)]
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

    // Compute shader that will be responsble for prop  generation
    public ComputeShader propShader;

    // Prop segment management and diffing
    private NativeHashSet<int4> propSegments;
    private NativeHashSet<int4> oldPropSegments;
    private NativeList<int4> addedSegments;
    private NativeList<int4> removedSegments;

    // Prop segment classes bounded to their positions
    private Dictionary<int4, PropSegment> propSegmentsDict;

    // When we load in a prop segment
    public delegate void PropSegmentLoaded(int3 position, PropSegment segment);
    public event PropSegmentLoaded onPropSegmentLoaded;

    // When we unload a prop segment
    public delegate void PropSegmentUnloaded(int3 position, PropSegment segment);
    public event PropSegmentUnloaded onPropSegmentUnloaded;

    // Pooled props that we can reuse for each prop type
    private List<GameObject>[] pooledPropGameObjects;

    // Create a game object attached to the main terrain that will store pooled props
    private GameObject[] pooledPropOwners;

    // Unculled compute buffers for position/scale for each prop type
    private ComputeBuffer[] posScaleBuffers;

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

    // Create captures of the props, and register main settings
    internal override void Init() {
        VoxelUtils.PropSegmentResolution = propSegmentResolution;
        VoxelUtils.ChunksPerPropSegment = voxelChunksInPropSegment;
        UpdateStaticComputeFields();
        onPropSegmentLoaded += OnPropSegmentLoad;
        onPropSegmentUnloaded += OnPropSegmentUnload;
        pooledPropGameObjects = new List<GameObject>[props.Count];
        pooledPropOwners = new GameObject[props.Count];

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

        // Create a material that uses these values by default
        Material mat = new Material(billboardMaterialBase);

        mat.SetTexture("_Albedo", temp);
        mat.SetTexture("_Normal_Map", normalTextureOut);
        mat.SetFloat("_Alpha_Clip_Threshold", prop.billboardAlphaClipThreshold);
        mat.SetVector("_BillboardSize", prop.billboardSize);
        mat.SetVector("_BillboardOffset", prop.billboardOffset);
        mat.SetInt("_RECEIVE_SHADOWS_OFF", prop.billboardCastShadows ? 0 : 1);
        mat.SetInt("_Lock_Rotation_Y", prop.billboardRestrictRotationY ? 1 : 0);
        Destroy(faker);

        return (albedoTextureOut, normalTextureOut, mat);
    }

    // Called when a new prop segment is loaded and should be generated
    private void OnPropSegmentLoad(int3 position, PropSegment segment) {
        segment.test = new Dictionary<int, (ComputeBuffer, int)>();
        segment.props = new Dictionary<int, List<GameObject>>();
        
        // Call the compute shader for each prop type
        for (int i = 0; i < props.Count; i++) {
            Prop propType = props[i];

            var propsBuffer = new ComputeBuffer(maxComputeBufferSize, Marshal.SizeOf(new BlittableProp()), ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);

            // Set compute properties and run the compute shader
            propsBuffer.SetCounterValue(0);
            countBuffer.SetData(new int[] { 0 });
            propShader.SetBuffer(0, "props", propsBuffer);
            propShader.SetVector("propChunkOffset", segment.position);

            // Execute the prop segment compute shader
            int _count = VoxelUtils.PropSegmentResolution / 4;
            propShader.Dispatch(0, _count, _count, _count);

            if (segment.lod == 0) {
                segment.props.Add(i, new List<GameObject>());
                SpawnPropPrefabs(i, segment, propType, propsBuffer, countBuffer);
            } else {
                ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);
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
    private void OnPropSegmentUnload(int3 position, PropSegment segment) {
        foreach (var collection in segment.props) {
            foreach (var item in collection.Value) {
                item.SetActive(false);
                pooledPropGameObjects[collection.Key].Add(item);
            }
        }

        segment.test = null;
        segment.props = null;
    }

    // Spawn the necessary prop gameobjects for a prop segment at lod 0
    private void SpawnPropPrefabs(int i, PropSegment segment, Prop propType, ComputeBuffer propsBuffer, ComputeBuffer countBuffer) {
        // Fetches a pooled prop, or creates a new one from scratch
        GameObject FetchPooledProp() {
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


        if (!spawnPropPrefabs)
            return;

        ComputeBuffer.CopyCount(propsBuffer, countBuffer, 0);
        int[] count = new int[1] { 0 };
        countBuffer.GetData(count);

        BlittableProp[] generatedProps = new BlittableProp[count[0]];
        propsBuffer.GetData(generatedProps);
        foreach (var prop in generatedProps) {
            GameObject propGameObject = FetchPooledProp();
            propGameObject.transform.SetParent(pooledPropOwners[i].transform);
            float3 propPosition = prop.position_and_scale.xyz;
            float3 propRotation = prop.euler_angles_padding.xyz;
            float propScale = prop.position_and_scale.w;
            propGameObject.transform.position = propPosition;
            propGameObject.transform.localScale = Vector3.one * propScale;
            propGameObject.transform.eulerAngles = propRotation;
            segment.props[i].Add(propGameObject);
        }
    }

    // Updates the prop segments LOD and renders instanced/billboarded instances for props
    private void Update() {
        if (terrain.VoxelOctree.mustUpdate) {
            NativeList<TerrainLoaderTarget> targets = terrain.VoxelOctree.targets;

            PropSegmentSpawnDiffJob job = new PropSegmentSpawnDiffJob {
                addedSegments = addedSegments,
                removedSegments = removedSegments,
                oldPropSegments = oldPropSegments,
                propSegments = propSegments,
                target = targets[0],
                propSegmentSize = VoxelUtils.PropSegmentSize,
            };

            job.Schedule().Complete();

            for (int i = 0; i < addedSegments.Length; i++) {
                var segment = new PropSegment();
                var pos = addedSegments[i];
                segment.position = new Vector3(pos.x, pos.y, pos.z) * VoxelUtils.PropSegmentSize;
                segment.lod = pos.w;
                propSegmentsDict.Add(pos, segment);
                onPropSegmentLoaded.Invoke(pos.xyz, segment);
            }

            for (int i = 0; i < removedSegments.Length; i++) {
                var pos = removedSegments[i];
                if (propSegmentsDict.Remove(pos, out PropSegment val)) {
                    onPropSegmentUnloaded.Invoke(pos.xyz, val);
                }
            }

        }

        // Render all prop types using a single command
        /*
        for (int i = 0; i < props.Count; i++) {
            IndirectExtraPropData extra = extraPropData[i];
            Prop prop = props[i];

        }
        */

        if (!renderBillboards)
            return;

        foreach (var segment in propSegmentsDict) {
            if (segment.Value.test == null)
                continue;

            foreach (var item in segment.Value.test) {
                IndirectExtraPropData extraData = extraPropData[item.Key];
                Prop prop = props[item.Key];
                RenderBillboardsForSegment(segment.Value.position, extraData, prop, item.Value.Item1, item.Value.Item2);
            }
        }
    }

    /*
    // Render the billboards for a specific type of prop for one frame
    private void RenderBillboardsForProp(IndirectExtraPropData extraData, Prop prop, GraphicsBuffer commandBuffer) {
        if (!renderBillboards)
            return;

        ShadowCastingMode shadowCastingMode = prop.billboardCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        RenderParams renderParams = new RenderParams(extraData.billboardMaterial);
        renderParams.shadowCastingMode = shadowCastingMode;
        renderParams.worldBounds = new Bounds {
            center = Vector3.zero,
            extents = Vector3.one * 100000.0f
        };

        renderParams.matProps = new MaterialPropertyBlock();
        //renderParams.matProps.SetBuffer("_BlittablePropBuffer", prop.Item2);
        renderParams.matProps.SetVector("_BoundsOffset", renderParams.worldBounds.center);
        renderParams.matProps.SetTexture("_Albedo", extraData.billboardAlbedoTexture);
        renderParams.matProps.SetTexture("_Normal_Map", extraData.billboardNormalTexture);

        Mesh mesh = VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshIndirect(renderParams, quadBillboard, commandBuffer);
    }
    */

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

        renderParams.matProps = new MaterialPropertyBlock();
        renderParams.matProps.SetBuffer("_BlittablePropBuffer", buffer);
        renderParams.matProps.SetVector("_BoundsOffset", renderParams.worldBounds.center);
        renderParams.matProps.SetTexture("_Albedo", extraData.billboardAlbedoTexture);
        renderParams.matProps.SetTexture("_Normal_Map", extraData.billboardNormalTexture);

        Mesh mesh = VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshPrimitives(renderParams, mesh, 0, count);
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos) {
            int size = VoxelUtils.PropSegmentSize;
            foreach (var item in propSegmentsDict) {
                var key = item.Key.xyz;
                Vector3 center = new Vector3(key.x, key.y, key.z) * size + Vector3.one * size / 2.0f;

                switch (item.Key.w) {
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
        propSegments.Dispose();
        oldPropSegments.Dispose();
        addedSegments.Dispose();
        removedSegments.Dispose();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Partial implementation simply responsible for billboard capturing and indirect rendering
public partial class VoxelProps {
    // Used for prop billboard captures
    [Header("Billboard Capture & Rendering")]
    public bool renderIndirectInstancingMeshes;
    public GameObject propCaptureCameraPrefab;
    public Material propCaptureFullscreenMaterial;
    public Mesh quadBillboard;
    public Material billboardMaterialBase;

    // Extra prop data that is shared with the prop segments
    public List<IndirectExtraPropData> extraPropData;

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
        bool mips = prop.billboardMipMaps;

        var temp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        temp.autoGenerateMips = mips;
        temp.useMipMap = mips;
        camera.targetTexture = temp;


        // Pregenerate the textures so we can copy to them on the GPU
        Texture2DArray albedoTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, mips);
        Texture2DArray normalTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, mips);
        Texture2DArray maskTextureOut = new Texture2DArray(width, height, prop.variants.Count, TextureFormat.ARGB32, mips);
        Texture2DArray[] tempOut = new Texture2DArray[3] { albedoTextureOut, normalTextureOut, maskTextureOut };

        // Many different variants per prop type
        for (int i = 0; i < prop.variants.Count; i++) {
            PropType.PropVariantType variant = prop.variants[i];
            camera.orthographicSize = variant.billboardCaptureCameraScale;

            // Spawns the fake object and put it in its own layer with the camera
            GameObject faker = Instantiate(variant.prefab);
            faker.GetComponent<SerializableProp>().OnSpawnCaptureFake(camera, tempOut, i);
            faker.layer = 31;
            foreach (Transform item in faker.transform) {
                item.gameObject.layer = 31;
            }

            // Move the prop to the appropriate position
            faker.transform.position = variant.billboardCapturePosition;
            //  + r * new Vector3(0, 90, 0)
            faker.transform.eulerAngles = variant.billboardCaptureRotation;

            // Renders the camera 3 times (albedo, normal, mask)
            for (int j = 0; j < 3; j++) {
                tempOut[j].filterMode = prop.billboardTextureFilterMode;
                propCaptureFullscreenMaterial.SetInteger("_TextureType", j);
                camera.Render();
                for (int m = 0; m < temp.mipmapCount; m++) {
                    Graphics.CopyTexture(temp, 0, m, tempOut[j], i, m);
                }
            }

            // Reset the prop variant
            faker.GetComponent<SerializableProp>().OnDestroyCaptureFake();
            faker.SetActive(false);
            Destroy(faker);
            temp.DiscardContents(true, true);
            temp.Release();
        }

        return new IndirectExtraPropData {
            billboardAlbedoTexture = albedoTextureOut,
            billboardNormalTexture = normalTextureOut,
            billboardMaskTexture = maskTextureOut,
        };
    }

    // Render the indirectly rendered (billboard / instanced) props 
    private void Update() {
        if (terrain.VoxelOctree.target == null || !renderIndirectInstancingMeshes)
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
        tempCount.SetData(new int[1]);
        propCullingCopy.SetBuffer(0, "totalTemp", tempCount);
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

        var temp = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        //drawArgsBuffer.GetData(temp);
        //Debug.Log("Culled: " + temp[0].instanceCount);

        var temp2 = new int[1];
        //tempCount.GetData(temp2);
        //Debug.Log("Unculled: " + temp2[0]);
    }

    // Render the instanced mesh for a specific type of prop type
    // This is either the billboard or the given instanced mesh
    private void RenderInstancesOfType(int i, IndirectExtraPropData extraData, PropType prop) {
        bool meshed = prop.propSpawnBehavior.HasFlag(PropSpawnBehavior.SwapForInstancedMeshes);

        if (meshed && (prop.instancedMeshMaterial == null || prop.instancedMesh == null))
            return;

        Material material = meshed ? prop.instancedMeshMaterial : billboardMaterialBase;

        ShadowCastingMode shadowCastingMode = prop.instancesCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
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
            mat.SetInt("_RECEIVE_SHADOWS_OFF", prop.instancesCastShadows ? 0 : 1);
            mat.SetInt("_Lock_Rotation_Y", prop.instancesRestrictRotationY ? 1 : 0);
        }

        Mesh mesh = meshed ? prop.instancedMesh : VoxelTerrain.Instance.VoxelProps.quadBillboard;
        Graphics.RenderMeshIndirect(renderParams, mesh, drawArgsBuffer, 1, i);
    }
}

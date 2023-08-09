using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Struct containing the render textures of the voxel data
public struct VoxelRenderTextures
{
    // Voxel densities stored as halfs for performance
    [HideInInspector]
    public RenderTexture densityTexture;

    // Color (stored as vec3<u8>) and material index (u8) packed together
    [HideInInspector]
    public RenderTexture colorMaterialTexture;
}

// Struct containing read-only voxel textures
public struct VoxelTextures
{
    // Voxel densities stored as halfs for performance
    [HideInInspector]
    public Texture3D densityTexture;

    // Color (stored as vec3<u8>) and material index (u8) packed together
    [HideInInspector]
    public Texture3D colorMaterialTexture;
}

// Struct containing the native arrays of the voxel data
public struct VoxelNativeArrays
{
    // Voxel densities stored as halfs for performance
    public NativeArray<half> densities;

    // Color (stored as vec3<u8>) and material index (u8) packed together
    public NativeArray<uint> colorMaterials;
}

// Generated voxel data from the GPU
// Allows us to check if the readback has finished and if we can use the NativeArray
// Also allows us to Free the native array to give it back to the Voxel Generator for generation
public class VoxelReadbackRequest 
{
    public bool DensityReadbackCompleted { get; internal set; }
    public bool ColorMaterialsReadbackCompleted { get; internal set; }
    public bool Completed
    {
        get
        {
            return DensityReadbackCompleted && ColorMaterialsReadbackCompleted;
        }
    }

    public int Index { get; internal set; }

    public VoxelGenerator generator;
    public VoxelChunk chunk;
    public VoxelNativeArrays nativeArrays;

    // Dispose of the request's memory, giving it back to the VoxelGenerator
    public void Dispose() {
        generator.freeVoxelNativeArrays[Index] = true;
        generator.voxelNativeArrays[Index] = nativeArrays;
        chunk = null;
    }
}

// Responsible for generating the voxel data using the voxel graph
public class VoxelGenerator : VoxelBehaviour
{
    [Header("Voxelization Settings")]
    public Vector3 worldOffset = Vector3.zero;
    public Vector3 worldScale = Vector3.one;

    [Header("Seeding Behavior")]
    public Vector3Int permutationSeed = Vector3Int.zero;
    public Vector3Int moduloSeed = Vector3Int.zero;

    // Added onto the isosurface value
    public float isosurfaceOffset = 0.0F;

    // Compute shader that will be responsble for voxel generation
    public ComputeShader voxelShader;

    // Render texture responsible for storing voxels
    [HideInInspector]
    public VoxelRenderTextures readbackTextures;

    // Number of simultaneous async readbacks that happen during one frame
    [Range(1, 8)]
    public int asyncReadbacks = 1;

    // List of persistently allocated native arrays
    internal List<VoxelNativeArrays> voxelNativeArrays;

    // Bitset containing the voxel native arrays that are free
    internal BitArray freeVoxelNativeArrays;

    // Chunks that we must generate the voxels for
    internal Queue<VoxelChunk> pendingVoxelGenerationChunks = new Queue<VoxelChunk>();

    // Get the number of voxel generation tasks pen
    /*
    public int VoxelGenerationTasksRemaining
    {
        get
        {
            if (pendingVoxelGenerationChunks != null)
            {
                return pendingVoxelGenerationChunks.Count;
            } else
            {
                return 0;
            }
        }
    }
    */

    // Checks if we completed voxel generation
    public bool Free
    {
        get
        {
            if (pendingVoxelGenerationChunks != null && freeVoxelNativeArrays != null)
            {
                bool free = pendingVoxelGenerationChunks.Count == 0;
                free &= freeVoxelNativeArrays.Cast<bool>().All(x => x);
                return free;
            }
            else
            {
                return false;
            }
        }
    }

    // Called when a chunk finishes generating its voxel data
    // The request must be disposed of otherwise we will not be able to generate more voxels
    public delegate void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request);
    public event OnVoxelGenerationComplete onVoxelGenerationComplete;

    // Initialize the voxel generator
    internal override void Init()
    {
        voxelNativeArrays = new List<VoxelNativeArrays>(asyncReadbacks);

        readbackTextures = new VoxelRenderTextures
        {
            densityTexture = VoxelUtils.CreateTexture(VoxelUtils.Size, GraphicsFormat.R16_SFloat),
            colorMaterialTexture = VoxelUtils.CreateTexture(VoxelUtils.Size, GraphicsFormat.R32_UInt),
        };
        
        freeVoxelNativeArrays = new BitArray(asyncReadbacks, true);

        for (int i = 0; i < asyncReadbacks; i++)
        {
            VoxelNativeArrays arrays = new VoxelNativeArrays
            {
                densities = new NativeArray<half>(VoxelUtils.Total, Allocator.Persistent),
                colorMaterials = new NativeArray<uint>(VoxelUtils.Total, Allocator.Persistent)
            };

            voxelNativeArrays.Add(arrays);
        }

        UpdateStaticComputeFields();
    }

    // Randomize the stored seeds
    public void RandomizeSeeds()
    {
        permutationSeed.x = UnityEngine.Random.Range(-10, 10);
        permutationSeed.y = UnityEngine.Random.Range(-10, 10);
        permutationSeed.z = UnityEngine.Random.Range(-10, 10);

        moduloSeed.x = UnityEngine.Random.Range(-10, 10);
        moduloSeed.y = UnityEngine.Random.Range(-10, 10);
        moduloSeed.z = UnityEngine.Random.Range(-10, 10);

        UpdateStaticComputeFields();
    }

    // Update the static world generation fields (will also update the seed)
    public void UpdateStaticComputeFields()
    {
        voxelShader.SetVector("worldOffset", worldOffset);
        voxelShader.SetVector("worldScale", worldScale * VoxelUtils.VoxelSize);
        voxelShader.SetFloat("isosurfaceOffset", isosurfaceOffset);
        voxelShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        voxelShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        voxelShader.SetTexture(0, "densityTexture", readbackTextures.densityTexture);
        voxelShader.SetTexture(0, "colorMaterialTexture", readbackTextures.colorMaterialTexture);
    }

    // Add the given chunk inside the queue for voxel generation
    public void GenerateVoxels(VoxelChunk chunk) {
        pendingVoxelGenerationChunks.Enqueue(chunk);
    }

    // Get the latest chunk in the queue and generate voxel data for it
    void Update()
    {    
        for(int i = 0; i < asyncReadbacks; i++)
        {
            if (!freeVoxelNativeArrays[i])
            {
                continue;
            }

            VoxelChunk chunk = null;
            if (pendingVoxelGenerationChunks.TryDequeue(out chunk)) {
                Vector3 test = Vector3.one * (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) * 0.5F;
                voxelShader.SetVector("chunkOffset", (chunk.transform.position - test) / VoxelUtils.VoxelSize);
                voxelShader.SetFloat("chunkScale", (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) / VoxelUtils.VoxelSize);

                int count = VoxelUtils.Size / 4;
                voxelShader.Dispatch(0, count, count, count);

                VoxelReadbackRequest voxelReadbackRequest = new VoxelReadbackRequest
                {
                    Index = i,
                    ColorMaterialsReadbackCompleted = false,
                    DensityReadbackCompleted = false,
                    generator = this,
                    chunk = chunk,
                    nativeArrays = voxelNativeArrays[i],
                };

                NativeArray<half> densities = voxelNativeArrays[i].densities;
                NativeArray<uint> colorMaterials = voxelNativeArrays[i].colorMaterials;

                freeVoxelNativeArrays[i] = false;

                AsyncGPUReadback.RequestIntoNativeArray(
                    ref densities,
                    readbackTextures.densityTexture, 0,
                    delegate (AsyncGPUReadbackRequest request)
                    {
                        voxelReadbackRequest.DensityReadbackCompleted = true;

                        if (voxelReadbackRequest.Completed)
                        {
                            onVoxelGenerationComplete?.Invoke(chunk, voxelReadbackRequest);
                        }
                    }
                );

                AsyncGPUReadback.RequestIntoNativeArray(
                    ref colorMaterials,
                    readbackTextures.colorMaterialTexture, 0,
                    delegate (AsyncGPUReadbackRequest request)
                    {
                        voxelReadbackRequest.ColorMaterialsReadbackCompleted = true;
                
                        if (voxelReadbackRequest.Completed)
                        {
                            onVoxelGenerationComplete?.Invoke(chunk, voxelReadbackRequest);
                        }
                    }
                );

            }
        }
    }

    internal override void Dispose()
    {
        AsyncGPUReadback.WaitAllRequests();
        foreach (VoxelNativeArrays nativeArrays in voxelNativeArrays)
        {
            nativeArrays.densities.Dispose();
            nativeArrays.colorMaterials.Dispose();
        }
    }
}

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

// Generated voxel data from the GPU
// Allows us to check if the readback has finished and if we can use the NativeArray
// Also allows us to Free the native array to give it back to the Voxel Generator for generation
public class VoxelReadbackRequest 
{
    public bool Completed { get; internal set; }
    public int Index { get; internal set; }

    public VoxelGenerator generator;
    public VoxelChunk chunk;

    // Voxelized memory
    public NativeArray<float> voxelized;

    // Dispose of the request's memory, giving it back to the VoxelGenerator
    public void Dispose() {
        generator.freeVoxelNativeArrays[Index] = true;
        generator.voxelNativeArrays[Index] = voxelized;
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
    public RenderTexture voxelTexture;

    // Number of simultaneous async readbacks that happen during one frame
    [Range(1, 8)]
    public int asyncReadbacks = 1;

    // List of persistently allocated native arrays
    internal List<NativeArray<float>> voxelNativeArrays;

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
        voxelNativeArrays = new List<NativeArray<float>>(asyncReadbacks);
        voxelTexture = VoxelUtils.CreateTexture(VoxelUtils.Size, GraphicsFormat.R32_SFloat);
        freeVoxelNativeArrays = new BitArray(asyncReadbacks, true);

        for (int i = 0; i < asyncReadbacks; i++)
        {
            voxelNativeArrays.Add(new NativeArray<float>(VoxelUtils.Total, Allocator.Persistent));
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
                voxelShader.SetTexture(0, "voxelTexture", voxelTexture);
                Vector3 test = Vector3.one * (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) * 0.5F;

                //voxelShader.SetVector("chunkOffset", (chunk.transform.position / VoxelUtils.VoxelSize) / VoxelUtils.VertexScaling);
                voxelShader.SetVector("chunkOffset", (chunk.transform.position - test) / VoxelUtils.VoxelSize);
                //voxelShader.SetFloat("chunkScale", chunk.transform.localScale.x);
                voxelShader.SetFloat("chunkScale", (chunk.node.WorldSize().x / ((float)VoxelUtils.Size - 2.0F)) / VoxelUtils.VoxelSize);

                int count = VoxelUtils.Size / 4;
                voxelShader.Dispatch(0, count, count, count);

                VoxelReadbackRequest voxelReadbackRequest = new VoxelReadbackRequest
                {
                    Index = i,
                    Completed = false,
                    generator = this,
                    chunk = chunk,
                    voxelized = voxelNativeArrays[i],
                };

                NativeArray<float> nativeArray = voxelNativeArrays[i];
                freeVoxelNativeArrays[i] = false;

                AsyncGPUReadback.RequestIntoNativeArray(
                    ref nativeArray,
                    voxelTexture, 0,
                    delegate (AsyncGPUReadbackRequest request)
                    {
                        voxelReadbackRequest.Completed = true;
                        onVoxelGenerationComplete?.Invoke(chunk, voxelReadbackRequest);
                    }
                );
                
            }
        }
    }

    internal override void Dispose()
    {
        AsyncGPUReadback.WaitAllRequests();
        foreach (NativeArray<float> nativeArray in voxelNativeArrays)
        {
            nativeArray.Dispose();
        }
    }
}

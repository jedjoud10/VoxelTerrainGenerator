using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Generated voxel data from the GPuU
// Allows us to check if the readback has finished and if we can use the NativeArray
// Also allows us to Free the native array to give it back to the Voxel Generator for generation
public class VoxelReadbackRequest 
{
    public bool completed;
    public int index;

    public VoxelGenerator generator;
    public VoxelChunk chunk;

    // Voxelized memory
    public NativeArray<float> voxelized;


    // Dispose of the request's memory, giving it back to the VoxelGenerator
    public void Dispose() {
        generator.freeVoxelNativeArrays[index] = true;
        generator.voxelNativeArrays[index] = voxelized;
        chunk = null;
    }
}

// Responsible for generating the voxel data using the voxel graph
public class VoxelGenerator : MonoBehaviour
{
    // Compute shader that will be responsble for voxel generation
    public ComputeShader voxelShader;

    // Render texture responsible for storing voxels
    public RenderTexture voxelTexture;

    // Number of simultaneous async readbacks that happen during one frame
    [Range(1, 4)]
    public int asyncReadbacks = 1;

    // List of persistently allocated native arrays
    internal List<NativeArray<float>> voxelNativeArrays;

    // Bitset containing the voxel native arrays that are free
    internal BitArray freeVoxelNativeArrays;

    // Chunks that we must generate the voxels for
    internal Queue<VoxelChunk> pendingVoxelGenerationChunks = new Queue<VoxelChunk>();

    // Called when a chunk finishes generating its voxel data
    // The request must be disposed of otherwise we will not be able to generate more voxels
    public delegate void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request);
    public event OnVoxelGenerationComplete onVoxelGenerationComplete;

    // Creates the voxel data list for triple or double buffering
    void Start()
    {
        voxelNativeArrays = new List<NativeArray<float>>(asyncReadbacks);
        voxelTexture = VoxelUtils.CreateTexture(64, GraphicsFormat.R32_SFloat);
        freeVoxelNativeArrays = new BitArray(asyncReadbacks, true);

        for(int i = 0; i < asyncReadbacks; i++)
        {
            voxelNativeArrays.Add(new NativeArray<float>(64*64*64, Allocator.Persistent));
        }
    }

    public void GenerateVoxels(VoxelChunk chunk) {
        pendingVoxelGenerationChunks.Enqueue(chunk);
    }

    // Get the latest chunk in the queue and generate voxel data for it
    void Update()
    {
        AsyncGPUReadback.WaitAllRequests();
    
        for(int i = 0; i < asyncReadbacks; i++)
        {
            if (!freeVoxelNativeArrays[i])
            {
                continue;
            }

            VoxelChunk chunk = null;
            if (pendingVoxelGenerationChunks.TryDequeue(out chunk)) {
                // begin generating the voxel data
                voxelShader.SetTexture(0, "voxelTexture", voxelTexture);
                voxelShader.SetVector("offset", (chunk.transform.position / VoxelUtils.VOXEL_SIZE) / VoxelUtils.VERTEX_SCALING);
                voxelShader.Dispatch(0, 16, 16, 16);

                VoxelReadbackRequest voxelReadbackRequest = new VoxelReadbackRequest
                {
                    index = i,
                    completed = false,
                    generator = this,
                    chunk = chunk,
                    voxelized = voxelNativeArrays[i],
                };

                NativeArray<float> nativeArray = voxelNativeArrays[i];
                freeVoxelNativeArrays[i] = false;

                AsyncGPUReadback.RequestIntoNativeArray(
                    ref nativeArray,
                    voxelTexture, 0,
                    delegate(AsyncGPUReadbackRequest request) 
                    {
                        voxelReadbackRequest.completed = true;
                        onVoxelGenerationComplete.Invoke(chunk, voxelReadbackRequest);
                    }
                );
            }
        }
    }
    
    void OnApplicationQuit() {
        AsyncGPUReadback.WaitAllRequests();
        foreach (NativeArray<float> nativeArray in voxelNativeArrays) 
        {
            nativeArray.Dispose();
        }
    }
}

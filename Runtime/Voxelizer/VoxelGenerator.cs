using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Responsible for generating the voxel data using the voxel graph
public class VoxelGenerator : VoxelBehaviour {
    [Header("Voxelization Settings")]
    public Vector3 worldOffset = Vector3.zero;
    public Vector3 worldScale = Vector3.one;

    [Header("Seeding Behavior")]
    public int seed = 1234;
    public Vector3Int permutationSeed = Vector3Int.zero;
    public Vector3Int moduloSeed = Vector3Int.zero;

    // Added onto the isosurface value
    public float isosurfaceOffset = 0.0F;

    // Compute shader that will be responsble for voxel generation
    public ComputeShader voxelShader;

    // Render texture responsible for storing voxels
    [HideInInspector]
    public RenderTexture readbackTexture;

    // Number of simultaneous async readbacks that happen during one frame
    [Range(1, 8)]
    public int asyncReadbacks = 1;

    // List of persistently allocated native arrays
    internal List<NativeArray<Voxel>> voxelNativeArrays;

    // Bitset containing the voxel native arrays that are free
    internal BitArray freeVoxelNativeArrays;

    // Chunks that we must generate the voxels for
    internal Queue<VoxelChunk> pendingVoxelGenerationChunks;

    // Checks if we completed voxel generation
    public bool Free {
        get {
            if (pendingVoxelGenerationChunks != null && freeVoxelNativeArrays != null) {
                bool free = pendingVoxelGenerationChunks.Count == 0;
                free &= freeVoxelNativeArrays.Cast<bool>().All(x => x);
                return free;
            } else {
                return false;
            }
        }
    }

    // Called when a chunk finishes generating its voxel data
    // The request must be disposed of otherwise we will not be able to generate more voxels
    public delegate void OnVoxelGenerationComplete(VoxelChunk chunk, VoxelReadbackRequest request);
    public event OnVoxelGenerationComplete onVoxelGenerationComplete;

    // Initialize the voxel generator
    internal override void Init() {
        readbackTexture = VoxelUtils.Create3DRenderTexture(VoxelUtils.Size, GraphicsFormat.R32_UInt);
        freeVoxelNativeArrays = new BitArray(asyncReadbacks, true);
        pendingVoxelGenerationChunks = new Queue<VoxelChunk>();
        voxelNativeArrays = new List<NativeArray<Voxel>>(asyncReadbacks);
        for (int i = 0; i < asyncReadbacks; i++) {
            voxelNativeArrays.Add(new NativeArray<Voxel>(VoxelUtils.Volume, Allocator.Persistent));
        }

        UpdateStaticComputeFields();
    }

    // Update the static world generation fields (will also update the seed)
    public void UpdateStaticComputeFields() {
        var random = new System.Random(seed);

        permutationSeed.x = random.Next(-1000, 1000);
        permutationSeed.y = random.Next(-1000, 1000);
        permutationSeed.z = random.Next(-1000, 1000);
        moduloSeed.x = random.Next(-1000, 1000);
        moduloSeed.y = random.Next(-1000, 1000);
        moduloSeed.z = random.Next(-1000, 1000);

        voxelShader.SetVector("worldOffset", worldOffset);
        voxelShader.SetVector("worldScale", worldScale * VoxelUtils.VoxelSizeFactor);
        voxelShader.SetFloat("isosurfaceOffset", isosurfaceOffset);
        voxelShader.SetInts("permuationSeed", new int[] { permutationSeed.x, permutationSeed.y, permutationSeed.z });
        voxelShader.SetInts("moduloSeed", new int[] { moduloSeed.x, moduloSeed.y, moduloSeed.z });
        voxelShader.SetInt("size", VoxelUtils.Size);
        voxelShader.SetFloat("voxelSize", VoxelUtils.VoxelSizeFactor);
        voxelShader.SetFloat("vertexScaling", VoxelUtils.VertexScaling);
        voxelShader.SetTexture(0, "voxels", readbackTexture);
    }

    // Add the given chunk inside the queue for voxel generation
    public void GenerateVoxels(VoxelChunk chunk) {
        if (pendingVoxelGenerationChunks.Contains(chunk)) return;
        pendingVoxelGenerationChunks.Enqueue(chunk);
    }

    // Get the latest chunk in the queue and generate voxel data for it
    void Update() {
        for (int i = 0; i < asyncReadbacks; i++) {
            if (!freeVoxelNativeArrays[i]) {
                continue;
            }

            VoxelChunk chunk = null;
            if (pendingVoxelGenerationChunks.TryDequeue(out chunk)) {
                // Set chunk only parameters
                voxelShader.SetVector("chunkOffset", chunk.transform.position);
                voxelShader.SetFloat("chunkScale", chunk.transform.localScale.x);

                // Generate the voxel data for the chunk
                int count = VoxelUtils.Size / 4;
                voxelShader.Dispatch(0, count, count, count);

                // Begin the readback request
                VoxelReadbackRequest voxelReadbackRequest = new VoxelReadbackRequest {
                    Index = i,
                    generator = this,
                    chunk = chunk,
                    voxels = voxelNativeArrays[i],
                };

                // Readback the voxel texture
                freeVoxelNativeArrays[i] = false;
                NativeArray<Voxel> voxels = voxelNativeArrays[i];
                AsyncGPUReadback.RequestIntoNativeArray(
                    ref voxels,
                    readbackTexture, 0,
                    delegate (AsyncGPUReadbackRequest asyncRequest) {
                        onVoxelGenerationComplete?.Invoke(voxelReadbackRequest.chunk, voxelReadbackRequest);
                    }
                );
            }
        }
    }

    internal override void Dispose() {
        AsyncGPUReadback.WaitAllRequests();
        foreach (var nativeArrays in voxelNativeArrays) {
            nativeArrays.Dispose();
        }
    }
}

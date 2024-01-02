using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour {
    // Max number of chunks we should edit at the same time (should be less than or equal to max mesh jobs)
    [Range(0, 8)]
    public int maxImmediateMeshEditJobsPerEdit = 1;
    public bool debugGUI = false;

    // Dictionary to map chunk positions to sparseVoxelData indices
    // Contains a bitmask telling us what chunks of a specific segment is enabled
    private NativeArray<VoxelDeltaLookup> lookup;

    // All the chunks the user has modified in each LOD level
    // Stored like this: layers -> unsafe list -> sparse voxel delta chunks
    private UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    // Initialize the voxel edits handler
    internal override void Init() {
        lookup = new NativeArray<VoxelDeltaLookup>(VoxelUtils.MaxSegments * VoxelUtils.MaxSegments * VoxelUtils.MaxSegments, Allocator.Persistent);
        sparseVoxelData = new UnsafeList<SparseVoxelDeltaData>(0, Allocator.Persistent);
    }

    // Dispose of any memory
    internal override void Dispose() {
        for (int i = 0; i < lookup.Length; i++) {
            UnsafeBitArray bitset = lookup[i].bitset;
            if (bitset.IsCreated && !bitset.IsEmpty) {
                for (int j = 0; j < VoxelUtils.ChunksPerSegmentVolume; j++) {
                    if (bitset.IsSet(j)) {
                        SparseVoxelDeltaData data = sparseVoxelData[j];
                        data.materials.Dispose();
                        data.densities.Dispose();
                    }
                }
            }

        }
        sparseVoxelData.Dispose();
        lookup.Dispose();
    }

    // Apply a voxel edit to the terrain world immediately
    public void ApplyVoxelEdit<T>(T edit) where T : struct, IVoxelEdit {
        if (!terrain.Free || !terrain.VoxelGenerator.Free || !terrain.VoxelMesher.Free || !terrain.VoxelOctree.Free)
            return;

        // Idk why we have to do this bruh this shit don't make no sense 
        float extentOffset = VoxelUtils.VoxelSizeFactor * 4.0F;
        Bounds bound = edit.GetBounds();
        //bound.Expand(200);

        // Custom job to find all the octree nodes that touch the bounds
        NativeList<OctreeNode>? temp;
        terrain.VoxelOctree.TryCheckAABBIntersection(bound, out temp);

        // Sparse voxel chunks that we must edit
        List<SparseVoxelDeltaChunk> sparseVoxelEditChunks = InitSegmentsFindSparseChunks(bound);

        // Modify sparse voxel data
        foreach (var item in sparseVoxelEditChunks) {
            SparseVoxelDeltaData data = sparseVoxelData[item.listIndex];
            VoxelEditJob<T> job = new VoxelEditJob<T> {
                chunkOffset = math.float3(item.position) * VoxelUtils.Size * VoxelUtils.VoxelSizeFactor,
                voxelScale = VoxelUtils.VoxelSizeFactor,
                size = VoxelUtils.Size,
                vertexScaling = VoxelUtils.VertexScaling,
                edit = edit,
                densities = data.densities,
                materials = data.materials,
            };

            JobHandle handle = job.Schedule(VoxelUtils.Volume, 2048);
            handle.Complete();
        }

        // Re-mesh the chunks
        foreach (var node in temp) {
            VoxelChunk chunk = terrain.Chunks[node];
            if (chunk.uniqueVoxelContainer) {
                // Regenerate the mesh based on the unique voxel container
                terrain.VoxelMesher.GenerateMesh(chunk, true);
            } else {
                // If not, simply regenerate the chunk
                // This is pretty inefficient but it's a matter of memory vs performance
                terrain.VoxelGenerator.GenerateVoxels(chunk);
            }
        }
        temp.Value.Dispose();
    }

    // Makes sure the segments that intersect the bounds are loaded in and ready for modification
    // This will also return the sparse voxel delta chunks that have been initialized and that must be written to
    private List<SparseVoxelDeltaChunk> InitSegmentsFindSparseChunks(Bounds bounds) {
        List<SparseVoxelDeltaChunk> sparseVoxelEditChunks = new List<SparseVoxelDeltaChunk>();
        int3 offset = -VoxelUtils.MaxSegments / 2;

        for (int x = 0; x < VoxelUtils.MaxSegments; x++) {
            for (int y = 0; y < VoxelUtils.MaxSegments; y++) {
                for (int z = 0; z < VoxelUtils.MaxSegments; z++) {
                    int3 segmentCoords = math.int3(x, y, z);
                    int3 worldSegmentCoords = segmentCoords + offset;
                    if (VoxelUtils.SegmentCoordsIntersectBounds(worldSegmentCoords, bounds)) {
                        InitSegmentFindSparseChunks(bounds, worldSegmentCoords, ref sparseVoxelEditChunks);
                    }
                }
            }
        }

        return sparseVoxelEditChunks;
    }

    // Makes sure the chunks that intersects the bounds (for this segment) are ready for editing
    private void InitSegmentFindSparseChunks(Bounds bounds, int3 worldSegmentCoords, ref List<SparseVoxelDeltaChunk> sparseVoxelEditChunks) {
        uint3 uintSegmentCoords = math.uint3(worldSegmentCoords + VoxelUtils.MaxSegments / 2);
        int segmentIndex = VoxelUtils.PosToIndex(uintSegmentCoords, (uint)VoxelUtils.MaxSegments);
        VoxelDeltaLookup segment = lookup[segmentIndex];

        // This will initialize the segment if it does not contain any chunks
        if (!segment.bitset.IsCreated) {
            segment.bitset = new UnsafeBitArray(VoxelUtils.ChunksPerSegmentVolume, Allocator.Persistent);
            segment.startingIndex = sparseVoxelData.Length;

            for (int i = 0; i < VoxelUtils.ChunksPerSegmentVolume; i++) {
                sparseVoxelData.Add(SparseVoxelDeltaData.Empty);
            }
        }

        // This loop will create the memory allocations for edited chunks
        for (int i = 0; i < VoxelUtils.ChunksPerSegmentVolume; i++) {
            int3 localChunkCoords = math.int3(VoxelUtils.IndexToPos(i, (uint)VoxelUtils.ChunksPerSegment));
            int3 globalChunkCoords = worldSegmentCoords * VoxelUtils.ChunksPerSegment + localChunkCoords;

            // Check if the chunk intersects the given input bounds
            if (VoxelUtils.ChunkCoordsIntersectBounds(globalChunkCoords, bounds)) {
                // Initialize the SparseVoxelDeltaData chunk if it was not already initialized
                if (!segment.bitset.IsSet(i)) {
                    segment.bitset.Set(i, true);

                    SparseVoxelDeltaData data = new SparseVoxelDeltaData {
                        densities = new UnsafeList<half>(VoxelUtils.Volume, Allocator.Persistent),
                        materials = new UnsafeList<ushort>(VoxelUtils.Volume, Allocator.Persistent),
                    };

                    data.densities.Resize(VoxelUtils.Volume);
                    data.materials.Resize(VoxelUtils.Volume);

                    for (int k = 0; k < VoxelUtils.Volume; k++) {
                        data.densities[i] = half.zero;
                        data.materials[i] = ushort.MaxValue;
                    }

                    sparseVoxelData[segment.startingIndex + i] = data;
                }

                sparseVoxelEditChunks.Add(new SparseVoxelDeltaChunk {
                    position = globalChunkCoords,
                    listIndex = segment.startingIndex + i,
                });
            }
        }

        lookup[segmentIndex] = segment;
    }

    // Check if a chunk was modified (or if it contains regions of modified voxels)
    public bool WasChunkModified(VoxelChunk chunk) {
        return true;
    }

    // Create an apply job dependeny for a chunk that is about to be meshed
    // Chunk voxel temp container MUST be valid
    public void TryGetApplyJobDependency(VoxelChunk chunk, ref NativeArray<Voxel> outputVoxels, out JobHandle newDependency) {
        if (!WasChunkModified(chunk)) {
            newDependency = new JobHandle();
            return;
        }

        VoxelEditApplyJob job = new VoxelEditApplyJob {
            lookup = lookup,
            sparseVoxelData = sparseVoxelData,
            inputVoxels = chunk.container.voxels,
            outputVoxels = outputVoxels,
            node = chunk.node,
            chunksPerSegment = VoxelUtils.ChunksPerSegment,
            segmentSize = VoxelUtils.SegmentSize,
            maxSegments = VoxelUtils.MaxSegments,
            size = VoxelUtils.Size,
            vertexScaling = VoxelUtils.VertexScaling,
            voxelScale = VoxelUtils.VoxelSizeFactor,
        };
        newDependency = job.Schedule(VoxelUtils.Volume, 2048);
        newDependency.Complete();
        return;
    }

    private void OnDrawGizmosSelected() {
        if (!lookup.IsCreated || !debugGUI)
            return;

        for (int i = 0; i < lookup.Length; i++) {
            Gizmos.color = new Color(1f, 1f, 1f, 1f);
            uint3 segmentCoordsUint = VoxelUtils.IndexToPos(i, (uint)VoxelUtils.MaxSegments);
            int3 segmentCoords = math.int3(segmentCoordsUint) - math.int3(VoxelUtils.MaxSegments / 2);

            VoxelDeltaLookup segment = lookup[i];

            if (!segment.bitset.IsCreated)
                continue;

            var offset = (float)VoxelUtils.SegmentSize;
            Vector3 segmentCenter = new Vector3(segmentCoords.x, segmentCoords.y, segmentCoords.z) * VoxelUtils.SegmentSize + Vector3.one * offset / 2F;
            Gizmos.DrawWireCube(segmentCenter * VoxelUtils.VoxelSizeFactor, Vector3.one * offset * VoxelUtils.VoxelSizeFactor);
            Vector3 offsetTwoIdfk = new Vector3(segmentCoords.x, segmentCoords.y, segmentCoords.z) * VoxelUtils.SegmentSize * VoxelUtils.VoxelSizeFactor;

            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            float size = VoxelUtils.VoxelSizeFactor * VoxelUtils.Size;
            for (int k = 0; k < VoxelUtils.ChunksPerSegmentVolume; k++) {
                if (!segment.bitset.IsSet(k))
                    continue;

                uint3 pos = VoxelUtils.IndexToPos(k, (uint)VoxelUtils.ChunksPerSegment);
                Vector3 pos2 = new Vector3(pos.x, pos.y, pos.z);
                Vector3 chunkOffset = Vector3.one * size * 0.5f;
                Vector3 chunkSize = new Vector3(size, size, size);
                Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawWireCube(pos2 * size + offsetTwoIdfk + chunkOffset, chunkSize);
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
// We will assume that the player can only edits the LOD0 chunks
// Everytime we edit a chunk (LOD0), we make a separate voxel data "delta" voxel array
// This new voxel array will then be additively added ontop of chunks further away

public class VoxelEdits : VoxelBehaviour
{
    // Max number of chunks we should edit at the same time (should be less than or equal to max mesh jobs)
    [Range(0, 8)]
    public int maxImmediateMeshEditJobsPerEdit = 1;

    // Sparse voxel data that we will check against
    private UnsafeList<SparseVoxelDeltaData> sparseVoxelData;

    // World is separated into segments of multiple chunks
    private NativeArray<VoxelDeltaRegion> segments;
    
    // Initialize the voxel edits handler
    internal override void Init()
    {
        segments = new NativeArray<VoxelDeltaRegion>(VoxelUtils.MaxSegments * VoxelUtils.MaxSegments * VoxelUtils.MaxSegments, Allocator.Persistent);
        sparseVoxelData = new UnsafeList<SparseVoxelDeltaData>(0, Allocator.Persistent);
    }

    // Dispose of any memory
    internal override void Dispose()
    {
        for (int i = 0; i < segments.Length; i++)
        {
            var bitset = segments[i].bitset;
            if (bitset.IsCreated && !bitset.IsEmpty)
            {
                for (int j = 0; j < 512; j++)
                {
                    if (bitset.IsSet(j))
                    {
                        SparseVoxelDeltaData data = sparseVoxelData[j];
                        data.materials.Dispose();
                        data.densities.Dispose();
                    }
                }
            }
        }

        segments.Dispose();
        sparseVoxelData.Dispose();
    }

    // Apply a voxel edit to the terrain world either immediately or asynchronously
    public void ApplyVoxelEdit<T>(T edit) where T : struct, IVoxelEdit
    {
        if (!terrain.Free || !terrain.VoxelGenerator.Free || !terrain.VoxelMesher.Free || !terrain.VoxelOctree.Free)       
            return;

        // Idk why we have to do this bruh this shit don't make no sense 
        float extentOffset = VoxelUtils.VoxelSizeFactor * 4.0F;
        Bounds bound = edit.GetBounds();
        bound.Expand(extentOffset);

        // Sparse voxel chunks that we must edit
        List<SparseVoxelDeltaChunk> sparseVoxelEditChunks = new List<SparseVoxelDeltaChunk>();

        // Make sure the sparse voxel data already exists
        InitSegmentsFindSparseChunks(bound, ref sparseVoxelEditChunks);
        Debug.Log($"Sparse delta chunks to edit: {sparseVoxelEditChunks.Count}");

        // Modify sparse voxel data
        foreach (var item in sparseVoxelEditChunks)
        {
            VoxelEditJob<T> job = new VoxelEditJob<T>
            {
                chunkOffset = math.float3(item.position),
                voxelScale = VoxelUtils.VoxelSizeFactor,
                size = VoxelUtils.Size,
                vertexScaling = VoxelUtils.VertexScaling,
                edit = edit,
                sparseVoxelData = sparseVoxelData,
                sparseVoxelDataChunkIndex = item.bitIndex,
            };

            JobHandle handle = job.Schedule(VoxelUtils.Size * VoxelUtils.Size * VoxelUtils.Size, 2048);
            handle.Complete();
        }

        // Apply sparse voxel data deltas onto affected chunks
    }

    // Makes sure the segments that intersect the bounds are loaded in and ready for modification
    // Not used for serialization / deserialization
    private void InitSegmentsFindSparseChunks(Bounds bounds, ref List<SparseVoxelDeltaChunk> sparseVoxelEditChunks)
    {
        float3 extents = new float3(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        uint3 uintExtents = math.uint3(math.ceil(extents / (float)VoxelUtils.SegmentSize));

        float3 offset = math.floor(new float3(bounds.min.x, bounds.min.y, bounds.min.z) / (float)VoxelUtils.SegmentSize);
        int3 uintOffset = math.int3(offset);

        for (int x = 0; x < uintExtents.x; x++)
        {
            for (int y = 0; y < uintExtents.y; y++)
            {
                for (int z = 0; z < uintExtents.z; z++)
                {
                    int3 segmentCoords = math.int3(x, y, z);
                    segmentCoords += uintOffset;
                    InitSegmentFindSparseChunks(bounds, segmentCoords, ref sparseVoxelEditChunks);
                }
            }
        }
    }

    // Makes sure the chunks that intersect the bounds (for this segment are ready for editing)
    // Not used for serialization / deserialization
    private void InitSegmentFindSparseChunks(Bounds bounds, int3 segmentCoords, ref List<SparseVoxelDeltaChunk> sparseVoxelEditChunks)
    {
        uint3 uintSegmentCoords = math.uint3(segmentCoords + VoxelUtils.MaxSegments / 2);
        int segmentIndex = VoxelUtils.PosToIndex(uintSegmentCoords, (uint)VoxelUtils.MaxSegments);
        VoxelDeltaRegion segment = segments[segmentIndex];

        // This will initialize the segment if it does not contain any chunks
        if (!segment.bitset.IsCreated)
        {
            segment.bitset = new UnsafeBitArray(512, Allocator.Persistent);
            segment.startingIndex = sparseVoxelData.Length;

            for (int i = 0; i < 512; i++)
            {
                sparseVoxelData.Add(SparseVoxelDeltaData.Empty);
            }
        }

        // This loop will create the memory allocations for edited chunks
        for (int i = 0; i < 512; i++)
        {
            int3 localChunkCoords = math.int3(VoxelUtils.IndexToPos(i, 8));
            int3 globalChunkCoords = segmentCoords * VoxelUtils.ChunksPerSegment + localChunkCoords;
            
            if (VoxelUtils.ChunkCoordsIntersectBounds(globalChunkCoords, bounds))
            {
                if (!segment.bitset.IsSet(i))
                {
                    segment.bitset.Set(i, true);

                    SparseVoxelDeltaData data = new SparseVoxelDeltaData
                    {
                        densities = new NativeArray<half>(VoxelUtils.Volume, Allocator.Persistent),
                        materials = new NativeArray<ushort>(VoxelUtils.Volume, Allocator.Persistent),
                    };

                    sparseVoxelData[segment.startingIndex + i] = data;
                }

                sparseVoxelEditChunks.Add(new SparseVoxelDeltaChunk
                {
                    position = globalChunkCoords,
                    bitIndex = i,
                });
            }
        }

        segments[segmentIndex] = segment;
    }

    /*
    private void OnDrawGizmosSelected()
    {
        if (!segments.IsCreated)
            return;

        for (int i = 0; i < segments.Length; i++)
        {
            uint3 segmentCoordsUint = VoxelUtils.IndexToPos(i, (uint)VoxelUtils.MaxSegments);
            int3 segmentCoords = math.int3(segmentCoordsUint) - math.int3(VoxelUtils.MaxSegments / 2);

            VoxelSegment segment = segments[i];

            if (!segment.bitset.IsCreated)
                continue;

            var offset = (float)VoxelUtils.SegmentSize;
            Vector3 segmentCenter = new Vector3(segmentCoords.x, segmentCoords.y, segmentCoords.z) * VoxelUtils.SegmentSize + Vector3.one * offset / 2F;
            Gizmos.DrawWireCube(segmentCenter, Vector3.one * offset);
        }
    }
    */
}

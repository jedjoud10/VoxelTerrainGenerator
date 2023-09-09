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
    private UnsafeList<SparseVoxelData> sparseVoxelData;

    // World is separated into segments of multiple chunks
    private NativeArray<VoxelSegment> segments;
    
    // Initialize the voxel edits handler
    internal override void Init()
    {
        segments = new NativeArray<VoxelSegment>(VoxelUtils.MaxSegments * VoxelUtils.MaxSegments * VoxelUtils.MaxSegments, Allocator.Persistent);
        sparseVoxelData = new UnsafeList<SparseVoxelData>(0, Allocator.Persistent);
    }

    // Dispose of any memory
    internal override void Dispose()
    {
        for (int i = 0; i < segments.Length; i++)
        {
            var bitset = segments[i].bitset;
            if (bitset != 0)
            {
                for (int j = 0; j < 64; j++)
                {
                    if (((bitset >> j) & 1) == 1)
                    {
                        SparseVoxelData data = sparseVoxelData[j];
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
    public void ApplyVoxelEdit<T>(T edit, bool immediate = false) where T : struct, IVoxelEdit
    {
        if (!terrain.Free || !terrain.VoxelGenerator.Free || !terrain.VoxelMesher.Free || !terrain.VoxelOctree.Free)       
            return;

        // Idk why we have to do this bruh this shit don't make no sense 
        float extentOffset = VoxelUtils.VoxelSizeFactor * 4.0F;
        Bounds bound = edit.GetBounds();
        bound.Expand(extentOffset);

        // Make sure the sparse voxel data already exists
        InitSegments(bound);

        // Modify said sparse voxel data
        // Apply sparse voxel data deltas onto affected chunks
    }

    // Makes sure the segments that intersect the bounds are loaded in and ready for modification
    // Not used for serialization / deserialization
    public void InitSegments(Bounds bounds)
    {
        float3 extents = new float3(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        uint3 uintExtents = math.uint3(math.ceil(extents / (float)VoxelUtils.SegmentSize));

        float3 offset = math.floor(new float3(bounds.min.x, bounds.min.y, bounds.min.z)) / (float)VoxelUtils.SegmentSize;
        int3 uintOffset = math.int3(offset);

        for (int x = 0; x < uintExtents.x; x++)
        {
            for (int y = 0; y < uintExtents.y; y++)
            {
                for (int z = 0; z < uintExtents.z; z++)
                {
                    int3 segmentCoords = math.int3(x, y, z);
                    segmentCoords += uintOffset;
                    InitSegment(bounds, segmentCoords);
                }
            }
        }
    }

    // Makes sure the chunks that intersect the bounds (for this segment are ready for editing)
    // Not used for serialization / deserialization
    public void InitSegment(Bounds bounds, int3 segmentCoords)
    {
        uint3 uintSegmentCoords = math.uint3(segmentCoords + VoxelUtils.MaxSegments / 2);
        int segmentIndex = VoxelUtils.PosToIndex(uintSegmentCoords, (uint)VoxelUtils.MaxSegments);
        VoxelSegment segment = segments[segmentIndex];

        // This will initialize the segment if it does not contain any chunks
        if (segment.bitset == 0)
        {
            segment.startingIndex = sparseVoxelData.Length;

            for (int i = 0; i < 64; i++)
            {
                sparseVoxelData.Add(SparseVoxelData.Empty);
            }
        }

        // This loop will create the memory allocations for edited chunks
        for (int i = 0; i < 64; i++)
        {
            int3 localChunkCoords = math.int3(VoxelUtils.IndexToPos(i, 4));
            int3 globalChunkCoords = segmentCoords * VoxelUtils.ChunksPerSegment + localChunkCoords;
            

            if (VoxelUtils.ChunkCoordsIntersectBounds(globalChunkCoords, bounds))
            {
                segment.bitset |= (ulong)1 << i;


                SparseVoxelData data = new SparseVoxelData
                {
                    densities = new NativeArray<half>(VoxelUtils.Volume, Allocator.Persistent),
                    materials = new NativeArray<ushort>(VoxelUtils.Volume, Allocator.Persistent),
                };

                sparseVoxelData[segment.startingIndex + i] = data;

                Debug.Log($"Intersect chunk {globalChunkCoords}");
            }
        }

        segments[segmentIndex] = segment;
    }

    private void OnDrawGizmosSelected()
    {
        
    }
}

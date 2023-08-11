using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles keeping track of voxel edits in the world
public class VoxelEdits : VoxelBehaviour
{
    // Indirection texture that contains the index of the textures at specific points in space
    private Texture3D indirectionTexture;

    // List of texture "segments" that will be sparsely stored in the world
    private List<Texture3D> editedVoxels;

    // Initialize the voxel edits handler
    internal override void Init()
    {
        editedVoxels = new List<Texture3D>();
    }

    // Dispose of any memory
    internal override void Dispose()
    {
    }

    // Apply a voxel edit to the terrain world immediately
    public void ApplyVoxelEdit<T>(T edit) where T : struct, IVoxelEdit
    {
        if (!terrain.Free)
        {
            Debug.LogWarning("Terrain currently active!");
            return;
        }

        // Get the edit's world AABB
        float3 extents = edit.GetWorldExtents() + math.float3(VoxelUtils.VoxelSize * 2.0);
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        // Find the chunks affected by the edit
        terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output);

        for(int i = 0; i < output.Value.Length; i++)
        {
            // Fetch chunk offsets + scale (like for compute shader)
            VoxelChunk chunk = terrain.Chunks[output.Value[i]];
            Vector3 offset = Vector3.one * (chunk.transform.localScale.x / ((float)VoxelUtils.Size)) * 0.5F;
            Vector3 chunkOffset = (chunk.transform.position / VoxelUtils.VoxelSize) / VoxelUtils.VertexScaling - offset;
            float scale = chunk.transform.localScale.x;

            // Begin the jobs for the affected chunks (synchronous)
            var job = new VoxelEditJob<T>
            {
                edit = edit,
                chunkOffset = new float3(chunkOffset.x, chunkOffset.y, chunkOffset.z),
                chunkScale = scale,
                worldScale = VoxelUtils.VoxelSize,
                size = (float)VoxelUtils.Size,
                voxels = chunk.voxels,
                vertexScaling = VoxelUtils.VertexScaling,
            }.Schedule(VoxelUtils.Volume, 512);

            // try generating the mesh immediately
            if (!terrain.VoxelMesher.TryGenerateMeshImmediate(chunk, chunk.AsContainer(), true, out _, job))
            {
                // if not possible, fallback to async
                terrain.VoxelMesher.GenerateMesh(chunk, chunk.AsContainer(), true, job);
            }
        }

        output.Value.Dispose();
    }

    private void OnDrawGizmos()
    {
        /*
        if (terrain == null || !terrain.started) { return; }

        var gm = GameObject.FindGameObjectWithTag("cueb");

        var edit = new SphereEdit {
            center = new float3(gm.transform.position.x, gm.transform.position.y, gm.transform.position.z),
            radius = 2.0F
        };

        Gizmos.DrawSphere(gm.transform.position, edit.radius);

        float3 extents = edit.GetWorldExtents();
        float3 center = edit.GetWorldCenter();

        float3 min = center - extents / 2;
        float3 max = center + extents / 2;

        if (terrain.VoxelOctree.TryCheckAABBIntersection(min, max, out var output))
        {
            foreach (var item in output)
            {
                Gizmos.DrawWireCube(item.WorldCenter(), item.WorldSize());
            }

            output.Value.Dispose();
        }
        */
    }
}

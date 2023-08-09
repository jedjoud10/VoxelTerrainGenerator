using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles generating the octree for all octree loaders and creating the octree and detecting the delta
public class VoxelOctree : VoxelBehaviour
{
    // Max depth of the octree
    [Min(1)]
    public int maxDepth = 8;

    // Quality LOD curve
    public float[] curvePoints;
    private NativeArray<float> qualityPointsNativeArray;

    // TODO: Make this work bruh (only set to one for now)
    private NativeArray<OctreeTarget> targets;

    // Native hashmap for keeping track of the current nodes in the tree
    private NativeHashSet<OctreeNode>[] octreeNodesBuffer;
    private int currentIndex;

    // Native arrays to diff the octree nodes
    private NativeList<OctreeNode> addedNodes;
    private NativeList<OctreeNode> removedNodes;

    // Used to generate the octree
    private NativeQueue<OctreeNode> pending;

    // Called whenever we detect a change in the octree
    public delegate void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode>removed);
    public event OnOctreeChanged onOctreeChanged;

    // Final job handle that we must wait for
    JobHandle finalJobHandle;

    // Used to make sure we only generate the octree when we are free
    VoxelMesher mesher;
    VoxelGenerator generator;

    private bool currentlyExecuting = false;
    private bool mustUpdate = false;

    // Intialize octree memory
    internal override void Init()
    {
        generator = GetComponent<VoxelGenerator>();
        mesher = GetComponent<VoxelMesher>();

        targets = new NativeArray<OctreeTarget>(1, Allocator.Persistent);
        targets[0] = new OctreeTarget();

        octreeNodesBuffer = new NativeHashSet<OctreeNode>[2];

        for (int i = 0; i < 2; i++)
        {
            octreeNodesBuffer[i] = new NativeHashSet<OctreeNode>(1, Allocator.Persistent);
        }
        
        pending = new NativeQueue<OctreeNode>(Allocator.Persistent);

        addedNodes = new NativeList<OctreeNode>(Allocator.Persistent);
        removedNodes = new NativeList<OctreeNode>(Allocator.Persistent);

        qualityPointsNativeArray = new NativeArray<float>(maxDepth, Allocator.Persistent);

        for (int i = 0; i < maxDepth; i++)
        {
            qualityPointsNativeArray[i] = curvePoints[i];
        }
    }

    // Force the octree to update due to an octree loader moving
    public void UpdateOctreeLoader(OctreeLoader loader)
    {
        float offset = (float)VoxelUtils.Size * VoxelUtils.VoxelSize;
        targets[0] = new OctreeTarget
        {
            generateCollisions = loader.generateCollisions,
            center = loader.transform.position / offset,
            radius = loader.radius / offset,
        };
        mustUpdate = true;
    }

    // Make sure the number of quality levels is equal the octree depth
    private void OnValidate()
    {
        if (curvePoints != null)
        {
            if (curvePoints.Length != maxDepth)
            {
                Array.Resize(ref curvePoints, maxDepth);
            }
        }
    }

    // Loop over all the octree loaders and generate the octree for them
    void Update()
    {
        if (currentlyExecuting && finalJobHandle.IsCompleted)
        {
            currentlyExecuting = false;
        }

        // Make sure we are free for octree generation
        bool free = mesher.Free && generator.Free;
        if (finalJobHandle.IsCompleted && free && !currentlyExecuting && mustUpdate)
        {
            mustUpdate = false;
            int index = currentIndex;
            currentIndex += 1;
            currentIndex = currentIndex % 2;

            // Ready up the allocations
            NativeHashSet<OctreeNode> oldNodes = octreeNodesBuffer[1 - index];
            NativeHashSet<OctreeNode> nodes = octreeNodesBuffer[index];
            pending.Enqueue(OctreeNode.RootNode(maxDepth-1));

            SubdivideJob job = new SubdivideJob
            {
                targets = targets,
                nodes = nodes,
                pending = pending,
                qualityPoints = qualityPointsNativeArray,
            };

            DiffJob addedDiffJob = new DiffJob
            {
                oldNodes = oldNodes,
                nodes = nodes,
                diffedNodes = addedNodes,
                direction = false,
            };

            DiffJob removedDiffJob = new DiffJob
            {
                oldNodes = oldNodes,
                nodes = nodes,
                diffedNodes = removedNodes,
                direction = true,
            };

            JobHandle initial = job.Schedule();

            JobHandle addedJob = addedDiffJob.Schedule(initial);
            JobHandle removedJob = removedDiffJob.Schedule(initial);

            finalJobHandle = JobHandle.CombineDependencies(addedJob, removedJob);
            currentlyExecuting = true;

            finalJobHandle.Complete();

            if (addedNodes.Length > 0 || removedNodes.Length > 0)
            {
                onOctreeChanged?.Invoke(ref addedNodes, ref removedNodes);
            }
        }
    }

    /*
    // Check if an AABB intersects the octree, and return a native list of the intersected leaf nodes (using an async job)
    public bool TryCheckAABBIntersection(Vector3 min, Vector3 max, ref NativeList<OctreeNode> output, out JobHandle handle)
    {

    }
    */

    // Dispose the octree memory
    internal override void Dispose()
    {
        targets.Dispose();
        pending.Dispose();
        addedNodes.Dispose();
        removedNodes.Dispose();
        qualityPointsNativeArray.Dispose();

        foreach (var item in octreeNodesBuffer)
        {
            item.Dispose();
        }
    }
}

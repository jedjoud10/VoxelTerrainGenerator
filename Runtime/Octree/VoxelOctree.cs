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
    private NativeHashSet<OctreeNode>[] octreeNodesHashSet;
    private NativeList<OctreeNode>[] octreeNodesList;
    private int lastIndex;

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

    private bool mustUpdate = false;

    // Is the octree free to calculate a diff nodes?
    public bool Free { get; private set; } = true;

    // Intialize octree memory
    internal override void Init()
    {
        VoxelUtils.MaxDepth = maxDepth;
        Free = true;
        targets = new NativeArray<OctreeTarget>(1, Allocator.Persistent);
        targets[0] = new OctreeTarget();

        octreeNodesHashSet = new NativeHashSet<OctreeNode>[2];
        octreeNodesList = new NativeList<OctreeNode>[2];

        for (int i = 0; i < 2; i++)
        {
            octreeNodesHashSet[i] = new NativeHashSet<OctreeNode>(1, Allocator.Persistent);
            octreeNodesList[i] = new NativeList<OctreeNode>(Allocator.Persistent);
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
    public bool TryUpdateOctreeLoader(OctreeLoader loader)
    {
        if (!Free)
            return false;

        targets[0] = new OctreeTarget
        {
            generateCollisions = loader.generateCollisions,
            center = loader.transform.position,
            radius = loader.radius,
        };
        mustUpdate = true;
        return true;
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

            if (qualityPointsNativeArray.IsCreated)
            {
                for (int i = 0; i < maxDepth; i++)
                {
                    qualityPointsNativeArray[i] = curvePoints[i];
                }
            }
        }
    }

    // Loop over all the octree loaders and generate the octree for them
    void Update()
    {
        // Make sure we are free for octree generation
        if (terrain.Free && Free && mustUpdate)
        {
            mustUpdate = false;
            int index = lastIndex;
            lastIndex += 1;
            lastIndex = lastIndex % 2;

            // Ready up the allocations
            NativeList<OctreeNode> oldNodesList = octreeNodesList[1 - index];
            NativeList<OctreeNode> newNodesList = octreeNodesList[index];
            NativeHashSet<OctreeNode> oldNodesHashSet = octreeNodesHashSet[1 - index];
            NativeHashSet<OctreeNode> newNodesHashSet = octreeNodesHashSet[index];

            OctreeNode root = OctreeNode.RootNode(maxDepth - 1);
            pending.Clear();
            pending.Enqueue(root);
            newNodesList.Clear();
            newNodesList.Add(root);

            // Creates the octree
            SubdivideJob job = new SubdivideJob
            {
                targets = targets,
                nodes = newNodesList,
                pending = pending,
                qualityPoints = qualityPointsNativeArray,
            };

            // Handle scheduling the jobs
            JobHandle initial = job.Schedule();

            // We don't need to execute the neighbour job if we have skirts disabled
            JobHandle hashSetJobHandle;

            if (VoxelUtils.Skirts)
            {
                initial.Complete();

                // Temp copy of the added nodes
                var copy = new NativeArray<OctreeNode>(newNodesList.Length, Allocator.TempJob);
                copy.CopyFrom(newNodesList.AsArray());

                // Execute the neighbour checking job for added nodes
                NeighbourJob neighbourJob = new NeighbourJob
                {
                    octreeLoaderPosition = targets[0].center,
                    inputNodes = copy,
                    outputNodes = newNodesList.AsArray(),
                };

                JobHandle neighbourJobHandle = neighbourJob.Schedule(newNodesList.Length, 128);
                copy.Dispose(neighbourJobHandle);

                // Converts the array into a hashlist
                ToHashSetJob hashSetJob = new ToHashSetJob
                {
                    oldNodesList = oldNodesList,
                    oldNodesHashSet = oldNodesHashSet,
                    newNodesList = newNodesList,
                    newNodesHashSet = newNodesHashSet,
                };

                hashSetJobHandle = hashSetJob.Schedule(neighbourJobHandle);
            } else
            {
                // Converts the array into a hashlist
                ToHashSetJob hashSetJob = new ToHashSetJob
                {
                    oldNodesList = oldNodesList,
                    oldNodesHashSet = oldNodesHashSet,
                    newNodesList = newNodesList,
                    newNodesHashSet = newNodesHashSet,
                };

                hashSetJobHandle = hashSetJob.Schedule(initial);
            }

            // Job to check what we added
            DiffJob addedDiffJob = new DiffJob
            {
                oldNodesHashSet = oldNodesHashSet,
                newNodesHashSet = newNodesHashSet,
                diffedNodes = removedNodes,
            };

            // Job to check what we removed
            DiffJob removedDiffJob = new DiffJob
            {
                oldNodesHashSet = newNodesHashSet,
                newNodesHashSet = oldNodesHashSet,
                diffedNodes = addedNodes,
            };

            JobHandle addedJob = addedDiffJob.Schedule(hashSetJobHandle);
            JobHandle removedJob = removedDiffJob.Schedule(hashSetJobHandle);
            finalJobHandle = JobHandle.CombineDependencies(addedJob, removedJob, initial);
            Free = false;
        }

        if (!Free && finalJobHandle.IsCompleted)
        {
            // Complete immediately
            finalJobHandle.Complete();

            if (addedNodes.Length > 0 || removedNodes.Length > 0)
            {
                onOctreeChanged?.Invoke(ref addedNodes, ref removedNodes);
            }

            Free = true;
        } 
    }

    // Check if an AABB intersects the octree, and return a native list of the intersected leaf nodes
    // Returns false if the octree is currently updating (and thus cannot handle the request)
    public bool TryCheckAABBIntersection(Vector3 min, Vector3 max, out NativeList<OctreeNode>? output)
    {
        NativeQueue<int> pendingQueue = new NativeQueue<int>(Allocator.TempJob);
        pendingQueue.Enqueue(0);
        NativeList<OctreeNode> intersectLeafs = new NativeList<OctreeNode>(Allocator.TempJob);

        var handle = new IntersectJob
        {
            min = min,
            max = max,
            pending = pendingQueue,
            nodes = octreeNodesList[1 - lastIndex],
            intersectLeafs = intersectLeafs
        }.Schedule();

        pendingQueue.Dispose(handle);

        handle.Complete();

        output = intersectLeafs;
        return intersectLeafs.Length > 0;
    }

    // Dispose the octree memory
    internal override void Dispose()
    {
        targets.Dispose();
        pending.Dispose();
        addedNodes.Dispose();
        removedNodes.Dispose();
        qualityPointsNativeArray.Dispose();

        for (int i = 0; i < 2; i++)
        {
            octreeNodesHashSet[i].Dispose();
            octreeNodesList[i].Dispose();
        }
    }
}

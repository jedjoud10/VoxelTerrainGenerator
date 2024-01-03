using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UnityEditor.PlayerSettings;

// Handles generating the octree for all octree loaders and creating the octree and detecting the delta
public class VoxelOctree : VoxelBehaviour {
    // Max depth of the octree
    [Min(1)]
    public int maxDepth = 8;
    public bool debugGizmos = false;

    // Custom octree subdivider script
    public IOctreeSubdivider subdivider;

    private NativeList<OctreeTarget> targets;
    private Dictionary<OctreeLoader, int> targetsLookup;

    // Native hashmap for keeping track of the current nodes in the tree
    private NativeHashSet<OctreeNode>[] octreeNodesHashSet;
    private NativeList<OctreeNode>[] octreeNodesList;
    private int lastIndex;

    // Native arrays to diff the octree nodes
    private NativeList<OctreeNode> addedNodes;
    private NativeList<OctreeNode> removedNodes;

    // Used to generate the octree
    private NativeQueue<OctreeNode> pending;
    private NativeList<OctreeNode> copy;

    // Called whenever we detect a change in the octree
    public delegate void OnOctreeChanged(ref NativeList<OctreeNode> added, ref NativeList<OctreeNode> removed);
    public event OnOctreeChanged onOctreeChanged;

    // Final job handle that we must wait for
    JobHandle finalJobHandle;

    private bool mustUpdate = false;

    // Is the octree free to calculate a diff nodes?
    public bool Free { get; private set; } = true;

    // Intialize octree memory
    internal override void Init() {
        subdivider = new DefaultOctreeSubdivider();
        VoxelUtils.MaxDepth = maxDepth;
        Free = true;
        targets = new NativeList<OctreeTarget>(Allocator.Persistent);
        targetsLookup = new Dictionary<OctreeLoader, int>();

        octreeNodesHashSet = new NativeHashSet<OctreeNode>[2];
        octreeNodesList = new NativeList<OctreeNode>[2];

        for (int i = 0; i < 2; i++) {
            octreeNodesHashSet[i] = new NativeHashSet<OctreeNode>(1, Allocator.Persistent);
            octreeNodesList[i] = new NativeList<OctreeNode>(Allocator.Persistent);
        }

        pending = new NativeQueue<OctreeNode>(Allocator.Persistent);

        addedNodes = new NativeList<OctreeNode>(Allocator.Persistent);
        removedNodes = new NativeList<OctreeNode>(Allocator.Persistent);
        copy = new NativeList<OctreeNode>(0, Allocator.Persistent);
    }

    private void OnValidate() {
        if (terrain == null) {
            VoxelUtils.MaxDepth = maxDepth;
        }
    }

    // Force the octree to update due to an octree loader moving
    // Returns true if the octree successfully updated the location of the loader
    public bool TryUpdateOctreeLoader(OctreeLoader loader) {
        if (!Free)
            return false;

        if (!targetsLookup.ContainsKey(loader)) {
            targetsLookup.Add(loader, targets.Length);
            targets.Add(new OctreeTarget {
                generateCollisions = false,
                center = default,
                radius = 0.0f,
            });
        }

        int index = targetsLookup[loader];
        targets[index] = new OctreeTarget {
            generateCollisions = loader.generateCollisions,
            center = loader.transform.position,
            radius = loader.radius,
        };

        mustUpdate = true;
        return true;
    }

    // Loop over all the octree loaders and generate the octree for them
    void Update() {
        // Make sure we are free for octree generation
        if (terrain.Free && Free && mustUpdate) {
            mustUpdate = false;
            int index = lastIndex;
            lastIndex += 1;
            lastIndex %= 2;

            // Ready up the allocations
            NativeList<OctreeNode> oldNodesList = octreeNodesList[1 - index];
            NativeList<OctreeNode> newNodesList = octreeNodesList[index];
            NativeHashSet<OctreeNode> oldNodesHashSet = octreeNodesHashSet[1 - index];
            NativeHashSet<OctreeNode> newNodesHashSet = octreeNodesHashSet[index];

            OctreeNode root = OctreeNode.RootNode(maxDepth);
            pending.Clear();
            pending.Enqueue(root);
            newNodesList.Clear();
            newNodesList.Add(root);

            // Creates the octree
            JobHandle initial = subdivider.Apply(targets.AsArray(), newNodesList, pending);

            // We don't need to execute the neighbour job if we have skirts disabled
            JobHandle hashSetJobHandle;

            if (VoxelUtils.Skirts) {
                initial.Complete();

                // Temp copy of the added nodes
                copy.Resize(Mathf.Max(newNodesList.Length, copy.Length), NativeArrayOptions.ClearMemory);
                copy.CopyFrom(newNodesList);

                // Execute the neighbour checking job for added nodes
                NeighbourJob neighbourJob = new NeighbourJob {
                    octreeLoaderPosition = targets[0].center,
                    inputNodes = copy.AsArray(),
                    outputNodes = newNodesList.AsArray(),
                };

                JobHandle neighbourJobHandle = neighbourJob.Schedule(newNodesList.Length, 128);
                

                // Converts the array into a hashlist
                ToHashSetJob hashSetJob = new ToHashSetJob {
                    oldNodesList = oldNodesList,
                    oldNodesHashSet = oldNodesHashSet,
                    newNodesList = newNodesList,
                    newNodesHashSet = newNodesHashSet,
                };

                hashSetJobHandle = hashSetJob.Schedule(neighbourJobHandle);
            } else {
                // Converts the array into a hashlist
                ToHashSetJob hashSetJob = new ToHashSetJob {
                    oldNodesList = oldNodesList,
                    oldNodesHashSet = oldNodesHashSet,
                    newNodesList = newNodesList,
                    newNodesHashSet = newNodesHashSet,
                };

                hashSetJobHandle = hashSetJob.Schedule(initial);
            }

            // Job to check what we added
            DiffJob addedDiffJob = new DiffJob {
                oldNodesHashSet = oldNodesHashSet,
                newNodesHashSet = newNodesHashSet,
                diffedNodes = removedNodes,
            };

            // Job to check what we removed
            DiffJob removedDiffJob = new DiffJob {
                oldNodesHashSet = newNodesHashSet,
                newNodesHashSet = oldNodesHashSet,
                diffedNodes = addedNodes,
            };

            JobHandle addedJob = addedDiffJob.Schedule(hashSetJobHandle);
            JobHandle removedJob = removedDiffJob.Schedule(hashSetJobHandle);
            finalJobHandle = JobHandle.CombineDependencies(addedJob, removedJob, initial);
            Free = false;
        }

        if (!Free && finalJobHandle.IsCompleted) {
            // Complete immediately
            finalJobHandle.Complete();

            if (addedNodes.Length > 0 || removedNodes.Length > 0) {
                onOctreeChanged?.Invoke(ref addedNodes, ref removedNodes);
            }

            Free = true;
        }
    }

    // Check if an AABB intersects the octree, and return a native list of the intersected leaf nodes
    // Returns false if the octree is currently updating (and thus cannot handle the request)
    public bool TryCheckAABBIntersection(Bounds bounds, out NativeList<OctreeNode>? output) {
        NativeQueue<int> pendingQueue = new NativeQueue<int>(Allocator.TempJob);
        pendingQueue.Enqueue(0);
        NativeList<OctreeNode> intersectLeafs = new NativeList<OctreeNode>(Allocator.TempJob);

        var handle = new IntersectJob {
            min = bounds.min,
            max = bounds.max,
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
    internal override void Dispose() {
        targets.Dispose();
        pending.Dispose();
        addedNodes.Dispose();
        removedNodes.Dispose();
        copy.Dispose();

        for (int i = 0; i < 2; i++) {
            octreeNodesHashSet[i].Dispose();
            octreeNodesList[i].Dispose();
        }
    }

    private void OnDrawGizmosSelected() {
        if (terrain != null && debugGizmos && terrain.Free && Free && !mustUpdate) {
            NativeList<OctreeNode> nodes = octreeNodesList[1 - lastIndex];

            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            foreach (var node in nodes) {
                Gizmos.DrawWireCube(node.Center, node.Size * Vector3.one);
            }
        }
    }
}

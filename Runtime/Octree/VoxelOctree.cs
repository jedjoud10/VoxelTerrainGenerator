using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Handles generating the octree for all octree loaders and creating the octree and detecting the delta
public class VoxelOctree : VoxelBehaviour
{
    // Max number of targets supported
    [Min(1)]
    public int maxTargetCount = 1;

    // Max depth of the octree
    [Min(1)]
    public int maxDepth = 8;

    // Min depth at wich we must generate the nodes
    [Min(0)]
    public int minNodeGenerationDepth = 0;

    // Factor for distance scaling
    [Min(1.0F)]
    public float lodDistanceScaling = 1.0F;

    // Should we draw gizmoss for the octree or not?
    public bool drawGizmos = false;

    // List of the targets
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

    internal override void Init()
    {
        generator = GetComponent<VoxelGenerator>();
        mesher = GetComponent<VoxelMesher>();

        targets = new NativeArray<OctreeTarget>(maxTargetCount, Allocator.Persistent);
        for (int i = 0; i < maxTargetCount; i++)
        {
            targets[i] = new OctreeTarget();
        }

        octreeNodesBuffer = new NativeHashSet<OctreeNode>[2];

        for (int i = 0; i < 2; i++)
        {
            octreeNodesBuffer[i] = new NativeHashSet<OctreeNode>(1, Allocator.Persistent);
        }
        
        pending = new NativeQueue<OctreeNode>(Allocator.Persistent);

        addedNodes = new NativeList<OctreeNode>(Allocator.Persistent);
        removedNodes = new NativeList<OctreeNode>(Allocator.Persistent);
    }

    // Loop over all the octree loaders and generate the octree for them
    void Update()
    {
        OctreeLoader[] loaders = FindObjectsByType<OctreeLoader>(FindObjectsSortMode.None);
        float offset = (float)VoxelUtils.Size * VoxelUtils.VoxelSize;
        for (int i = 0; i < Mathf.Min(targets.Length, loaders.Length); i++)
        {
            targets[i] = new OctreeTarget
            {
                generateCollisions = loaders[i].generateCollisions,
                center = loaders[i].transform.position / offset,
                radius = loaders[i].radius / offset,
                lodMultiplier = loaders[i].lodMultiplier,
            };
        }

        bool free = mesher.MeshGenerationTasksRemaining == 0 && generator.VoxelGenerationTasksRemaining == 0 && mesher.CollisionBakingTasksRemaining == 0;
        if (finalJobHandle.IsCompleted && free)
        {
            if (addedNodes.Length > 0 || removedNodes.Length > 0)
            {
                onOctreeChanged(ref addedNodes, ref removedNodes);
            }

            int index = currentIndex;
            currentIndex += 1;
            currentIndex = currentIndex % 2;

            // Ready up the allocations
            NativeHashSet<OctreeNode> oldNodes = octreeNodesBuffer[1 - index];
            NativeHashSet<OctreeNode> nodes = octreeNodesBuffer[index];
            pending.Enqueue(OctreeNode.RootNode(maxDepth));

            SubdivideJob job = new SubdivideJob
            {
                targets = targets,
                nodes = nodes,
                pending = pending,
                globalLodMultiplier = lodDistanceScaling,
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
            finalJobHandle.Complete();
        } else
        {

        }
    }

    // Dispose the octree memory
    internal override void Dispose()
    {
        targets.Dispose();
        pending.Dispose();
        addedNodes.Dispose();
        removedNodes.Dispose();
    }

    void OnDrawGizmosSelected()
    {
        if (!targets.IsCreated || !drawGizmos)
        {
            return;
        }

        foreach (var item in octreeNodesBuffer[currentIndex])
        {
            if (item.leaf)
            {
                float color = (float)(maxDepth - item.invDepth) / (float)maxDepth;
                Gizmos.color = new Color(color, color, color, color);
                Vector3 position = item.WorldCenter();
                Vector3 size = item.WorldSize();
                Gizmos.DrawWireCube(position, size);
            }
        }
    }
}

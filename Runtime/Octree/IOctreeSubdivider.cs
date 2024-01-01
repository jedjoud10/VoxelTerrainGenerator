using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

// Custom octree subdivier to allow end users to handle custom octree subdivision logic
public interface IOctreeSubdivider {
}
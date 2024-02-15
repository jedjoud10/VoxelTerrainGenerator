using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static TestTerrainEdit;

// Allows us to modify how props will spawn in a specific region
// Could allow us to only allow specific props to spawn within the bounds specified, or to NOT let 
// them spawn in the bounds specified
// Wheneer a new modifier is spawned in the world, we will recompute the internal list and resend it to the GPU
// WE will recompute the compute buffer every time after we sort the data
public class VoxelPropsSpawnModifier : MonoBehaviour {
    [Flags]
    public enum SpawnRulesetModifier {
        None = 0,
        InsideBounds = 1,
        OutsideBounds = 2,
    }

    public int priority;
    [Min(0)]
    public int propType = 0;
    [Min(0)]
    public int propVariant = 0;
    public SpawnRulesetModifier modifer = SpawnRulesetModifier.InsideBounds | SpawnRulesetModifier.OutsideBounds;

    public struct BlittableSpawnModifier {
        public const int size = sizeof(float) * 4 * 2 + sizeof(int) + sizeof(byte) * 4;

        public float4 center;
        public float4 extent;

        public int priority;

        public byte propType;
        public byte propVariant;
        public byte modifier;
        public byte padding_;
    }

    internal BlittableSpawnModifier ConvertToBlittable() {
        return new BlittableSpawnModifier {
            center = new float4(transform.position, 0),
            extent = new float4(transform.lossyScale, 0),
            priority= this.priority,
            propType = (byte)this.propType,
            propVariant = (byte)this.propVariant,
            modifier = (byte)this.modifer,
        };
    }
    
    private void Start() {
        if (VoxelTerrain.Instance != null) {
            var terrain = VoxelTerrain.Instance;
            terrain.VoxelProps.modifiersHashSet.Add(this);
            terrain.VoxelProps.ResortSpawnModifiers();
        }
    }

    private void OnDestroy() {
        if (VoxelTerrain.Instance != null) {
            var terrain = VoxelTerrain.Instance;
            terrain.VoxelProps.modifiersHashSet.Remove(this);
            terrain.VoxelProps.ResortSpawnModifiers();
        }
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);
    }
}

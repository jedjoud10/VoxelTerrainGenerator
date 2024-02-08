using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static TestTerrainEdit;

// Allows us to modify how props will spawn in a specific region
// Could allow us to only allow specific props to spawn within the bounds specified, or to NOT let 
// them spawn in the bounds specified
public class VoxelPropsSpawnModifier : MonoBehaviour {
    [Flags]
    public enum SpawnRulesetModifier {
        None = 0,
        InsideBounds = 1,
        OutsideBounds = 2,
    }
    [Serializable]
    public class SpawnRuleset {
        [Min(0)]
        public int propType = 0;
        [Min(0)]
        public int propVariant = 0;
        public SpawnRulesetModifier modifer = SpawnRulesetModifier.InsideBounds | SpawnRulesetModifier.OutsideBounds;
    }
    public List<SpawnRuleset> ruleset;

    public struct BlittableSpawnRuleset {
        public byte propType;
        public byte propVariant;
        public byte modifier;
    }
    
    public struct BlittableSpawnModifier {
        public float4 center;
        public float4 extent;
        public uint ruleSetId;
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);
    }
}

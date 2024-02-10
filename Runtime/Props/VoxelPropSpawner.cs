using UnityEngine;

// Manual spawner that will allow us to place props manually in the world
public class VoxelPropSpawner : MonoBehaviour {
    public PropType propType;
    public int variant;
    bool applied = false;
    internal int propTypeIndex = -1;

    private void Start() {
        if (VoxelTerrain.Instance != null && !applied) {
            propTypeIndex = VoxelTerrain.Instance.VoxelProps.props.FindIndex((x) => x == propType);
            applied = true;
        }
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.gray;
        Gizmos.DrawCube(transform.position, 0.5f * Vector3.one);
    }
}
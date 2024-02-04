using Unity.Burst.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

// Sample terrain edit (either dynamic or voxel) that is applied when the terrain is first generated
public class TestTerrainEdit : MonoBehaviour {
    public enum EditType {
        CuboidDynamic,
        SphereDynamic,
        CuboidVoxel,
        SphereVoxel,
        AddVoxel,
    }

    public EditType type;
    private bool isApplied = false;
    public bool writeMaterial;
    public float strength;
    public byte material;

    private void Update() {
        if (!isApplied && VoxelTerrain.Instance != null && VoxelTerrain.Instance.Free && VoxelTerrain.Instance.FinishedInit) {
            isApplied = true;

            float3 center = math.float3(transform.position.x, transform.position.y, transform.position.z);
            float3 halfExtents = math.float3(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z) / 2f;
            float radius = transform.lossyScale.x;

            switch (type) {
                case EditType.CuboidDynamic:
                    VoxelTerrain.Instance.VoxelEdits.ApplyDynamicEdit(new CuboidDynamicEdit {
                        center = center,
                        halfExtents = halfExtents,
                        writeMaterial = writeMaterial,
                        material = material,
                    });
                    break;
                case EditType.SphereDynamic:
                    VoxelTerrain.Instance.VoxelEdits.ApplyDynamicEdit(new SphereDynamicEdit {
                        center = center,
                        radius = radius,
                        writeMaterial = writeMaterial,
                        material = material,
                    });
                    break;
                case EditType.CuboidVoxel:
                    VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(new CuboidVoxelEdit {
                        center = center,
                        halfExtents = halfExtents,
                        writeMaterial = writeMaterial,
                        material = material,
                        strength = strength,
                    }, true);
                    break;
                case EditType.SphereVoxel:
                    VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(new SphereVoxelEdit {
                        center = center,
                        radius = radius,
                        writeMaterial = writeMaterial,
                        material = material,
                        strength = strength,
                    }, true);
                    break;
                case EditType.AddVoxel:
                    VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(new AddVoxelEdit {
                        center = center,
                        radius = radius,
                        strength = strength,
                        writeMaterial = writeMaterial,
                        material = material,
                    }, true);
                    break;
            }
        }
    }

    private void OnDrawGizmos() {
        bool sphere = false;
        bool dynamic = false;

        // lord have mercy on the code
        switch (type) {
            case EditType.CuboidDynamic:
                dynamic = true;
                sphere = false;
                break;
            case EditType.SphereDynamic:
                dynamic = true;
                sphere = true;
                break;
            case EditType.CuboidVoxel:
                dynamic = false;
                sphere = false;
                break;
            case EditType.SphereVoxel:
                dynamic = true;
                sphere = true;
                break;
            case EditType.AddVoxel:
                dynamic = false;
                sphere = true;
                break;
            default:
                break;
        }


        Gizmos.color = dynamic ? Color.blue : Color.red;

        if (sphere) {
            Gizmos.DrawWireSphere(transform.position, transform.lossyScale.x);
        } else {
            Gizmos.DrawWireCube(transform.position, transform.lossyScale);
        }
    }
}
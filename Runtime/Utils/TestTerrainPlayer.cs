using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// Sample script for editing terrain, loading/saving, and modifying props
public class PlayerControllerScript : MonoBehaviour {
    public GameObject head;
    public GameObject indicator;
    private float sizeRadius = 1.0F;
    private byte[] savedBytes;

    private void Start() {
    }

    // Update is called once per frame
    void Update() {
        if (Physics.Raycast(head.transform.position, head.transform.forward * 2, out RaycastHit hit, 500.0F, LayerMask.NameToLayer("Player"))) {
            bool add = Input.GetKey(KeyCode.LeftShift);
            float temp = add ? -1F : 1F;

            if (Input.GetMouseButtonDown(0)) {
                var edit = new SphereDynamicEdit {
                    center = math.float3(hit.point.x, hit.point.y, hit.point.z),
                    radius = sizeRadius,
                    writeMaterial = add,
                    material = 2,
                };

                VoxelTerrain.Instance.VoxelEdits.ApplyDynamicEdit(edit);
            }

            if (Input.GetMouseButtonDown(1)) {
                var edit = new AddVoxelEdit {
                    center = math.float3(hit.point.x, hit.point.y, hit.point.z),
                    radius = sizeRadius,
                    strength = 10.0F * temp,
                    writeMaterial = true,
                    material = 2,
                };

                VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(edit);
            }

            indicator.transform.position = Vector3.Lerp(indicator.transform.position, hit.point, 13.25F * Time.deltaTime);
        } else {
            indicator.transform.position = Vector3.Lerp(indicator.transform.position, head.transform.forward * 200.0F + head.transform.position, 13.25F * Time.deltaTime);
        }

        indicator.transform.localScale = Vector3.one * sizeRadius * 2;

        sizeRadius += Input.mouseScrollDelta.y * Time.deltaTime * 45.0F;
        sizeRadius = Mathf.Clamp(sizeRadius, 0, 500);

        if (Input.GetKeyDown(KeyCode.V)) {
            FastBufferWriter writer = new FastBufferWriter(1024 * 1024 * 5, Allocator.Persistent);
            VoxelTerrain.Instance.Serialize(ref writer);
            savedBytes = writer.ToArray();
            writer.Dispose();
        } else if (Input.GetKeyDown(KeyCode.B)) {
            FastBufferReader reader = new FastBufferReader(savedBytes, Allocator.Persistent);
            VoxelTerrain.Instance.Deserialize(ref reader);
            reader.Dispose();
        }
    }
}
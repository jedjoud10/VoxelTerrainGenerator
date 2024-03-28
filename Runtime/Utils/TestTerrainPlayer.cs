using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// Sample script for editing terrain, loading/saving, and modifying props
public class PlayerControllerScript : MonoBehaviour {
    public GameObject head;
    public GameObject indicator;
    private float sizeRadius = 1.0F;
    private VoxelEdits.VoxelEditCountersHandle handle; 
    private int target = 0;

    private void Start() {
    }

    // Update is called once per frame
    void Update() {
        if (Physics.Raycast(head.transform.position, head.transform.forward * 2, out RaycastHit hit, 500.0F, LayerMask.NameToLayer("Player"))) {
            bool add = Input.GetKey(KeyCode.LeftShift);
            float temp = add ? -1F : 1F;
            target += Input.GetKeyDown(KeyCode.K) ? 1 : 0;
            target = target % 3;
            
            if (Input.GetMouseButtonDown(0)) {
                switch (target) {
                    case 0:
                        var edit = new FlattenVoxelEdit {
                            center = hit.point,
                            normal = hit.normal,
                            radius = sizeRadius,
                            strength = temp * 10,
                        };

                        handle = VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(edit);
                        break;
                    case 1:
                        var edit2 = new AddVoxelEdit {
                            center = math.float3(hit.point.x, hit.point.y, hit.point.z),
                            radius = sizeRadius,
                            strength = 10.0F * temp,
                            writeMaterial = true,
                            material = 2,
                        };

                        handle = VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(edit2);
                        break;
                    case 2:
                        var edit3 = new SphereVoxelEdit {
                            center = math.float3(hit.point.x, hit.point.y, hit.point.z),
                            radius = sizeRadius,
                            strength = 10.0F * temp,
                            writeMaterial = true,
                            material = 2,
                        };

                        handle = VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(edit3);
                        break;
                }
            }

            if (Input.GetMouseButton(1)) {
                var edit2 = new AddVoxelEdit {
                    center = math.float3(hit.point.x, hit.point.y, hit.point.z),
                    radius = sizeRadius,
                    strength = 1.0F * temp,
                    writeMaterial = true,
                    material = 2,
                };

                handle = VoxelTerrain.Instance.VoxelEdits.ApplyVoxelEdit(edit2);
            }

            indicator.transform.position = Vector3.Lerp(indicator.transform.position, hit.point, 13.25F * Time.deltaTime);
            indicator.transform.rotation = Quaternion.Lerp(indicator.transform.rotation, Quaternion.LookRotation(hit.normal), 13.25F * Time.deltaTime);
        } else {
            indicator.transform.position = Vector3.Lerp(indicator.transform.position, head.transform.forward * 200.0F + head.transform.position, 13.25F * Time.deltaTime);
        }

        indicator.transform.localScale = Vector3.one * sizeRadius * 2;

        sizeRadius += Input.mouseScrollDelta.y * Time.deltaTime * 45.0F;
        sizeRadius = Mathf.Clamp(sizeRadius, 0, 500);

        if (handle != null) {
            if (handle.pending == 0) {
                Debug.Log(handle.added);
                Debug.Log(handle.removed);
                handle = null;
            }
        }

        if (Input.GetKeyDown(KeyCode.V)) {
            string path = Application.persistentDataPath + "/terrain.world";
            FastBufferWriter writer = new FastBufferWriter(1024 * 1024 * 5, Allocator.Persistent);
            VoxelTerrain.Instance.Serialize(ref writer);
            byte[] bytes = writer.ToArray();

            File.WriteAllBytes(path, bytes);
            Debug.Log("Saved the file to path: " + path);
            writer.Dispose();
        } else if (Input.GetKeyDown(KeyCode.B)) {
            string path = Application.persistentDataPath + "/terrain.world";
            byte[] bytes = File.ReadAllBytes(path);
            FastBufferReader reader = new FastBufferReader(bytes, Allocator.Persistent);
            VoxelTerrain.Instance.Deserialize(ref reader);
            reader.Dispose();
        }
    }
}
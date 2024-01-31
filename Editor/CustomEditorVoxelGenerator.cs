using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelGenerator))]
public class CustomEditorVoxelGenerator : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        VoxelGenerator generator = (VoxelGenerator)target;

        GUI.enabled = generator.Free;

        if (GUILayout.Button("Update Statics & Regenerate")) {
            generator.UpdateStaticComputeFields();
            generator.GetComponent<VoxelTerrain>().RequestAll(true);
            generator.GetComponent<VoxelProps>().RegenerateProps();
        }
        GUI.enabled = true;
    }
}
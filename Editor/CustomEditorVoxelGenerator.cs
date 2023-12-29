using System.Collections;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelGenerator))]
public class CustomEditorVoxelGenerator : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        VoxelGenerator generator = (VoxelGenerator)target;

        GUI.enabled = generator.Free;
        if (GUILayout.Button("Randomize Seeds & Regenerate")) {
            generator.RandomizeSeeds();
            generator.UpdateStaticComputeFields();
            generator.GetComponent<VoxelTerrain>().RequestAll(true);
        }

        if (GUILayout.Button("Update Static Compute Fields & Regenerate")) {
            generator.UpdateStaticComputeFields();
            generator.GetComponent<VoxelTerrain>().RequestAll(true);
        }
        GUI.enabled = true;
    }
}
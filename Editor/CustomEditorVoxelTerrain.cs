using UnityEngine;
using UnityEditor;
using Palmmedia.ReportGenerator.Core;
using System.Reflection.Emit;

[CustomEditor(typeof(VoxelTerrain))]
public class CustomEditorVoxelTerrain : Editor {
    public override bool RequiresConstantRepaint() {
        return true;
    }

    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        float voxelSize = VoxelUtils.VoxelSizeFactor;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Voxel Size: " + voxelSize + "m");

        VoxelTerrain terrain = ((VoxelTerrain)target);
        GUI.enabled = terrain.Free;
        if (GUILayout.Button("Regenerate")) {
            terrain.VoxelGenerator.UpdateStaticComputeFields();
            terrain.VoxelProps.UpdateStaticComputeFields();
            terrain.VoxelProps.RegenerateProps();
            terrain.RequestAll(true);
        }
        if (GUILayout.Button("Remesh")) {
            terrain.RequestAll(false);
        }
        GUI.enabled = true;
    }
}
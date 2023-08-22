using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelTerrain))]
public class CustomEditorVoxelTerrain : Editor
{
    public override bool RequiresConstantRepaint()
    {
        return true;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        float voxelSize = VoxelUtils.VoxelSizeFactor;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Voxel Size: " + voxelSize + "m");

        VoxelTerrain terrain = ((VoxelTerrain)target);

        //EditorGUILayout.LabelField("Mesh Tasks Remaining: " + mesher.MeshGenerationTasksRemaining);
        //EditorGUILayout.LabelField("Collision Tasks Remaining: " + mesher.CollisionBakingTasksRemaining);
        //EditorGUILayout.LabelField("Generator Tasks Remaining: " + generator.VoxelGenerationTasksRemaining);

        GUI.enabled = terrain.Free;
        if (GUILayout.Button("Regenerate"))
        {
            terrain.RequestAll();
        }
        GUI.enabled = true;
    }
}
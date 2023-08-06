using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GraphProcessor;
using UnityEditor.Callbacks;
using System.IO;
using Palmmedia.ReportGenerator.Core;
using UnityEngine.XR;

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

        float voxelSize = VoxelUtils.VoxelSize;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Voxel Size: " + voxelSize + "m");

        VoxelTerrain terrain = ((VoxelTerrain)target);
        VoxelMesher mesher = terrain.GetComponent<VoxelMesher>();
        VoxelGenerator generator = terrain.GetComponent<VoxelGenerator>();

        EditorGUILayout.LabelField("Mesh Tasks Remaining: " + mesher.MeshGenerationTasksRemaining);
        EditorGUILayout.LabelField("Collision Tasks Remaining: " + mesher.CollisionBakingTasksRemaining);
        EditorGUILayout.LabelField("Generator Tasks Remaining: " + generator.VoxelGenerationTasksRemaining);
    }
}
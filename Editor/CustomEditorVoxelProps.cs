using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelProps))]
public class CustomEditorVoxelProps : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        VoxelProps props = (VoxelProps)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global Prop Chunk Count (one axis): " + VoxelUtils.PropChunkCount);
    }
}
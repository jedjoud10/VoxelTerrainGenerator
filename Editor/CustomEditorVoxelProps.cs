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
        EditorGUILayout.LabelField("Global Prop Segment Count: " + VoxelUtils.PropSegmentsCount);
        EditorGUILayout.LabelField("Prop Segment Size: " + VoxelUtils.PropSegmentSize);
    }
}
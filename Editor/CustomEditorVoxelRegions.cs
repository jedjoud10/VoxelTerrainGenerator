using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelRegions))]
public class CustomEditorVoxelRegions : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        VoxelRegions regions = (VoxelRegions)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global Prop Segment Count: " + VoxelUtils.PropSegmentsCount);
        EditorGUILayout.LabelField("Prop Segment Size: " + VoxelUtils.PropSegmentSize);
    }
}
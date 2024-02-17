using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelSegments))]
public class CustomEditorVoxelRegions : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        VoxelSegments regions = (VoxelSegments)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global Prop Segment Count: " + VoxelUtils.PropSegmentsCount);
        EditorGUILayout.LabelField("Prop Segment Size: " + VoxelUtils.PropSegmentSize);
    }
}
using System.Collections;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelOctree))]
public class CustomEditorVoxelOctree : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        VoxelOctree octree = (VoxelOctree)target;
        float maxSize = Mathf.Pow(2F, (float)octree.maxDepth-1) * VoxelUtils.VoxelSizeFactor * VoxelUtils.Size;
        float segments = Mathf.CeilToInt(maxSize / (VoxelUtils.Size * 4));
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global World Size: " + maxSize + "m³");
        EditorGUILayout.LabelField("Global World Segments: " + segments + "³");
    }
}
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VoxelOctree))]
public class CustomEditorVoxelOctree : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        VoxelOctree octree = (VoxelOctree)target;
        float maxSize = Mathf.Pow(2F, (float)octree.maxDepth) * VoxelUtils.VoxelSizeFactor * VoxelUtils.Size;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global World Size: " + maxSize + "m");
        EditorGUILayout.LabelField("Global World Volume: " + maxSize * maxSize * maxSize + "m³");
    }
}
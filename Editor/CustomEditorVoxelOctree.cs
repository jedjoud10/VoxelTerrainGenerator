using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GraphProcessor;
using UnityEditor.Callbacks;
using System.IO;

[CustomEditor(typeof(VoxelOctree))]
public class CustomEditorVoxelOctree : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }
}
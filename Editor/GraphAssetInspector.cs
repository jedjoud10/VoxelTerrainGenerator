using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GraphProcessor;
using UnityEngine.UIElements;

[CustomEditor(typeof(BaseGraph), true)]
public class GraphAssetInspector : GraphInspector
{
	protected override void CreateInspector()
	{
		base.CreateInspector();

		root.Add(new Button(() => EditorWindow.GetWindow<VoxelGraphWindow>().InitializeGraph(target as BaseGraph))
		{
			text = "Open voxel graph window"
		});
	}
}

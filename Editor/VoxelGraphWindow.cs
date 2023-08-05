using UnityEngine;
using UnityEditor;
using GraphProcessor;

public class VoxelGraphWindow : BaseGraphWindow
{
    BaseGraph _graph;

    [MenuItem("Voxel Terrain/Voxel Graph")]
    public static BaseGraphWindow Open()
    {
        var graphWindow = GetWindow<VoxelGraphWindow>();
        graphWindow._graph = ScriptableObject.CreateInstance<BaseGraph>();
        graphWindow._graph.hideFlags = HideFlags.None;
        graphWindow._graph.AddNode(BaseNode.CreateFromType<OutputNode>(Vector2.zero));
        graphWindow.InitializeGraph(graphWindow._graph);
        graphWindow.Show();
        return graphWindow;
    }

    protected override void OnDestroy()
    {
        graphView?.Dispose();
        DestroyImmediate(_graph);
    }

    protected override void InitializeWindow(BaseGraph graph)
    {
        titleContent = new GUIContent("Voxel Graph");

        if (graphView == null)
            graphView = new BaseGraphView(this);

        rootView.Add(graphView);
    }
}
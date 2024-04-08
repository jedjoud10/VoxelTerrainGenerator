using UnityEngine;
using UnityEditor;
using UnityEngine.Experimental.Rendering;

[CustomEditor(typeof(VoxelGenerator))]
public class CustomEditorVoxelGenerator : Editor {
    RenderTexture previewTexture = null;

    private void OnSceneViewGUI(SceneView sv) {
        if (Application.isPlaying) return;

        VoxelGenerator generator = (VoxelGenerator)target;
        generator.UpdateStaticComputeFields();

        ComputeShader shader = generator.voxelShader;
        shader.SetVector("worldOffset", generator.previewWorldOffset);
        shader.SetVector("worldScale", generator.previewWorldScale * VoxelUtils.VoxelSizeFactor);
        shader.SetTexture(4, "previewVoxels", previewTexture);
        shader.SetVector("chunkOffset", Vector3.zero);
        shader.SetFloat("densityFactor", generator.previewDensityFactor);
        shader.SetFloat("chunkScale", 1.0f);

        // Generate the voxel data for the chunk
        int count = VoxelUtils.Size / 4;
        shader.Dispatch(4, count, count, count);

        Handles.matrix = Matrix4x4.Scale(Vector3.one * (float)VoxelUtils.Size);

        switch (generator.previewMode) {
            case VoxelGenerator.Preview3DMode.Volume:
                Handles.DrawTexture3DVolume(previewTexture, generator.previewOpacity, generator.previewQuality, useColorRamp: true, customColorRamp: generator.previewColorRampGradient);
                break;
            case VoxelGenerator.Preview3DMode.Slice:
                Handles.DrawTexture3DSlice(previewTexture, Vector3.zero);
                break;
            case VoxelGenerator.Preview3DMode.SDF:
                Handles.DrawTexture3DSDF(previewTexture, 0.1f);
                break;
        }
        
    }

    void OnEnable() {
        if (VoxelUtils.Size == 0)
            return;

        previewTexture = VoxelUtils.Create3DRenderTexture(VoxelUtils.Size, GraphicsFormat.R32_SFloat);
        SceneView.duringSceneGui += OnSceneViewGUI;
    }

    void OnDisable() {
        SceneView.duringSceneGui -= OnSceneViewGUI;
    }
}
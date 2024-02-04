using UnityEditor;

[CustomEditor(typeof(PropType))]
public class CustomEditorProp : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        /*
        var script = (Prop)target;

        if (script.propSpawnBehavior.HasFlag(PropSpawnBehavior.RenderBillboards)) {
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Billboard Capture", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            script.billboardCaptureCameraScale = EditorGUILayout.FloatField("Capture Camera Scale", script.billboardCaptureCameraScale);
            script.billboardTextureWidth = EditorGUILayout.IntField("Texture Width", script.billboardTextureWidth);
            script.billboardTextureHeight = EditorGUILayout.IntField("Texture Height", script.billboardTextureHeight);
            script.billboardCaptureRotation = EditorGUILayout.Vector3Field("Capture Rotation", script.billboardCaptureRotation);
            script.billboardCapturePosition = EditorGUILayout.Vector3Field("Capture Position", script.billboardCapturePosition);
            EditorGUI.indentLevel--;

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Billboard Rendering", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            script.billboardSize = EditorGUILayout.Vector2Field("World Size", script.billboardSize);
            script.billboardOffset = EditorGUILayout.Vector2Field("World Offset", script.billboardOffset);
            script.billboardRestrictRotationY = EditorGUILayout.Toggle("Restrict Rotation Y?", script.billboardRestrictRotationY);
            script.billboardCastShadows = EditorGUILayout.Toggle("Cast Shadows?", script.billboardCastShadows);
            script.billboardAlphaClipThreshold = EditorGUILayout.FloatField("Alpha Clip Threshold", script.billboardAlphaClipThreshold);
            EditorGUILayout.Separator();
        }
        */
    }
}
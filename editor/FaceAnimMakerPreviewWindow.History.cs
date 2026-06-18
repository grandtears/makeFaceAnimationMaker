using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // Unity の Undo ではなく、プレビュー用 BlendShape 値の配列を履歴として積む。
    // スライダー操作中は 1 回のドラッグを 1 つの Undo 単位にまとめる。

    private void DrawUndoRedoControls()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(blendShapeUndoStack.Count == 0);
        if (GUILayout.Button("Undo"))
            UndoPreviewBlendShapeChange();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(blendShapeRedoStack.Count == 0);
        if (GUILayout.Button("Redo"))
            RedoPreviewBlendShapeChange();
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Reset"))
            ResetPreviewBlendShapesToRendererDefaults();

        EditorGUILayout.EndHorizontal();
    }

    private void ClearBlendShapeHistory()
    {
        blendShapeUndoStack.Clear();
        blendShapeRedoStack.Clear();
        activeSliderUndoIndex = -1;
    }

    private void PushBlendShapeUndoState()
    {
        if (previewBlendShapeWeights == null || previewBlendShapeWeights.Length == 0)
            return;

        blendShapeUndoStack.Push((float[])previewBlendShapeWeights.Clone());
        blendShapeRedoStack.Clear();
    }

    private void UndoPreviewBlendShapeChange()
    {
        if (blendShapeUndoStack.Count == 0)
            return;

        activeSliderUndoIndex = -1;
        blendShapeRedoStack.Push((float[])previewBlendShapeWeights.Clone());
        previewBlendShapeWeights = blendShapeUndoStack.Pop();
        ApplyPreviewBlendShapeWeights();
        Repaint();
    }

    private void RedoPreviewBlendShapeChange()
    {
        if (blendShapeRedoStack.Count == 0)
            return;

        activeSliderUndoIndex = -1;
        blendShapeUndoStack.Push((float[])previewBlendShapeWeights.Clone());
        previewBlendShapeWeights = blendShapeRedoStack.Pop();
        ApplyPreviewBlendShapeWeights();
        Repaint();
    }

    private void ResetPreviewBlendShapesToRendererDefaults()
    {
        if (selectedRenderer == null || selectedRenderer.sharedMesh == null)
            return;

        activeSliderUndoIndex = -1;
        PushBlendShapeUndoState();
        InitializePreviewBlendShapeWeights();
        ApplyPreviewBlendShapeWeights();
        Repaint();
    }
}

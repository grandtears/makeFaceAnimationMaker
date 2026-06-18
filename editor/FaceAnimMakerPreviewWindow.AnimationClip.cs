using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // Preview 上で調整した BlendShape 値を表情用 .anim に保存し、既存 .anim から読み戻す。
    // vrc. で始まるリップシンク用 BlendShape は保存・表示で特別扱いする。

    private void DrawAnimationSaveControls()
    {
        // 現在 Preview で調整している BlendShape 値を書き込む表情用 AnimationClip。
        // 既存の .anim を指定して上書き保存することも、新規作成することもできる。
        EditorGUILayout.LabelField("Expression .anim", EditorStyles.boldLabel);

        expressionAnimationClip = (AnimationClip)EditorGUILayout.ObjectField(
            "Animation Clip",
            expressionAnimationClip,
            typeof(AnimationClip),
            false
        );

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Create .anim"))
            CreateExpressionAnimationClip();

        EditorGUI.BeginDisabledGroup(expressionAnimationClip == null);
        if (GUILayout.Button("Save Preview Values"))
            SavePreviewBlendShapesToAnimationClip();
        if (GUILayout.Button("Load .anim Values"))
            LoadBlendShapesFromAnimationClip();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    private void CreateExpressionAnimationClip()
    {
        // Project 内に新しい .anim を作成し、そのまま現在の Preview 値を保存する。
        string defaultName = selectedRenderer != null ? $"{selectedRenderer.name}_Expression.anim" : "Expression.anim";
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Expression Animation",
            defaultName,
            "anim",
            "保存先の .anim ファイルを指定してください。"
        );

        if (string.IsNullOrEmpty(path))
            return;

        AnimationClip clip = new AnimationClip
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path),
            frameRate = 60f
        };

        AssetDatabase.CreateAsset(clip, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        expressionAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        SavePreviewBlendShapesToAnimationClip();
    }

    private void SavePreviewBlendShapesToAnimationClip()
    {
        if (expressionAnimationClip == null || avatarRoot == null || selectedRenderer == null || selectedRenderer.sharedMesh == null)
            return;

        Mesh mesh = selectedRenderer.sharedMesh;

        // AnimationClip の path は Animator が付く Avatar Root から対象 Transform までの相対パス。
        // 例: Body がルート直下なら "Body"、階層下なら "Armature/Body" のようになる。
        string rendererPath = AnimationUtility.CalculateTransformPath(selectedRenderer.transform, avatarRoot.transform);

        Undo.RecordObject(expressionAnimationClip, "Save Face BlendShapes");

        // Preview の現在値を、SkinnedMeshRenderer の blendShape.xxx カーブとして保存する。
        // vrc. で始まるリップシンク用 BlendShape だけは、0 の値を .anim に書き出さない。
        // それ以外の BlendShape は、0 の値でも明示的な表情値として保存する。
        int blendShapeCount = Mathf.Min(mesh.blendShapeCount, previewBlendShapeWeights.Length);
        int savedCount = 0;
        for (int i = 0; i < blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);
            float weight = previewBlendShapeWeights[i];

            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                rendererPath,
                typeof(SkinnedMeshRenderer),
                $"blendShape.{shapeName}"
            );

            if (Mathf.Approximately(weight, 0f) && IsLipSyncBlendShape(shapeName))
            {
                AnimationUtility.SetEditorCurve(expressionAnimationClip, binding, null);
                continue;
            }

            // 表情クリップ用途なので、0 秒から 1 フレーム分だけ一定値のカーブにする。
            AnimationCurve curve = AnimationCurve.Constant(0f, 1f / 60f, weight);
            AnimationUtility.SetEditorCurve(expressionAnimationClip, binding, curve);
            savedCount++;
        }

        // Asset として保存し、Project 側の .anim に反映する。
        EditorUtility.SetDirty(expressionAnimationClip);
        AssetDatabase.SaveAssets();

        Debug.Log($"Saved {savedCount} BlendShape curves to {AssetDatabase.GetAssetPath(expressionAnimationClip)}");
    }

    private static bool IsLipSyncBlendShape(string shapeName)
    {
        return !string.IsNullOrEmpty(shapeName)
            && shapeName.StartsWith("vrc.", System.StringComparison.OrdinalIgnoreCase);
    }

    private void LoadBlendShapesFromAnimationClip()
    {
        if (expressionAnimationClip == null || avatarRoot == null || selectedRenderer == null || selectedRenderer.sharedMesh == null)
            return;

        Mesh mesh = selectedRenderer.sharedMesh;

        // 保存時と同じ Avatar Root からの相対パスで、対象 renderer のカーブだけを読む。
        string rendererPath = AnimationUtility.CalculateTransformPath(selectedRenderer.transform, avatarRoot.transform);

        if (previewBlendShapeWeights.Length != mesh.blendShapeCount)
            InitializePreviewBlendShapeWeights();

        activeSliderUndoIndex = -1;
        PushBlendShapeUndoState();

        for (int i = 0; i < previewBlendShapeWeights.Length; i++)
            previewBlendShapeWeights[i] = 0f;

        int loadedCount = 0;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                rendererPath,
                typeof(SkinnedMeshRenderer),
                $"blendShape.{shapeName}"
            );

            AnimationCurve curve = AnimationUtility.GetEditorCurve(expressionAnimationClip, binding);
            if (curve == null || curve.length == 0)
                continue;

            // 表情用 clip は一定値カーブ想定だが、通常のカーブでも 0 秒時点の値を読み込む。
            previewBlendShapeWeights[i] = curve.Evaluate(0f);
            loadedCount++;
        }

        ApplyPreviewBlendShapeWeights();
        Repaint();

        Debug.Log($"Loaded {loadedCount} BlendShape curves from {AssetDatabase.GetAssetPath(expressionAnimationClip)}");
    }
}

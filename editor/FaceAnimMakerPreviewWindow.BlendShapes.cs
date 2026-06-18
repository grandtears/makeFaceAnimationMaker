using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // BlendShape の検索、グループ Foldout、非ゼロ表示、スライダー描画を担当する。
    // vrc. で始まるリップシンク用 BlendShape は一覧に出さない。

    private void DrawBlendShapeSliders()
    {
        Mesh mesh = selectedRenderer.sharedMesh;
        int blendShapeCount = mesh.blendShapeCount;

        EditorGUILayout.LabelField($"BlendShapes: {blendShapeCount}", EditorStyles.boldLabel);

        // 区切り文字を変更すると、グループ名の判定結果が変わる。
        // そのため Foldout の開閉状態はリセットする。
        EditorGUI.BeginChangeCheck();
        blendShapeGroupDelimiter = EditorGUILayout.TextField("Foldout Delimiter", blendShapeGroupDelimiter);
        if (EditorGUI.EndChangeCheck())
            blendShapeFoldouts.Clear();

        // BlendShape 名の部分一致検索。
        // 大文字小文字は MatchesBlendShapeSearch 側で無視する。
        EditorGUILayout.BeginHorizontal();
        blendShapeSearchText = EditorGUILayout.TextField("Search", blendShapeSearchText);

        string nonZeroFilterButtonLabel = showOnlyNonZeroBlendShapes
            ? "Show All BlendShapes"
            : "Show Non-Zero BlendShapes";
        if (GUILayout.Button(nonZeroFilterButtonLabel, GUILayout.Width(180)))
            showOnlyNonZeroBlendShapes = !showOnlyNonZeroBlendShapes;

        EditorGUILayout.EndHorizontal();

        if (blendShapeCount == 0)
        {
            EditorGUILayout.HelpBox("This mesh has no BlendShapes.", MessageType.Info);
            return;
        }

        sliderScroll = EditorGUILayout.BeginScrollView(sliderScroll);

        string currentGroupKey = null;
        bool currentGroupOpen = true;
        bool hasSearch = !string.IsNullOrWhiteSpace(blendShapeSearchText);
        bool hasActiveFilter = hasSearch || showOnlyNonZeroBlendShapes;

        // BlendShape を上から走査する。
        // 区切り文字だけで構成された見出し名を見つけたら、以降をそのグループとして扱う。
        for (int i = 0; i < blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);

            if (TryGetBlendShapeGroupName(shapeName, out string groupName))
            {
                currentGroupKey = groupName;

                // 検索中は、一致する項目を含まないグループ見出しを表示しない。
                if (hasActiveFilter && !GroupHasSearchMatch(mesh, i + 1, blendShapeCount))
                {
                    currentGroupOpen = false;
                    continue;
                }

                currentGroupOpen = DrawBlendShapeGroupFoldout(currentGroupKey);
                continue;
            }

            if (!ShouldShowBlendShape(shapeName, i))
                continue;

            // 検索していないときは Foldout の開閉状態に従う。
            // 検索中は一致した項目を見逃さないよう、閉じていても表示する。
            if (!string.IsNullOrEmpty(currentGroupKey) && !currentGroupOpen && !hasActiveFilter)
                continue;

            EditorGUI.indentLevel += string.IsNullOrEmpty(currentGroupKey) ? 0 : 1;
            DrawBlendShapeSlider(i, shapeName);
            EditorGUI.indentLevel -= string.IsNullOrEmpty(currentGroupKey) ? 0 : 1;
        }

        EditorGUILayout.EndScrollView();
    }

    private bool TryGetBlendShapeGroupName(string shapeName, out string groupName)
    {
        groupName = null;

        if (string.IsNullOrEmpty(blendShapeGroupDelimiter) || string.IsNullOrEmpty(shapeName))
            return false;

        if (!shapeName.StartsWith(blendShapeGroupDelimiter) || !shapeName.EndsWith(blendShapeGroupDelimiter))
            return false;

        // 例: "--------Eye--------" なら、前後の "-" を削って "Eye" を表示名にする。
        groupName = shapeName;
        while (groupName.StartsWith(blendShapeGroupDelimiter))
            groupName = groupName.Substring(blendShapeGroupDelimiter.Length);

        while (groupName.EndsWith(blendShapeGroupDelimiter))
            groupName = groupName.Substring(0, groupName.Length - blendShapeGroupDelimiter.Length);

        groupName = groupName.Trim();
        if (string.IsNullOrEmpty(groupName))
            groupName = shapeName;

        return true;
    }

    private bool GroupHasSearchMatch(Mesh mesh, int startIndex, int blendShapeCount)
    {
        // startIndex から次のグループ見出しまでを調べ、
        // 現在のフィルタ条件に一致する BlendShape があるか確認する。
        for (int i = startIndex; i < blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);
            if (TryGetBlendShapeGroupName(shapeName, out _))
                return false;

            if (ShouldShowBlendShape(shapeName, i))
                return true;
        }

        return false;
    }

    private bool DrawBlendShapeGroupFoldout(string groupName)
    {
        // 新しく見つかったグループはデフォルトで閉じる。
        if (!blendShapeFoldouts.TryGetValue(groupName, out bool isOpen))
        {
            isOpen = false;
            blendShapeFoldouts[groupName] = isOpen;
        }

        bool newIsOpen = EditorGUILayout.Foldout(isOpen, groupName, true);
        if (newIsOpen != isOpen)
            blendShapeFoldouts[groupName] = newIsOpen;

        return newIsOpen;
    }

    private bool MatchesBlendShapeSearch(string shapeName)
    {
        if (string.IsNullOrWhiteSpace(blendShapeSearchText))
            return true;

        // 部分一致。OrdinalIgnoreCase で大文字小文字を区別しない。
        return shapeName.IndexOf(blendShapeSearchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ShouldShowBlendShape(string shapeName, int index)
    {
        if (IsLipSyncBlendShape(shapeName))
            return false;

        if (!MatchesBlendShapeSearch(shapeName))
            return false;

        if (!showOnlyNonZeroBlendShapes)
            return true;

        if (index < 0 || index >= previewBlendShapeWeights.Length)
            return false;

        return !Mathf.Approximately(previewBlendShapeWeights[index], 0f);
    }

    private void DrawBlendShapeSlider(int index, string shapeName)
    {
        // スライダーは previewBlendShapeWeights を更新するだけ。
        // 実 SkinnedMeshRenderer の BlendShape 値には書き込まない。
        float currentWeight = index < previewBlendShapeWeights.Length ? previewBlendShapeWeights[index] : 0f;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(shapeName, GUILayout.Width(160));

        float newWeight = EditorGUILayout.Slider(
            currentWeight,
            0f,
            100f,
            GUILayout.Width(220)
        );

        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck())
        {
            if (index >= previewBlendShapeWeights.Length)
                InitializePreviewBlendShapeWeights();

            if (Mathf.Approximately(previewBlendShapeWeights[index], newWeight))
                return;

            if (activeSliderUndoIndex != index)
            {
                PushBlendShapeUndoState();
                activeSliderUndoIndex = index;
            }

            previewBlendShapeWeights[index] = newWeight;

            Repaint();
        }

        Event e = Event.current;
        if (activeSliderUndoIndex == index &&
            e.type == EventType.MouseUp)
        {
            activeSliderUndoIndex = -1;
        }
    }
}

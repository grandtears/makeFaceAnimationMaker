using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // 左の Preview と右の BlendShape 操作パネルの幅をドラッグで調整する。

    private void DrawPaneSplitter(float splitterWidth, float contentWidth, float currentLeftPaneWidth)
    {
        // GUILayout の中に細い Rect を確保して、左右ペインの境界線として使う。
        Rect splitterRect = GUILayoutUtility.GetRect(
            splitterWidth,
            1f,
            GUILayout.Width(splitterWidth),
            GUILayout.ExpandHeight(true)
        );

        EditorGUI.DrawRect(splitterRect, new Color(0.18f, 0.18f, 0.18f, 1f));
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        Event e = Event.current;

        // 境界線上でマウスを押したらドラッグ開始。
        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
        {
            isDraggingPaneSplitter = true;
            e.Use();
        }

        // ドラッグ量を左ペイン幅に加算し、分割比へ戻す。
        // 極端に片側が潰れないように 25% - 75% に制限する。
        if (isDraggingPaneSplitter && e.type == EventType.MouseDrag)
        {
            float newLeftPaneWidth = currentLeftPaneWidth + e.delta.x;
            paneSplit = Mathf.Clamp(newLeftPaneWidth / contentWidth, 0.25f, 0.75f);
            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseUp)
            isDraggingPaneSplitter = false;
    }
}

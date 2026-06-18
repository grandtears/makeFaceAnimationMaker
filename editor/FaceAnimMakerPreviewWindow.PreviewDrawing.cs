using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // PreviewRenderUtility でアバターを描画し、マウス操作で回転・パン・ズームを行う。

    private void DrawPreview()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(
            300,
            520,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        if (previewUtility == null || previewAvatarRoot == null)
        {
            EditorGUI.HelpBox(rect, "Preview object is not available.", MessageType.Info);
            return;
        }

        HandlePreviewInput(rect);
        ApplyPreviewBlendShapeWeights();

        // BeginPreview から EndPreview までの間に camera を設定して Render する。
        previewUtility.BeginPreview(rect, GUIStyle.none);

        previewUtility.camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        previewUtility.camera.clearFlags = CameraClearFlags.Color;

        bool isFaceFocus = hasBlendShapeFocusBounds && previewSelectedRenderer != null;
        if (!TryGetFaceFocusBounds(out Bounds bounds))
        {
            previewUtility.EndPreview();
            EditorGUI.HelpBox(rect, "Preview mesh is not available.", MessageType.Info);
            return;
        }

        Vector3 center = bounds.center;

        // Body メッシュは全身を含むことが多いので、初期フォーカスを少し上へ寄せる。
        // 顔まわりを見やすくするための補正。
        if (!isFaceFocus)
            center.y += bounds.extents.y * 0.45f;

        float size = isFaceFocus
            ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1.2f
            : Mathf.Max(bounds.size.x, bounds.size.y * 0.55f, bounds.size.z);
        if (size <= 0f) size = 1f;

        // previewDir からカメラ回転を作り、previewPan で画面内の位置をずらす。
        Quaternion rotation = Quaternion.Euler(-previewDir.y, -previewDir.x, 0f);
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 panOffset = (right * previewPan.x + up * previewPan.y) * size;
        Vector3 cameraTarget = center + panOffset;
        Vector3 cameraPos = cameraTarget + rotation * new Vector3(0f, 0f, -previewDistance * size);

        previewUtility.camera.transform.position = cameraPos;
        previewUtility.camera.transform.rotation = rotation;
        float farClipSize = size;

        // 顔に寄っていても、髪や衣装など奥行きのあるパーツが far clip で切れないようにする。
        if (TryGetPreviewBounds(out Bounds fullBounds))
            farClipSize = Mathf.Max(farClipSize, fullBounds.extents.magnitude);

        previewUtility.camera.nearClipPlane = 0.01f;
        previewUtility.camera.farClipPlane = Mathf.Max(100f, previewDistance * farClipSize * 6f);

        previewUtility.camera.Render();

        Texture result = previewUtility.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
    }

    private void HandlePreviewInput(Rect rect)
    {
        Event e = Event.current;

        if (!rect.Contains(e.mousePosition))
            return;

        // 左ドラッグ: プレビューを回転する。
        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            previewDir += e.delta;
            e.Use();
            Repaint();
        }

        // 右ドラッグ: カメラの注視点を上下左右へ移動する。
        if (e.type == EventType.MouseDrag && e.button == 1)
        {
            previewPan += new Vector2(-e.delta.x, e.delta.y) * 0.0025f * previewDistance;
            e.Use();
            Repaint();
        }

        // ダブルクリック: パンとズームを初期状態へ戻す。
        if (e.type == EventType.MouseDown && e.clickCount == 2)
        {
            previewPan = Vector2.zero;
            previewDistance = 1.8f;
            e.Use();
            Repaint();
        }

        // ホイール: 顔フォーカスの距離を調整する。
        if (e.type == EventType.ScrollWheel)
        {
            previewDistance += e.delta.y * 0.05f;
            previewDistance = Mathf.Clamp(previewDistance, 0.25f, 8f);
            e.Use();
            Repaint();
        }
    }
}

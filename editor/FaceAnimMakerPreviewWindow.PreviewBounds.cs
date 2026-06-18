using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // 顔まわりへカメラを寄せるため、BlendShape で動く頂点範囲や Renderer Bounds を計算する。

    private void BuildBlendShapeFocusBounds()
    {
        hasBlendShapeFocusBounds = false;
        blendShapeFocusLocalBounds = new Bounds(Vector3.zero, Vector3.zero);

        if (selectedRenderer == null || selectedRenderer.sharedMesh == null)
            return;

        Mesh mesh = selectedRenderer.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int vertexCount = vertices.Length;

        if (vertexCount == 0 || mesh.blendShapeCount == 0)
            return;

        Vector3[] deltaVertices = new Vector3[vertexCount];
        Vector3[] deltaNormals = new Vector3[vertexCount];
        Vector3[] deltaTangents = new Vector3[vertexCount];
        const float epsilon = 0.000001f;

        for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            if (frameCount == 0)
                continue;

            mesh.GetBlendShapeFrameVertices(
                shapeIndex,
                frameCount - 1,
                deltaVertices,
                deltaNormals,
                deltaTangents
            );

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (deltaVertices[vertexIndex].sqrMagnitude <= epsilon)
                    continue;

                if (hasBlendShapeFocusBounds)
                    blendShapeFocusLocalBounds.Encapsulate(vertices[vertexIndex]);
                else
                    blendShapeFocusLocalBounds = new Bounds(vertices[vertexIndex], Vector3.zero);

                hasBlendShapeFocusBounds = true;
            }
        }
    }

    private bool TryGetPreviewBounds(out Bounds bounds)
    {
        return TryGetRendererBounds(previewAvatarRoot, null, out bounds);
    }

    private bool TryGetFaceFocusBounds(out Bounds bounds)
    {
        if (hasBlendShapeFocusBounds && previewSelectedRenderer != null)
        {
            bounds = TransformBounds(blendShapeFocusLocalBounds, previewSelectedRenderer.transform.localToWorldMatrix);
            return true;
        }

        return TryGetFocusBounds(out bounds);
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z)), Vector3.zero);
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z)));
        bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z)));

        return bounds;
    }

    private bool TryGetFocusBounds(out Bounds bounds)
    {
        // カメラの中心は、まず選択中の顔メッシュ Bounds を優先する。
        // 見つからない場合だけアバター全体 Bounds にフォールバックする。
        if (TryGetRendererBounds(previewAvatarRoot, previewSelectedRenderer, out bounds))
            return true;

        return TryGetPreviewBounds(out bounds);
    }

    private static bool TryGetRendererBounds(GameObject root, Renderer preferredRenderer, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        // preferredRenderer が渡されている場合は、その Bounds だけを使う。
        if (preferredRenderer != null && preferredRenderer.enabled && preferredRenderer.gameObject.activeInHierarchy)
        {
            bounds = preferredRenderer.bounds;
            return true;
        }

        if (root == null)
            return false;

        Renderer[] previewRenderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        // アバター全体の Bounds を作るため、有効な Renderer の Bounds を合成する。
        for (int i = 0; i < previewRenderers.Length; i++)
        {
            Renderer renderer = previewRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (hasBounds)
                bounds.Encapsulate(renderer.bounds);
            else
                bounds = renderer.bounds;

            hasBounds = true;
        }

        return hasBounds;
    }
}

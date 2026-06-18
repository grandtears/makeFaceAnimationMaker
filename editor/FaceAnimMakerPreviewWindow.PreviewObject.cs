using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // Scene 上の実アバターには触らず、Preview 専用の複製を作って BlendShape を反映する。
    // 複製側の Behaviour は停止し、EditorWindow の中だけで描画する。

    private void CreatePreviewUtility()
    {
        if (previewUtility != null)
            return;

        // PreviewRenderUtility は EditorWindow 内に独立した Camera / Light を持つ。
        // Scene のカメラやライト状態に依存しないプレビューを描画する。
        previewUtility = new PreviewRenderUtility();
        previewUtility.cameraFieldOfView = 30f;

        // 2灯で最低限の立体感を出す。
        previewUtility.lights[0].intensity = 1.2f;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);

        previewUtility.lights[1].intensity = 0.8f;
        previewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 0f);
    }

    private void CleanupPreview()
    {
        // Window を閉じたときに HideAndDontSave の複製オブジェクトを必ず破棄する。
        DestroyPreviewAvatar();

        if (previewUtility != null)
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }
    }

    private void DestroyPreviewAvatar()
    {
        if (previewAvatarRoot != null)
        {
            // DestroyImmediate を使うのは Editor 拡張上の一時オブジェクトだから。
            DestroyImmediate(previewAvatarRoot);
            previewAvatarRoot = null;
            previewSelectedRenderer = null;
        }
    }

    private void RebuildPreviewObject()
    {
        CreatePreviewUtility();

        // メッシュ切り替え時は表示位置と折りたたみ状態を初期状態へ戻す。
        previewPan = Vector2.zero;
        blendShapeFoldouts.Clear();
        ClearBlendShapeHistory();
        DestroyPreviewAvatar();

        if (avatarRoot == null || selectedRenderer == null || selectedRenderer.sharedMesh == null)
        {
            previewBlendShapeWeights = new float[0];
            return;
        }

        InitializePreviewBlendShapeWeights();

        // Scene 上の実アバターを直接描画・変更せず、Preview 用に丸ごと複製する。
        // AddSingleGO で GameObject と Renderer と Material を Unity の通常描画に近い形で扱える。
        previewAvatarRoot = Instantiate(avatarRoot);
        previewAvatarRoot.name = "FaceAnimationMaker Preview Avatar";
        SetHideFlagsRecursive(previewAvatarRoot, HideFlags.HideAndDontSave);
        DisablePreviewBehaviours(previewAvatarRoot);

        // 複製アバター内で、実アバターの selectedRenderer に対応する SkinnedMeshRenderer を探す。
        // 以降の BlendShape 操作はこの複製側 renderer にだけ行う。
        previewSelectedRenderer = FindPreviewSelectedRenderer();
        BuildBlendShapeFocusBounds();
        ApplyPreviewBlendShapeWeights();
        previewUtility.AddSingleGO(previewAvatarRoot);
    }

    private void InitializePreviewBlendShapeWeights()
    {
        if (selectedRenderer == null || selectedRenderer.sharedMesh == null)
        {
            previewBlendShapeWeights = new float[0];
            return;
        }

        int count = selectedRenderer.sharedMesh.blendShapeCount;
        previewBlendShapeWeights = new float[count];

        // 初期値は実 renderer の現在値からコピーする。
        // 以降は previewBlendShapeWeights だけを書き換えるので、実 renderer は変更されない。
        for (int i = 0; i < count; i++)
            previewBlendShapeWeights[i] = selectedRenderer.GetBlendShapeWeight(i);
    }

    private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
    {
        // 複製アバターが Hierarchy や保存対象に出ないよう、子も含めて HideAndDontSave にする。
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.hideFlags = hideFlags;
    }

    private static void DisablePreviewBehaviours(GameObject root)
    {
        // Preview 用の複製で MonoBehaviour 等が動くと副作用が出るため無効化する。
        // Renderer / Transform は Behaviour ではないので描画には影響しない。
        Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;
    }

    private SkinnedMeshRenderer FindPreviewSelectedRenderer()
    {
        if (avatarRoot == null || previewAvatarRoot == null || selectedRenderer == null)
            return null;

        // Instantiate 後も子階層の並びは基本的に同じなので、
        // 元アバター側と複製側の SkinnedMeshRenderer 配列を同じ index で対応させる。
        SkinnedMeshRenderer[] sourceRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer[] previewRenderers = previewAvatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        int count = Mathf.Min(sourceRenderers.Length, previewRenderers.Length);
        for (int i = 0; i < count; i++)
        {
            if (sourceRenderers[i] == selectedRenderer)
                return previewRenderers[i];
        }

        return null;
    }

    private void ApplyPreviewBlendShapeWeights()
    {
        if (previewSelectedRenderer == null || previewSelectedRenderer.sharedMesh == null)
            return;

        // 実 renderer ではなく複製 renderer にだけ BlendShape 値を適用する。
        // これにより、Scene / Inspector 側の BlendShape 値は変わらない。
        int count = Mathf.Min(previewSelectedRenderer.sharedMesh.blendShapeCount, previewBlendShapeWeights.Length);
        for (int i = 0; i < count; i++)
            previewSelectedRenderer.SetBlendShapeWeight(i, previewBlendShapeWeights[i]);
    }
}

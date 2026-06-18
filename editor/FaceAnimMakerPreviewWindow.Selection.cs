using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow
{
    // Avatar Root と対象 SkinnedMeshRenderer を決める処理。
    // 選択が変わったら Renderer 一覧と Preview 用複製を作り直す。

    private void DrawAvatarField()
    {
        EditorGUI.BeginChangeCheck();

        GameObject newAvatar = (GameObject)EditorGUILayout.ObjectField(
            "Avatar Root",
            avatarRoot,
            typeof(GameObject),
            true
        );

        if (EditorGUI.EndChangeCheck())
        {
            avatarRoot = newAvatar;
            RefreshRenderers();
        }
    }

    private void DrawSelectedRendererField()
    {
        EditorGUI.BeginChangeCheck();

        SkinnedMeshRenderer newRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
            "Selected Face Mesh",
            selectedRenderer,
            typeof(SkinnedMeshRenderer),
            true
        );

        if (EditorGUI.EndChangeCheck())
            SetSelectedRenderer(newRenderer, true);
    }

    private bool ApplySelection()
    {
        // Unity の現在選択から SkinnedMeshRenderer を拾える場合だけ同期する。
        // 何も選ばれていないときや、同じ renderer のときは何もしない。
        SkinnedMeshRenderer rendererFromSelection = GetRendererFromSelection();
        if (rendererFromSelection == null || rendererFromSelection == selectedRenderer)
            return false;

        SetSelectedRenderer(rendererFromSelection, true);
        return true;
    }

    private static SkinnedMeshRenderer GetRendererFromSelection()
    {
        // Component 自体が選ばれている場合。
        if (Selection.activeObject is SkinnedMeshRenderer selectedSkinnedMeshRenderer)
            return selectedSkinnedMeshRenderer;

        if (Selection.activeGameObject == null)
            return null;

        // GameObject が選ばれている場合。
        return Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
    }

    private void SetSelectedRenderer(SkinnedMeshRenderer renderer, bool syncAvatarRoot)
    {
        selectedRenderer = renderer;

        // Scene から直接メッシュを選んだ場合は、その Transform の root を Avatar Root として扱う。
        if (syncAvatarRoot && selectedRenderer != null)
        {
            avatarRoot = selectedRenderer.transform.root.gameObject;
            RefreshRendererList();
        }

        selectedRendererIndex = renderers.IndexOf(selectedRenderer);
        RebuildPreviewObject();
    }

    private void RefreshRenderers()
    {
        selectedRenderer = null;
        selectedRendererIndex = -1;
        RefreshRendererList();

        if (renderers.Count > 0)
            SetSelectedRenderer(renderers[0], false);
        else
            RebuildPreviewObject();
    }

    private void RefreshRendererList()
    {
        renderers.Clear();

        if (avatarRoot == null)
        {
            rendererNames = new string[0];
            return;
        }

        avatarRoot.GetComponentsInChildren(true, renderers);

        rendererNames = new string[renderers.Count];

        for (int i = 0; i < renderers.Count; i++)
        {
            SkinnedMeshRenderer smr = renderers[i];
            string meshName = smr.sharedMesh != null ? smr.sharedMesh.name : "No Mesh";
            rendererNames[i] = $"{smr.name} / {meshName}";
        }
    }

    private void DrawRendererSelector()
    {
        EditorGUI.BeginChangeCheck();

        selectedRendererIndex = EditorGUILayout.Popup(
            "Face Mesh",
            selectedRendererIndex,
            rendererNames
        );

        if (EditorGUI.EndChangeCheck())
        {
            if (selectedRendererIndex >= 0 && selectedRendererIndex < renderers.Count)
                SetSelectedRenderer(renderers[selectedRendererIndex], false);
        }
    }
}

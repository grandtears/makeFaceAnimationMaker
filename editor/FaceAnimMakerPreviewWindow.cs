using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FaceAnimMakerPreviewWindow : EditorWindow
{
    #region 状態フィールド

    // この EditorWindow 全体で共有する入力状態、プレビュー状態、履歴状態をまとめる。
    // 実アバターを直接変更しないため、調整中の値は previewBlendShapeWeights に集約する。

    // ユーザーが指定したアバターのルートと、BlendShape を操作する対象の顔メッシュ。
    private GameObject avatarRoot;
    private SkinnedMeshRenderer selectedRenderer;

    // Avatar Root 配下にある SkinnedMeshRenderer の一覧。
    // Popup の表示名と選択インデックスをここで管理する。
    private readonly List<SkinnedMeshRenderer> renderers = new();
    private string[] rendererNames = new string[0];
    private int selectedRendererIndex = -1;

    // BlendShape 一覧のスクロール位置と、一覧を見やすくするための検索・折りたたみ設定。
    private Vector2 sliderScroll;
    private string blendShapeGroupDelimiter = "-";
    private string blendShapeSearchText = "";
    private bool showOnlyNonZeroBlendShapes;
    private readonly Dictionary<string, bool> blendShapeFoldouts = new();

    // 左の Preview ペインと右の BlendShape ペインの分割比。
    // 初期値 0.5 で左右同じ幅にする。
    private float paneSplit = 0.5f;
    private bool isDraggingPaneSplitter;

    // Unity Editor 上で独立したプレビューを描画するための Utility。
    private PreviewRenderUtility previewUtility;

    // Scene 上の実アバターは変更しない。
    // Preview 用に Avatar Root を複製し、その複製側だけに BlendShape 値を反映する。
    private GameObject previewAvatarRoot;
    private SkinnedMeshRenderer previewSelectedRenderer;
    private bool hasBlendShapeFocusBounds;
    private Bounds blendShapeFocusLocalBounds;
    private float[] previewBlendShapeWeights = new float[0];

    // ツール内だけで使う Undo / Redo 履歴。
    // Unity の Undo ではなく、previewBlendShapeWeights のスナップショットを積む。
    private readonly Stack<float[]> blendShapeUndoStack = new();
    private readonly Stack<float[]> blendShapeRedoStack = new();
    private int activeSliderUndoIndex = -1;
    private AnimationClip expressionAnimationClip;

    // Preview カメラ操作用の状態。
    // 左ドラッグで回転、右ドラッグでパン、ホイールでズームする。
    private Vector2 previewDir = new Vector2(120f, -20f);
    private Vector2 previewPan;
    private float previewDistance = 1.8f;

    #endregion

    #region Unity の入口とメイン UI

    // Unity メニューからウィンドウを開き、左右ペインの大枠を描画する。
    // 具体的な入力欄やプレビュー描画は、下の機能別メソッドへ委譲する。

    [MenuItem("Tools/LastMemories/FaceAnimationMaker")]
    public static void Open()
    {
        GetWindow<FaceAnimMakerPreviewWindow>("FaceAnimationMaker");
    }

    private void OnEnable()
    {
        CreatePreviewUtility();
        ApplySelection();
    }

    private void OnDisable()
    {
        CleanupPreview();
    }

    private void OnSelectionChange()
    {
        // Hierarchy / Scene で SkinnedMeshRenderer を選択したとき、
        // このウィンドウの Selected Face Mesh にも反映する。
        if (ApplySelection())
            Repaint();
    }

    private void OnGUI()
    {
        if (Event.current.type == EventType.MouseUp)
            activeSliderUndoIndex = -1;

        EditorGUILayout.LabelField("FaceAnimationMaker", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("BlendShape Preview", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        DrawAvatarField();
        DrawSelectedRendererField();

        if (avatarRoot == null && selectedRenderer == null)
        {
            EditorGUILayout.HelpBox("Select an Avatar Root or a face SkinnedMeshRenderer.", MessageType.Info);
            return;
        }

        if (avatarRoot != null && renderers.Count == 0)
        {
            EditorGUILayout.HelpBox("No SkinnedMeshRenderer was found.", MessageType.Warning);
            return;
        }

        if (renderers.Count > 0)
            DrawRendererSelector();

        if (selectedRenderer == null || selectedRenderer.sharedMesh == null)
        {
            EditorGUILayout.HelpBox("No valid mesh is selected.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();

        // 左右ペインは固定幅にせず、paneSplit の割合で幅を決める。
        // splitterWidth は中央のドラッグ可能な境界線の幅。
        float splitterWidth = 6f;
        float contentWidth = Mathf.Max(1f, position.width - 20f);
        float paneWidth = Mathf.Max(200f, (contentWidth - splitterWidth) * paneSplit);
        float rightPaneWidth = Mathf.Max(200f, contentWidth - paneWidth - splitterWidth);

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));

        EditorGUILayout.BeginVertical(GUILayout.Width(paneWidth), GUILayout.ExpandHeight(true));
        DrawPreview();
        EditorGUILayout.EndVertical();

        DrawPaneSplitter(splitterWidth, contentWidth, paneWidth);

        EditorGUILayout.BeginVertical(GUILayout.Width(rightPaneWidth), GUILayout.ExpandHeight(true));
        DrawUndoRedoControls();
        EditorGUILayout.Space();
        DrawAnimationSaveControls();
        EditorGUILayout.Space();
        DrawBlendShapeSliders();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region アバターと顔メッシュの選択

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

    #endregion

    #region ペイン分割 UI

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

    #endregion

    #region プレビュー用オブジェクトの生成と同期

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

    #endregion

    #region プレビューのフォーカス範囲計算

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

    #endregion

    #region プレビュー描画とカメラ操作

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

    #endregion

    #region BlendShape 値の Undo / Redo

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

    #endregion

    #region 表情 AnimationClip の入出力

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

    #endregion

    #region BlendShape 一覧とフィルタ UI

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

    #endregion
}

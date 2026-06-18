using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class FaceAnimMakerPreviewWindow : EditorWindow
{
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
}

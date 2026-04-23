using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public class BManagerWindow : EditorWindow
{
    // ── スクロール・検索・ソート ───────────────────────────────────
    private Vector2 scrollPos;
    private string searchKeyword = "";
    private int selectedTab = 0;
    private enum SortType { Name, Date }
    private SortType currentSortType = SortType.Date;
    private bool isAscending = false;

    // ── データキャッシュ ─────────────────────────────────────────
    private List<BManagerData> cachedAllItems = new List<BManagerData>();
    private List<BManagerData> filteredItems  = new List<BManagerData>();
    private List<string> tabNames             = new List<string> { "全て" };
    private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();

    private bool needsRefreshData = true;
    private bool needsRefilter    = true;
    private Vector2 tabScrollPos;

    // ── 設定パネル ───────────────────────────────────────────────
    private bool showSettings = false;

    // ── EditorPrefs キー ─────────────────────────────────────────
    public const string PREF_AUTO_OPEN            = "BManager_AutoOpenOnImport";
    public const string PREF_LAST_REGISTERED_PATH = "BManager_LastRegisteredPath";
    public const string PREF_HIGHLIGHT_ENABLED    = "BManager_HighlightEnabled";
    public const string PREF_HIGHLIGHT_COLOR_R    = "BManager_HighlightColor_R";
    public const string PREF_HIGHLIGHT_COLOR_G    = "BManager_HighlightColor_G";
    public const string PREF_HIGHLIGHT_COLOR_B    = "BManager_HighlightColor_B";

    // ── デフォルトハイライトカラー ────────────────────────────────
    private static readonly Color DEFAULT_HIGHLIGHT = new Color(0.4f, 0.8f, 1.0f, 1f);

    // ═══════════════════════════════════════════════════════════
    //  メニュー・ライフサイクル
    // ═══════════════════════════════════════════════════════════

    [MenuItem("UniMuse.lab/B-Manager")]
    public static void ShowWindow() => GetWindow<BManagerWindow>("B-Manager");

    private void OnEnable()
    {
        RequestRefresh();
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    private void OnDisable()
    {
        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
        Resources.UnloadUnusedAssets();
    }

    private void OnFocus()         => RequestRefresh();
    private void OnProjectChange() => RequestRefresh();

    private void RequestRefresh() { needsRefreshData = true; Repaint(); }

    // ═══════════════════════════════════════════════════════════
    //  データ管理
    // ═══════════════════════════════════════════════════════════

    private void RefreshData()
    {
        var guids = AssetDatabase.FindAssets("t:BManagerData");
        cachedAllItems.Clear();
        foreach (var guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (data != null) cachedAllItems.Add(data);
        }
        SortItems();
        UpdateCategoryCounts();
        needsRefreshData = false;
        needsRefilter    = true;
    }

    private void SortItems()
    {
        if (currentSortType == SortType.Name)
            cachedAllItems = isAscending
                ? cachedAllItems.OrderBy(x => x.itemName).ToList()
                : cachedAllItems.OrderByDescending(x => x.itemName).ToList();
        else
            cachedAllItems = isAscending
                ? cachedAllItems.OrderBy(x => x.registrationTimestamp).ToList()
                : cachedAllItems.OrderByDescending(x => x.registrationTimestamp).ToList();
    }

    private void UpdateCategoryCounts()
    {
        categoryCounts.Clear();
        categoryCounts["全て"] = cachedAllItems.Count;
        foreach (var item in cachedAllItems)
        {
            string tag = (item.tags != null && item.tags.Count > 0) ? item.tags[0] : "未分類";
            categoryCounts[tag] = categoryCounts.GetValueOrDefault(tag) + 1;
        }
        tabNames = new List<string> { $"全て ({categoryCounts["全て"]})" };
        foreach (var tag in categoryCounts.Keys.Where(k => k != "全て").OrderBy(k => k))
            tabNames.Add($"{tag} ({categoryCounts[tag]})");
    }

    private void UpdateFilteredList()
    {
        string lowerKeyword = searchKeyword.ToLower();
        string rawTabName   = tabNames.Count > 0
            ? tabNames[Mathf.Clamp(selectedTab, 0, tabNames.Count - 1)].Split(' ')[0]
            : "全て";

        filteredItems = cachedAllItems.Where(data =>
        {
            if (data == null) return false;
            if (rawTabName != "全て")
            {
                string tag = (data.tags != null && data.tags.Count > 0)
                    ? data.tags[0] : "未分類";
                if (tag != rawTabName) return false;
            }
            if (!string.IsNullOrEmpty(lowerKeyword))
                return data.itemName.ToLower().Contains(lowerKeyword);
            return true;
        }).ToList();

        needsRefilter = false;
    }

    // ═══════════════════════════════════════════════════════════
    //  GUI メイン
    // ═══════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (needsRefreshData) RefreshData();
        if (needsRefilter)    UpdateFilteredList();
        DrawMainContent();
    }

    private void DrawMainContent()
    {
        // ── ツールバー ────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        searchKeyword = EditorGUILayout.TextField(searchKeyword,
            EditorStyles.toolbarSearchField, GUILayout.Width(130));
        if (EditorGUI.EndChangeCheck()) needsRefilter = true;

        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40)))
        { searchKeyword = ""; GUI.FocusControl(null); needsRefilter = true; }

        GUILayout.FlexibleSpace();

        bool autoOpen = EditorPrefs.GetBool(PREF_AUTO_OPEN, false);
        EditorGUI.BeginChangeCheck();
        autoOpen = EditorGUILayout.ToggleLeft(
            new GUIContent("Auto Open", "アセットのインポート時に自動で登録画面を開く設定です"),
            autoOpen, GUILayout.Width(85));
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PREF_AUTO_OPEN, autoOpen);

        if (GUILayout.Button(
            new GUIContent("一括再取得", "リスト内の全BOOTHアイテムの情報を再取得します"),
            EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            if (EditorUtility.DisplayDialog("一括更新", "全アイテムの情報を再取得します", "実行", "キャンセル"))
                _ = RunBulkUpdate();
        }

        // ソートボタン
        var sortContent = EditorGUIUtility.IconContent("AlphabeticalSorting");
        sortContent.tooltip = "並び順を変更します";
        if (GUILayout.Button(sortContent, EditorStyles.toolbarButton, GUILayout.Width(32)))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("名前順 / 昇順"),
                currentSortType == SortType.Name && isAscending,
                () => ApplySort(SortType.Name, true));
            menu.AddItem(new GUIContent("名前順 / 降順"),
                currentSortType == SortType.Name && !isAscending,
                () => ApplySort(SortType.Name, false));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("登録順 / 新しい順"),
                currentSortType == SortType.Date && !isAscending,
                () => ApplySort(SortType.Date, false));
            menu.AddItem(new GUIContent("登録順 / 古い順"),
                currentSortType == SortType.Date && isAscending,
                () => ApplySort(SortType.Date, true));
            menu.ShowAsContext();
        }

        // 設定ボタン
        var settingsContent = EditorGUIUtility.IconContent("_Popup");
        settingsContent.tooltip = "設定";
        EditorGUI.BeginChangeCheck();
        showSettings = GUILayout.Toggle(showSettings, settingsContent,
            EditorStyles.toolbarButton, GUILayout.Width(28));

        EditorGUILayout.EndHorizontal();

        // ── 設定パネル ────────────────────────────────────────────
        if (showSettings) DrawSettingsPanel();

        // ── カテゴリータブ ────────────────────────────────────────
        DrawCategoryTabs();

        // ── D&D ──────────────────────────────────────────────────
        HandleDragAndDrop(new Rect(0, 0, position.width, position.height));

        // ── アイテムリスト（仮想スクロール） ─────────────────────
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        float itemHeight  = 74f;
        int totalCount    = filteredItems.Count;
        int startIndex    = Mathf.Max(0, (int)(scrollPos.y / itemHeight));
        int visibleCount  = (int)(position.height / itemHeight) + 2;
        int endIndex      = Mathf.Min(startIndex + visibleCount, totalCount);

        GUILayout.Space(startIndex * itemHeight);
        for (int i = startIndex; i < endIndex; i++) DrawItemRow(filteredItems[i]);
        GUILayout.Space(Mathf.Max(0, (totalCount - endIndex) * itemHeight));
        EditorGUILayout.EndScrollView();
    }

    // ═══════════════════════════════════════════════════════════
    //  設定パネル
    // ═══════════════════════════════════════════════════════════

    private void DrawSettingsPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);

            // ハイライト有効/無効
            bool hlEnabled = EditorPrefs.GetBool(PREF_HIGHLIGHT_ENABLED, true);
            EditorGUI.BeginChangeCheck();
            hlEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent("最後に追加されたフォルダをハイライト",
                    "登録直後のフォルダに色を付けてProjectツリー上で識別しやすくします"),
                hlEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PREF_HIGHLIGHT_ENABLED, hlEnabled);
                EditorApplication.RepaintProjectWindow();
            }

            if (hlEnabled)
            {
                Color current = LoadHighlightColor();
                EditorGUI.BeginChangeCheck();
                Color next = EditorGUILayout.ColorField("ハイライトカラー", current);
                if (EditorGUI.EndChangeCheck())
                {
                    SaveHighlightColor(next);
                    EditorApplication.RepaintProjectWindow();
                }

                if (GUILayout.Button("ハイライトをクリア", GUILayout.Width(140)))
                {
                    EditorPrefs.DeleteKey(PREF_LAST_REGISTERED_PATH);
                    EditorApplication.RepaintProjectWindow();
                }
            }
        }
    }

    // ── カラー保存/読み込み ───────────────────────────────────────

    public static Color LoadHighlightColor()
    {
        return new Color(
            EditorPrefs.GetFloat(PREF_HIGHLIGHT_COLOR_R, DEFAULT_HIGHLIGHT.r),
            EditorPrefs.GetFloat(PREF_HIGHLIGHT_COLOR_G, DEFAULT_HIGHLIGHT.g),
            EditorPrefs.GetFloat(PREF_HIGHLIGHT_COLOR_B, DEFAULT_HIGHLIGHT.b),
            1f);
    }

    private static void SaveHighlightColor(Color c)
    {
        EditorPrefs.SetFloat(PREF_HIGHLIGHT_COLOR_R, c.r);
        EditorPrefs.SetFloat(PREF_HIGHLIGHT_COLOR_G, c.g);
        EditorPrefs.SetFloat(PREF_HIGHLIGHT_COLOR_B, c.b);
    }

    // ═══════════════════════════════════════════════════════════
    //  Projectウィンドウ カラーハイライト
    // ═══════════════════════════════════════════════════════════

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        if (!EditorPrefs.GetBool(PREF_HIGHLIGHT_ENABLED, true)) return;

        string lastPath = EditorPrefs.GetString(PREF_LAST_REGISTERED_PATH, "");
        if (string.IsNullOrEmpty(lastPath)) return;

        string itemPath = AssetDatabase.GUIDToAssetPath(guid);
        if (itemPath != lastPath) return;

        // 背景にカラーオーバーレイ
        Color hlColor = LoadHighlightColor();
        hlColor.a = 0.30f;
        EditorGUI.DrawRect(selectionRect, hlColor);

        // アイコン右の細いバーで強調
        Rect bar = new Rect(selectionRect.xMax - 4, selectionRect.y, 4, selectionRect.height);
        hlColor.a = 0.85f;
        EditorGUI.DrawRect(bar, hlColor);
    }

    // ═══════════════════════════════════════════════════════════
    //  カテゴリータブ
    // ═══════════════════════════════════════════════════════════

    private void DrawCategoryTabs()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button(EditorGUIUtility.IconContent("scrollleft"),
            EditorStyles.toolbarButton, GUILayout.Width(20)))
            tabScrollPos.x -= 150;

        tabScrollPos = EditorGUILayout.BeginScrollView(
            tabScrollPos, GUIStyle.none, GUIStyle.none, GUILayout.Height(18));
        EditorGUILayout.BeginHorizontal();

        for (int i = 0; i < tabNames.Count; i++)
        {
            var content = new GUIContent(tabNames[i]);
            float width = EditorStyles.toolbarButton.CalcSize(content).x + 10f;
            bool isSelected = (i == selectedTab);
            if (GUILayout.Toggle(isSelected, content, EditorStyles.toolbarButton, GUILayout.Width(width)))
            {
                if (selectedTab != i) { selectedTab = i; needsRefilter = true; }
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button(EditorGUIUtility.IconContent("scrollright"),
            EditorStyles.toolbarButton, GUILayout.Width(20)))
            tabScrollPos.x += 150;

        EditorGUILayout.EndHorizontal();
    }

    // ═══════════════════════════════════════════════════════════
    //  アイテム行描画
    // ═══════════════════════════════════════════════════════════

    private void ApplySort(SortType type, bool asc)
    { currentSortType = type; isAscending = asc; SortItems(); needsRefilter = true; }

    private void DrawItemRow(BManagerData data)
    {
        Rect itemRect = EditorGUILayout.BeginVertical(GUILayout.Height(72));
        Event evt = Event.current;

        if (itemRect.Contains(evt.mousePosition))
        {
            EditorGUI.DrawRect(itemRect, new Color(0.5f, 0.5f, 0.5f, 0.15f));
            EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Link);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (data.linkedAsset != null)
                {
                    Selection.activeObject = data.linkedAsset;
                    EditorGUIUtility.PingObject(data.linkedAsset);
                }
                evt.Use();
            }
            else if (evt.type == EventType.ContextClick)
            {
                ShowItemContextMenu(data);
                evt.Use();
            }
        }

        GUI.Box(itemRect, "", EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        if (data.thumbnail != null)
            GUILayout.Label(data.thumbnail, GUILayout.Width(64), GUILayout.Height(64));
        else
            GUILayout.Box("No Image", GUILayout.Width(64), GUILayout.Height(64));

        EditorGUILayout.BeginVertical();
        GUILayout.Space(5);
        EditorGUILayout.LabelField(data.itemName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Category: {(data.tags != null && data.tags.Count > 0 ? data.tags[0] : "未分類")}",
            EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void ShowItemContextMenu(BManagerData data)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("URLを開く"), false,
            () => Application.OpenURL(data.itemUrl));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("編集"), false,
            () => BManagerPopup.ShowPopup(data.linkedAsset, data));

        if (BManagerPopup.IsBoothUrl(data.itemUrl))
            menu.AddItem(new GUIContent("再取得"), false,
                () => _ = BManagerPopup.StaticFetchAndSave(data, true));
        else
            menu.AddDisabledItem(new GUIContent("再取得 (BOOTHのみ)"));

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("リストから削除"), false,
            () => { DeleteEntry(data, false); RequestRefresh(); });
        menu.AddItem(new GUIContent("アセット本体も削除"), false,
            () => { DeleteEntry(data, true); RequestRefresh(); });
        menu.ShowAsContext();
    }

    // ═══════════════════════════════════════════════════════════
    //  一括再取得
    // ═══════════════════════════════════════════════════════════

    private async Task RunBulkUpdate()
    {
        var items = cachedAllItems
            .Where(i => i != null && BManagerPopup.IsBoothUrl(i.itemUrl))
            .ToList();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                    "一括再取得",
                    $"({i + 1}/{items.Count}) {items[i].itemName}",
                    (float)i / items.Count)) break;

                await Task.Delay(Random.Range(1000, 2000));
                // 一括再取得はトップ画像自動取得 & 以前のサムネイル優先ロジックに任せる
                await BManagerPopup.StaticFetchAndSave(items[i], true);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            RequestRefresh();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  D&D / 削除
    // ═══════════════════════════════════════════════════════════

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition)) return;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                    BManagerPopup.ShowPopup(obj);
                evt.Use();
            }
        }
    }

    private void DeleteEntry(BManagerData data, bool deleteAsset)
    {
        if (EditorUtility.DisplayDialog("削除確認", "データを削除しますか？", "削除", "キャンセル"))
        {
            if (deleteAsset && data.linkedAsset != null)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(data.linkedAsset));
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(data));
            AssetDatabase.SaveAssets();
        }
    }
}
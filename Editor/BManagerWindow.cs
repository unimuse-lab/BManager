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

    private enum SortType { Name, Date, Published, Price }
    private SortType currentSortType = SortType.Date;
    private bool isAscending = false;

    /// <summary>ユーザータグの絞り込み。空文字 = 絞り込みなし。</summary>
    private string selectedUserTag = "";
    /// <summary>「タグなし」で絞り込むための内部キー。ユーザーが入力し得ない値にしている。</summary>
    private const string TAG_FILTER_NONE = "\u0000none";

    // ── データキャッシュ ─────────────────────────────────────────
    private List<BManagerData> cachedAllItems = new List<BManagerData>();
    private List<BManagerData> filteredItems  = new List<BManagerData>();

    // タブは「表示名」と「絞り込みに使う実際のカテゴリー名」を分けて持つ。
    // 以前は "カテゴリー (件数)" を Split(' ')[0] で復元していたため、
    // カテゴリー名に半角スペースが入ると絞り込みが一致しなくなっていた。
    private List<string> tabKeys   = new List<string> { TAB_ALL_KEY };
    private List<string> tabLabels = new List<string>();
    private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
    private List<string> allUserTags = new List<string>();

    private const string TAB_ALL_KEY = "\u0000all";

    private bool needsRefreshData = true;
    private bool needsRefilter    = true;
    private Vector2 tabScrollPos;
    private int seenRegistryVersion = -1;

    // ── 設定パネル ───────────────────────────────────────────────
    private bool showSettings = false;

    // ── EditorPrefs キー ─────────────────────────────────────────
    public const string PREF_AUTO_OPEN = "BManager_AutoOpenOnImport";

    // 以下は後方互換のためのエイリアス。実体は BManagerHighlight 側に移した。
    public const string PREF_LAST_REGISTERED_PATH = BManagerHighlight.PREF_PATH_LEGACY;
    public const string PREF_HIGHLIGHT_ENABLED    = BManagerHighlight.PREF_ENABLED;
    public const string PREF_HIGHLIGHT_COLOR_R    = BManagerHighlight.PREF_COLOR_R;
    public const string PREF_HIGHLIGHT_COLOR_G    = BManagerHighlight.PREF_COLOR_G;
    public const string PREF_HIGHLIGHT_COLOR_B    = BManagerHighlight.PREF_COLOR_B;

    // ── 行のサイズ ───────────────────────────────────────────────
    private const float ROW_INNER_HEIGHT = 82f;
    private const float ROW_HEIGHT       = ROW_INNER_HEIGHT + 2f;  // 行間スペース込み

    // ── 使い回すGUIStyle ─────────────────────────────────────────
    private static GUIStyle _miniWarn;
    private static GUIStyle _miniAlert;
    private static GUIStyle _miniDim;

    private static void EnsureStyles()
    {
        if (_miniWarn != null) return;
        _miniWarn  = new GUIStyle(EditorStyles.miniLabel);
        _miniWarn.normal.textColor  = new Color(1.00f, 0.55f, 0.20f);
        _miniAlert = new GUIStyle(EditorStyles.miniLabel);
        _miniAlert.normal.textColor = new Color(1.00f, 0.38f, 0.38f);
        _miniDim   = new GUIStyle(EditorStyles.miniLabel);
        _miniDim.normal.textColor   = new Color(0.60f, 0.70f, 0.85f);
    }

    // ═══════════════════════════════════════════════════════════
    //  メニュー・ライフサイクル
    // ═══════════════════════════════════════════════════════════

    [MenuItem("UniMuse.lab/B-Manager")]
    public static void ShowWindow() => GetWindow<BManagerWindow>("B-Manager");

    private void OnEnable()
    {
        BManagerRegistry.Invalidate();
        RequestRefresh();
    }

    private void OnDisable()
    {
        Resources.UnloadUnusedAssets();
    }

    /// <summary>
    /// 以前は OnFocus のたびに全データを走査していたため、件数が増えると
    /// ウィンドウを触るだけで重くなっていた。索引のバージョンが変わっていなければ何もしない。
    /// </summary>
    private void OnFocus()
    {
        if (seenRegistryVersion != BManagerRegistry.Version) needsRefreshData = true;
        Repaint();
    }

    private void OnProjectChange()
    {
        BManagerRegistry.Invalidate();
        RequestRefresh();
    }

    private void RequestRefresh() { needsRefreshData = true; Repaint(); }

    // ═══════════════════════════════════════════════════════════
    //  データ管理
    // ═══════════════════════════════════════════════════════════

    private void RefreshData()
    {
        cachedAllItems = BManagerRegistry.All();
        seenRegistryVersion = BManagerRegistry.Version;

        SortItems();
        UpdateCategoryCounts();
        UpdateUserTagList();
        needsRefreshData = false;
        needsRefilter    = true;
    }

    private void SortItems()
    {
        switch (currentSortType)
        {
            case SortType.Name:
                cachedAllItems = isAscending
                    ? cachedAllItems.OrderBy(x => x.itemName).ToList()
                    : cachedAllItems.OrderByDescending(x => x.itemName).ToList();
                break;

            case SortType.Published:
                cachedAllItems = isAscending
                    ? cachedAllItems.OrderBy(x => x.publishedTimestamp).ToList()
                    : cachedAllItems.OrderByDescending(x => x.publishedTimestamp).ToList();
                break;

            case SortType.Price:
                cachedAllItems = isAscending
                    ? cachedAllItems.OrderBy(x => BManagerBoothApi.ParsePrice(x.priceText)).ToList()
                    : cachedAllItems.OrderByDescending(x => BManagerBoothApi.ParsePrice(x.priceText)).ToList();
                break;

            default:
                cachedAllItems = isAscending
                    ? cachedAllItems.OrderBy(x => x.registrationTimestamp).ToList()
                    : cachedAllItems.OrderByDescending(x => x.registrationTimestamp).ToList();
                break;
        }
    }

    private void UpdateCategoryCounts()
    {
        categoryCounts.Clear();
        foreach (var item in cachedAllItems)
        {
            string key = item.Category;
            categoryCounts[key] = categoryCounts.GetValueOrDefault(key) + 1;
        }

        tabKeys   = new List<string> { TAB_ALL_KEY };
        tabLabels = new List<string> {
            string.Format("{0} ({1})", BManagerL10n.Tr("tab.all"), cachedAllItems.Count) };

        foreach (var key in categoryCounts.Keys.OrderBy(k => k))
        {
            tabKeys.Add(key);
            tabLabels.Add(string.Format("{0} ({1})", BManagerL10n.Category(key), categoryCounts[key]));
        }

        if (selectedTab >= tabKeys.Count) selectedTab = 0;
    }

    private void UpdateUserTagList()
    {
        var set = new SortedSet<string>();
        foreach (var item in cachedAllItems)
        {
            if (item.userTags == null) continue;
            foreach (var t in item.userTags)
                if (!string.IsNullOrEmpty(t)) set.Add(t);
        }
        allUserTags = set.ToList();

        // 絞り込み中のタグが無くなっていたら解除する
        if (!string.IsNullOrEmpty(selectedUserTag) &&
            selectedUserTag != TAG_FILTER_NONE &&
            !allUserTags.Contains(selectedUserTag))
            selectedUserTag = "";
    }

    private void UpdateFilteredList()
    {
        string keyword = (searchKeyword ?? "").Trim().ToLowerInvariant();
        string tabKey  = (tabKeys.Count > 0)
            ? tabKeys[Mathf.Clamp(selectedTab, 0, tabKeys.Count - 1)]
            : TAB_ALL_KEY;

        filteredItems = cachedAllItems.Where(data =>
        {
            if (data == null) return false;

            // カテゴリータブ
            if (tabKey != TAB_ALL_KEY && data.Category != tabKey) return false;

            // ユーザータグ
            if (selectedUserTag == TAG_FILTER_NONE)
            {
                if (data.userTags != null && data.userTags.Count > 0) return false;
            }
            else if (!string.IsNullOrEmpty(selectedUserTag))
            {
                if (data.userTags == null || !data.userTags.Contains(selectedUserTag)) return false;
            }

            // キーワード（アイテム名・ショップ名・カテゴリー・ユーザータグ・URL を対象にする）
            if (!string.IsNullOrEmpty(keyword) && !MatchesKeyword(data, keyword)) return false;

            return true;
        }).ToList();

        needsRefilter = false;
    }

    private static bool MatchesKeyword(BManagerData data, string keyword)
    {
        if (Contains(data.itemName, keyword)) return true;
        if (Contains(data.shopName, keyword)) return true;
        if (Contains(data.Category, keyword)) return true;
        if (Contains(data.itemUrl,  keyword)) return true;
        if (data.userTags != null)
            foreach (var t in data.userTags)
                if (Contains(t, keyword)) return true;
        return false;
    }

    private static bool Contains(string haystack, string lowerNeedle)
    {
        return !string.IsNullOrEmpty(haystack) &&
               haystack.ToLowerInvariant().Contains(lowerNeedle);
    }

    // ═══════════════════════════════════════════════════════════
    //  GUI メイン
    // ═══════════════════════════════════════════════════════════

    private void OnGUI()
    {
        EnsureStyles();
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

        if (GUILayout.Button(BManagerL10n.Tr("toolbar.clear"),
            EditorStyles.toolbarButton, GUILayout.Width(40)))
        { searchKeyword = ""; GUI.FocusControl(null); needsRefilter = true; }

        // ユーザータグの絞り込み
        string tagButtonLabel = string.IsNullOrEmpty(selectedUserTag)
            ? BManagerL10n.Tr("filter.tag.button")
            : BManagerL10n.Tr("filter.tag.label",
                selectedUserTag == TAG_FILTER_NONE ? BManagerL10n.Tr("filter.tag.none") : selectedUserTag);
        var tagContent = new GUIContent(tagButtonLabel, BManagerL10n.Tr("toolbar.tagFilter.tip"));
        if (GUILayout.Button(tagContent, EditorStyles.toolbarDropDown,
                GUILayout.Width(Mathf.Max(50f, EditorStyles.toolbarDropDown.CalcSize(tagContent).x + 6f))))
            ShowUserTagFilterMenu();

        GUILayout.FlexibleSpace();

        bool autoOpen = EditorPrefs.GetBool(PREF_AUTO_OPEN, false);
        EditorGUI.BeginChangeCheck();
        autoOpen = EditorGUILayout.ToggleLeft(
            BManagerL10n.C("toolbar.autoOpen", "toolbar.autoOpen.tip"), autoOpen, GUILayout.Width(85));
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PREF_AUTO_OPEN, autoOpen);

        if (GUILayout.Button(BManagerL10n.C("toolbar.bulk", "toolbar.bulk.tip"),
            EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            int count = cachedAllItems.Count(i => i != null && BManagerPopup.IsBoothUrl(i.itemUrl));
            if (EditorUtility.DisplayDialog(
                    BManagerL10n.Tr("dialog.bulk.title"),
                    BManagerL10n.Tr("dialog.bulk.body", count),
                    BManagerL10n.Tr("common.execute"),
                    BManagerL10n.Tr("common.cancel")))
                _ = RunBulkUpdate();
        }

        // 書き出し・読み込み
        var toolsContent = EditorGUIUtility.IconContent("SaveAs");
        toolsContent.tooltip = BManagerL10n.Tr("toolbar.tools.tip");
        if (GUILayout.Button(toolsContent, EditorStyles.toolbarButton, GUILayout.Width(32)))
            ShowToolsMenu();

        // ソートボタン
        var sortContent = EditorGUIUtility.IconContent("AlphabeticalSorting");
        sortContent.tooltip = BManagerL10n.Tr("toolbar.sort.tip");
        if (GUILayout.Button(sortContent, EditorStyles.toolbarButton, GUILayout.Width(32)))
            ShowSortMenu();

        // 設定ボタン
        var settingsContent = EditorGUIUtility.IconContent("_Popup");
        settingsContent.tooltip = BManagerL10n.Tr("toolbar.settings.tip");
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
        int totalCount   = filteredItems.Count;
        int startIndex   = Mathf.Max(0, (int)(scrollPos.y / ROW_HEIGHT));
        int visibleCount = (int)(position.height / ROW_HEIGHT) + 2;
        int endIndex     = Mathf.Min(startIndex + visibleCount, totalCount);

        GUILayout.Space(startIndex * ROW_HEIGHT);
        for (int i = startIndex; i < endIndex; i++) DrawItemRow(filteredItems[i]);
        GUILayout.Space(Mathf.Max(0, (totalCount - endIndex) * ROW_HEIGHT));
        EditorGUILayout.EndScrollView();
    }

    // ═══════════════════════════════════════════════════════════
    //  メニュー
    // ═══════════════════════════════════════════════════════════

    private void ShowSortMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(BManagerL10n.C("sort.nameAsc"),
            currentSortType == SortType.Name && isAscending, () => ApplySort(SortType.Name, true));
        menu.AddItem(BManagerL10n.C("sort.nameDesc"),
            currentSortType == SortType.Name && !isAscending, () => ApplySort(SortType.Name, false));
        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("sort.dateNew"),
            currentSortType == SortType.Date && !isAscending, () => ApplySort(SortType.Date, false));
        menu.AddItem(BManagerL10n.C("sort.dateOld"),
            currentSortType == SortType.Date && isAscending, () => ApplySort(SortType.Date, true));
        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("sort.pubNew"),
            currentSortType == SortType.Published && !isAscending, () => ApplySort(SortType.Published, false));
        menu.AddItem(BManagerL10n.C("sort.pubOld"),
            currentSortType == SortType.Published && isAscending, () => ApplySort(SortType.Published, true));
        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("sort.priceHigh"),
            currentSortType == SortType.Price && !isAscending, () => ApplySort(SortType.Price, false));
        menu.AddItem(BManagerL10n.C("sort.priceLow"),
            currentSortType == SortType.Price && isAscending, () => ApplySort(SortType.Price, true));
        menu.ShowAsContext();
    }

    private void ShowUserTagFilterMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(BManagerL10n.C("filter.tag.all"), string.IsNullOrEmpty(selectedUserTag),
            () => { selectedUserTag = ""; needsRefilter = true; Repaint(); });
        menu.AddItem(BManagerL10n.C("filter.tag.none"), selectedUserTag == TAG_FILTER_NONE,
            () => { selectedUserTag = TAG_FILTER_NONE; needsRefilter = true; Repaint(); });

        if (allUserTags.Count > 0) menu.AddSeparator("");
        foreach (var tag in allUserTags)
        {
            string captured = tag;
            menu.AddItem(new GUIContent(tag), selectedUserTag == tag,
                () => { selectedUserTag = captured; needsRefilter = true; Repaint(); });
        }
        menu.ShowAsContext();
    }

    private void ShowToolsMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(BManagerL10n.C("tools.exportJson"), false, () => ExportTo("json"));
        menu.AddItem(BManagerL10n.C("tools.exportCsv"),  false, () => ExportTo("csv"));
        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("tools.importJson"), false, () => ImportFrom("json"));
        menu.AddItem(BManagerL10n.C("tools.importCsv"),  false, () => ImportFrom("csv"));
        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("tools.restoreBackup"), false, RestoreFromBackup);
        menu.AddItem(BManagerL10n.C("tools.openBackup"),    false, BManagerBackup.OpenBackupFolder);
        menu.ShowAsContext();
    }

    // ═══════════════════════════════════════════════════════════
    //  書き出し・読み込み
    // ═══════════════════════════════════════════════════════════

    private void ExportTo(string extension)
    {
        if (cachedAllItems.Count == 0)
        {
            EditorUtility.DisplayDialog(BManagerL10n.Tr("export.title"),
                BManagerL10n.Tr("export.none"), BManagerL10n.Tr("common.ok"));
            return;
        }

        string path = EditorUtility.SaveFilePanel(
            BManagerL10n.Tr("export.title"), Application.dataPath, "BManagerItems", extension);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (extension == "csv")
                // Excelで開いても文字化けしないよう BOM 付きで書く
                BManagerBackup.WriteTextFile(path, BManagerBackup.BuildCsv(cachedAllItems), true);
            else
                BManagerBackup.WriteTextFile(path, BManagerBackup.BuildJson(cachedAllItems), false);

            EditorUtility.DisplayDialog(
                BManagerL10n.Tr("export.done.title"),
                BManagerL10n.Tr("export.done", cachedAllItems.Count, path),
                BManagerL10n.Tr("common.ok"));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[B-Manager] 書き出しに失敗しました: " + e.Message);
            EditorUtility.DisplayDialog(BManagerL10n.Tr("export.done.title"), e.Message,
                BManagerL10n.Tr("common.ok"));
        }
    }

    private void ImportFrom(string extension)
    {
        string path = EditorUtility.OpenFilePanel(BManagerL10n.Tr("import.title"), Application.dataPath, extension);
        if (string.IsNullOrEmpty(path)) return;

        List<BManagerBackup.Record> records;
        try
        {
            string text = System.IO.File.ReadAllText(path);
            records = (extension == "csv")
                ? BManagerBackup.ParseCsv(text)
                : BManagerBackup.ParseJson(text);
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog(BManagerL10n.Tr("import.failed.title"),
                BManagerL10n.Tr("import.failed", e.Message), BManagerL10n.Tr("common.ok"));
            return;
        }

        ApplyImport(records);
    }

    private void RestoreFromBackup()
    {
        var records = BManagerBackup.ReadAllBackups();
        if (records.Count == 0)
        {
            EditorUtility.DisplayDialog(BManagerL10n.Tr("backup.restore.title"),
                BManagerL10n.Tr("backup.none"), BManagerL10n.Tr("common.ok"));
            return;
        }

        if (!EditorUtility.DisplayDialog(
                BManagerL10n.Tr("backup.restore.title"),
                BManagerL10n.Tr("backup.restore.confirm", records.Count),
                BManagerL10n.Tr("common.execute"),
                BManagerL10n.Tr("common.cancel")))
            return;

        ApplyRecordsAndReport(records);
    }

    private void ApplyImport(List<BManagerBackup.Record> records)
    {
        if (records == null || records.Count == 0)
        {
            EditorUtility.DisplayDialog(BManagerL10n.Tr("import.failed.title"),
                BManagerL10n.Tr("import.failed", BManagerL10n.Tr("err.parse")),
                BManagerL10n.Tr("common.ok"));
            return;
        }

        if (!EditorUtility.DisplayDialog(
                BManagerL10n.Tr("import.confirm.title"),
                BManagerL10n.Tr("import.confirm", records.Count),
                BManagerL10n.Tr("common.execute"),
                BManagerL10n.Tr("common.cancel")))
            return;

        ApplyRecordsAndReport(records);
    }

    private void ApplyRecordsAndReport(List<BManagerBackup.Record> records)
    {
        var result = BManagerBackup.ApplyRecords(records);
        RequestRefresh();

        EditorUtility.DisplayDialog(
            BManagerL10n.Tr("import.done.title"),
            BManagerL10n.Tr("import.done", result.updated, result.added, result.skipped),
            BManagerL10n.Tr("common.ok"));
    }

    // ═══════════════════════════════════════════════════════════
    //  設定パネル
    // ═══════════════════════════════════════════════════════════

    private void DrawSettingsPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(BManagerL10n.Tr("settings.title"), EditorStyles.boldLabel);

            // 表示言語
            EditorGUI.BeginChangeCheck();
            var lang = (BManagerL10n.Lang)EditorGUILayout.EnumPopup(
                BManagerL10n.Tr("settings.language"), BManagerL10n.Current);
            if (EditorGUI.EndChangeCheck())
            {
                BManagerL10n.Current = lang;
                // タブのラベルなど翻訳済み文字列を作り直す
                UpdateCategoryCounts();
                needsRefilter = true;
                Repaint();
            }

            // JSONバックアップ
            EditorGUI.BeginChangeCheck();
            bool backup = EditorGUILayout.ToggleLeft(
                BManagerL10n.C("settings.backup", "settings.backup.tip"),
                BManagerBackup.BackupEnabled);
            if (EditorGUI.EndChangeCheck()) BManagerBackup.BackupEnabled = backup;

            if (GUILayout.Button(BManagerL10n.Tr("settings.openBackup"), GUILayout.Width(200)))
                BManagerBackup.OpenBackupFolder();

            EditorGUILayout.Space(2);

            // ハイライト有効/無効
            EditorGUI.BeginChangeCheck();
            bool hlEnabled = EditorGUILayout.ToggleLeft(
                BManagerL10n.C("settings.highlight", "settings.highlight.tip"),
                BManagerHighlight.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                BManagerHighlight.Enabled = hlEnabled;
                EditorApplication.RepaintProjectWindow();
            }

            if (hlEnabled)
            {
                EditorGUI.BeginChangeCheck();
                Color next = EditorGUILayout.ColorField(
                    BManagerL10n.Tr("settings.highlightColor"), BManagerHighlight.LoadColor());
                if (EditorGUI.EndChangeCheck())
                {
                    BManagerHighlight.SaveColor(next);
                    EditorApplication.RepaintProjectWindow();
                }

                if (GUILayout.Button(BManagerL10n.Tr("settings.clearHighlight"), GUILayout.Width(200)))
                    BManagerHighlight.Clear();
            }
        }
    }

    /// <summary>後方互換のためのエイリアス。</summary>
    public static Color LoadHighlightColor() { return BManagerHighlight.LoadColor(); }

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

        for (int i = 0; i < tabLabels.Count; i++)
        {
            var content = new GUIContent(tabLabels[i]);
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
        Rect itemRect = EditorGUILayout.BeginVertical(GUILayout.Height(ROW_INNER_HEIGHT));
        Event evt = Event.current;
        bool linkBroken = (data.linkedAsset == null);

        if (itemRect.Contains(evt.mousePosition))
        {
            EditorGUI.DrawRect(itemRect, new Color(0.5f, 0.5f, 0.5f, 0.15f));
            EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Link);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (!linkBroken)
                {
                    Selection.activeObject = data.linkedAsset;
                    EditorGUIUtility.PingObject(data.linkedAsset);
                }
                else
                {
                    // 無反応で終わらせず、理由と対処をログに残す
                    Debug.LogWarning("[B-Manager] 「" + data.itemName +
                        "」の紐づけ先アセットが見つかりません。右クリックメニューから再リンクしてください。");
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
            GUILayout.Box(BManagerL10n.Tr("item.noImage"), GUILayout.Width(64), GUILayout.Height(64));

        EditorGUILayout.BeginVertical();
        GUILayout.Space(4);

        // 1行目: アイテム名
        EditorGUILayout.LabelField(data.itemName, EditorStyles.boldLabel);

        // 2行目: カテゴリー ／ ショップ
        string line2 = BManagerL10n.Tr("item.category") + ": " + BManagerL10n.Category(data.Category);
        if (!string.IsNullOrEmpty(data.shopName)) line2 += "   /   " + data.shopName;
        EditorGUILayout.LabelField(line2, EditorStyles.miniLabel);

        // 3行目: 価格・販売状態・ユーザータグ
        EditorGUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(data.priceText))
            GUILayout.Label(data.priceText, EditorStyles.miniLabel,
                GUILayout.Width(EditorStyles.miniLabel.CalcSize(new GUIContent(data.priceText)).x + 6f));

        if (linkBroken)
            DrawMiniTag(BManagerL10n.Tr("item.missingAsset"), _miniAlert);
        if (data.isEndOfSale)
            DrawMiniTag(BManagerL10n.Tr("badge.endOfSale"), _miniAlert);
        else if (data.isSoldOut)
            DrawMiniTag(BManagerL10n.Tr("badge.soldOut"), _miniWarn);

        if (data.userTags != null && data.userTags.Count > 0)
            DrawMiniTag("#" + string.Join(" #", data.userTags.ToArray()), _miniDim);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static void DrawMiniTag(string text, GUIStyle style)
    {
        var content = new GUIContent(text);
        GUILayout.Label(content, style, GUILayout.Width(style.CalcSize(content).x + 6f));
    }

    private void ShowItemContextMenu(BManagerData data)
    {
        var menu = new GenericMenu();

        if (!string.IsNullOrEmpty(data.itemUrl))
        {
            menu.AddItem(BManagerL10n.C("menu.openUrl"), false, () => Application.OpenURL(data.itemUrl));
            menu.AddItem(BManagerL10n.C("menu.copyUrl"), false,
                () => EditorGUIUtility.systemCopyBuffer = data.itemUrl);
        }
        else
        {
            menu.AddDisabledItem(BManagerL10n.C("menu.openUrl"));
        }

        if (!string.IsNullOrEmpty(data.shopUrl))
            menu.AddItem(BManagerL10n.C("menu.openShop"), false, () => Application.OpenURL(data.shopUrl));

        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("menu.edit"), false,
            () => BManagerPopup.ShowPopup(data.linkedAsset, data));

        if (BManagerPopup.IsBoothUrl(data.itemUrl))
            menu.AddItem(BManagerL10n.C("menu.refetch"), false, () => _ = RefetchOne(data));
        else
            menu.AddDisabledItem(BManagerL10n.C("menu.refetchDisabled"));

        // リンク切れの救済
        Object selected = Selection.activeObject;
        bool canRelink = (selected != null) &&
                         !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(selected)) &&
                         selected != data.linkedAsset;
        if (canRelink)
            menu.AddItem(BManagerL10n.C("menu.relink"), false, () => Relink(data, selected));
        else
            menu.AddDisabledItem(BManagerL10n.C("menu.relinkDisabled"));

        menu.AddSeparator("");
        menu.AddItem(BManagerL10n.C("menu.removeFromList"), false, () => DeleteEntry(data, false));
        menu.AddItem(BManagerL10n.C("menu.deleteAsset"),    false, () => DeleteEntry(data, true));
        menu.ShowAsContext();
    }

    private async Task RefetchOne(BManagerData data)
    {
        await BManagerPopup.StaticFetchAndSave(data, true);
        RequestRefresh();
    }

    private void Relink(BManagerData data, Object newTarget)
    {
        string newPath = AssetDatabase.GetAssetPath(newTarget);

        if (!EditorUtility.DisplayDialog(
                BManagerL10n.Tr("dialog.relink.title"),
                BManagerL10n.Tr("dialog.relink.body", data.itemName, newPath),
                BManagerL10n.Tr("common.execute"),
                BManagerL10n.Tr("common.cancel")))
            return;

        var duplicate = BManagerRegistry.FindDuplicate(newTarget, data);
        if (duplicate != null)
        {
            EditorUtility.DisplayDialog(
                BManagerL10n.Tr("dialog.relink.title"),
                BManagerL10n.Tr("popup.dup", duplicate.itemName),
                BManagerL10n.Tr("common.ok"));
            return;
        }

        Undo.RecordObject(data, "B-Manager Relink");
        data.linkedAsset = newTarget;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);

        BManagerRegistry.Invalidate();
        BManagerBackup.WriteBackup(data);
        RequestRefresh();
    }

    // ═══════════════════════════════════════════════════════════
    //  一括再取得
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 一括再取得。
    /// 以前は StartAssetEditing() 〜 StopAssetEditing() の間で await していたため、
    /// 途中で例外が出るとアセット編集ロックが残る危険があった。
    /// ここではロックを張らず、進捗バーと取得間隔のみを管理する。
    /// Undo はひとまとめにして、1回の Ctrl+Z で全件戻せるようにしている。
    /// </summary>
    private async Task RunBulkUpdate()
    {
        var items = cachedAllItems
            .Where(i => i != null && BManagerPopup.IsBoothUrl(i.itemUrl))
            .ToList();
        if (items.Count == 0) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("B-Manager Bulk Refetch");

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        BManagerL10n.Tr("progress.bulk.title"),
                        BManagerL10n.Tr("progress.bulk.body", i + 1, items.Count, items[i].itemName),
                        (float)i / items.Count)) break;

                // BOOTHへ連続アクセスしないよう間隔を空ける（1件目は待たない）
                if (i > 0) await Task.Delay(UnityEngine.Random.Range(1000, 2000));

                // 一括再取得ではトップ画像自動取得 & 以前のサムネイル優先ロジックに任せる。
                // ユーザータグは null を渡して変更しない。
                await BManagerPopup.StaticFetchAndSave(items[i], true);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            BManagerRegistry.Invalidate();
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
                {
                    // 既に登録済みのフォルダを落とした場合は、重複を作らず編集として開く
                    BManagerPopup.ShowPopup(obj, BManagerRegistry.FindByAsset(obj));
                    break;  // 登録画面は1つずつ
                }
                evt.Use();
            }
        }
    }

    private void DeleteEntry(BManagerData data, bool deleteAsset)
    {
        string assetPath = (data.linkedAsset != null)
            ? AssetDatabase.GetAssetPath(data.linkedAsset) : "";

        string message = deleteAsset
            ? BManagerL10n.Tr("dialog.deleteAsset.body", data.itemName,
                string.IsNullOrEmpty(assetPath) ? "-" : assetPath)
            : BManagerL10n.Tr("dialog.delete.body", data.itemName);

        if (!EditorUtility.DisplayDialog(
                BManagerL10n.Tr("dialog.delete.title"), message,
                BManagerL10n.Tr("common.delete"),
                BManagerL10n.Tr("common.cancel")))
            return;

        BManagerBackup.DeleteBackup(data);

        if (deleteAsset && !string.IsNullOrEmpty(assetPath))
            AssetDatabase.DeleteAsset(assetPath);

        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(data));
        AssetDatabase.SaveAssets();

        BManagerRegistry.Invalidate();
        RequestRefresh();
    }
}

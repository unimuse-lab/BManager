using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

public class BManagerWindow : EditorWindow
{
    private Vector2 scrollPos;
    private string searchKeyword = "";
    private int selectedTab = 0;
    private enum SortType { Name, Date }
    private SortType currentSortType = SortType.Date;
    private bool isAscending = false;

    private List<BManagerData> cachedAllItems = new List<BManagerData>();
    private List<BManagerData> filteredItems = new List<BManagerData>();
    private List<string> tabNames = new List<string> { "全て" };
    private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();

    private bool needsRefreshData = true;
    private bool needsRefilter = true;
    public const string PREF_AUTO_OPEN = "BManager_AutoOpenOnImport";
    private Vector2 tabScrollPos;

    [MenuItem("UniMuse.lab/B-Manager")]
    public static void ShowWindow() => GetWindow<BManagerWindow>("B-Manager");

    private void OnEnable() => RequestRefresh();
    private void OnFocus() => RequestRefresh();
    private void OnProjectChange() => RequestRefresh();
    private void OnDisable() => Resources.UnloadUnusedAssets();

    private void RequestRefresh() { needsRefreshData = true; Repaint(); }

    private void RefreshData()
    {
        var guids = AssetDatabase.FindAssets("t:BManagerData");
        cachedAllItems.Clear();
        foreach (var guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && !cachedAllItems.Contains(data)) cachedAllItems.Add(data);
        }
        SortItems();
        UpdateCategoryCounts();
        needsRefreshData = false;
        needsRefilter = true;
    }

    private void SortItems()
    {
        if (currentSortType == SortType.Name)
            cachedAllItems = isAscending ? cachedAllItems.OrderBy(x => x.itemName).ToList() : cachedAllItems.OrderByDescending(x => x.itemName).ToList();
        else
            cachedAllItems = isAscending ? cachedAllItems.OrderBy(x => x.registrationTimestamp).ToList() : cachedAllItems.OrderByDescending(x => x.registrationTimestamp).ToList();
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
        foreach (var tag in categoryCounts.Keys.Where(k => k != "全て").OrderBy(k => k)) tabNames.Add($"{tag} ({categoryCounts[tag]})");
    }

    private void UpdateFilteredList()
    {
        string lowerKeyword = searchKeyword.ToLower();
        string rawTabName = tabNames.Count > 0 ? tabNames[Mathf.Clamp(selectedTab, 0, tabNames.Count - 1)].Split(' ')[0] : "全て";
        filteredItems = cachedAllItems.Where(data => {
            if (data == null) return false;
            if (rawTabName != "全て")
            {
                string tag = (data.tags != null && data.tags.Count > 0) ? data.tags[0] : "未分類";
                if (tag != rawTabName) return false;
            }
            if (!string.IsNullOrEmpty(lowerKeyword)) return data.itemName.ToLower().Contains(lowerKeyword);
            return true;
        }).ToList();
        needsRefilter = false;
    }

    private void OnGUI()
    {
        if (needsRefreshData) RefreshData();
        if (needsRefilter) UpdateFilteredList();
        DrawMainContent();
    }

    private void DrawMainContent()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        searchKeyword = EditorGUILayout.TextField(searchKeyword, EditorStyles.toolbarSearchField, GUILayout.Width(130));
        if (EditorGUI.EndChangeCheck()) needsRefilter = true;
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40))) { searchKeyword = ""; GUI.FocusControl(null); needsRefilter = true; }
        GUILayout.FlexibleSpace();

        bool autoOpen = EditorPrefs.GetBool(PREF_AUTO_OPEN, false);
        EditorGUI.BeginChangeCheck();
        GUIContent autoOpenContent = new GUIContent("Auto Open", "アセットのインポート時に自動で登録画面を開く設定です");
        autoOpen = EditorGUILayout.ToggleLeft(autoOpenContent, autoOpen, GUILayout.Width(85));
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PREF_AUTO_OPEN, autoOpen);

        GUIContent bulkUpdateContent = new GUIContent("一括再取得", "リスト内の全てのBOOTHアイテムの情報をWebから取得し直して更新します");
        if (GUILayout.Button(bulkUpdateContent, EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            if (EditorUtility.DisplayDialog("一括更新", "全アイテムの情報を再取得します", "実行", "キャンセル")) _ = RunBulkUpdate();
        }

        GUIContent sortContent = EditorGUIUtility.IconContent("AlphabeticalSorting");
        sortContent.tooltip = "アイテムの並び順（名前順・登録日順）を変更します";
        if (GUILayout.Button(sortContent, EditorStyles.toolbarButton, GUILayout.Width(32)))
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("名前順 / 昇順"), currentSortType == SortType.Name && isAscending, () => ApplySort(SortType.Name, true));
            menu.AddItem(new GUIContent("名前順 / 降順"), currentSortType == SortType.Name && !isAscending, () => ApplySort(SortType.Name, false));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("登録順 / 新しい順"), currentSortType == SortType.Date && !isAscending, () => ApplySort(SortType.Date, false));
            menu.AddItem(new GUIContent("登録順 / 古い順"), currentSortType == SortType.Date && isAscending, () => ApplySort(SortType.Date, true));
            menu.ShowAsContext();
        }
        EditorGUILayout.EndHorizontal();

        DrawCategoryTabs();
        HandleDragAndDrop(new Rect(0, 0, position.width, position.height));

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        float itemHeight = 74f;
        int totalCount = filteredItems.Count;
        int startIndex = Mathf.Max(0, (int)(scrollPos.y / itemHeight));
        int visibleCount = (int)(position.height / itemHeight) + 2;
        int endIndex = Mathf.Min(startIndex + visibleCount, totalCount);

        GUILayout.Space(startIndex * itemHeight);
        for (int i = startIndex; i < endIndex; i++) DrawItemRow(filteredItems[i]);
        GUILayout.Space(Mathf.Max(0, (totalCount - endIndex) * itemHeight));
        EditorGUILayout.EndScrollView();
    }

    private void DrawCategoryTabs()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button(EditorGUIUtility.IconContent("scrollleft"), EditorStyles.toolbarButton, GUILayout.Width(20)))
        {
            tabScrollPos.x -= 150;
        }

        tabScrollPos = EditorGUILayout.BeginScrollView(tabScrollPos, GUIStyle.none, GUIStyle.none, GUILayout.Height(18));
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < tabNames.Count; i++)
        {
            GUIContent content = new GUIContent(tabNames[i]);
            float width = EditorStyles.toolbarButton.CalcSize(content).x + 10f;
            bool isSelected = (i == selectedTab);
            if (GUILayout.Toggle(isSelected, content, EditorStyles.toolbarButton, GUILayout.Width(width)))
            {
                if (selectedTab != i) { selectedTab = i; needsRefilter = true; }
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button(EditorGUIUtility.IconContent("scrollright"), EditorStyles.toolbarButton, GUILayout.Width(20)))
        {
            tabScrollPos.x += 150;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void ApplySort(SortType type, bool asc) { currentSortType = type; isAscending = asc; SortItems(); needsRefilter = true; }

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
                if (data.linkedAsset != null) { Selection.activeObject = data.linkedAsset; EditorGUIUtility.PingObject(data.linkedAsset); }
                evt.Use();
            }
            else if (evt.type == EventType.ContextClick)
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("URLを開く"), false, () => Application.OpenURL(data.itemUrl));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("編集"), false, () => BManagerPopup.ShowPopup(data.linkedAsset, data));
                if (BManagerPopup.IsBoothUrl(data.itemUrl)) menu.AddItem(new GUIContent("再取得"), false, () => _ = BManagerPopup.StaticFetchAndSave(data, true));
                else menu.AddDisabledItem(new GUIContent("再取得 (BOOTHのみ)"));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("リストから削除"), false, () => { DeleteEntry(data, false); RequestRefresh(); });
                menu.AddItem(new GUIContent("アセット本体も削除"), false, () => { DeleteEntry(data, true); RequestRefresh(); });
                menu.ShowAsContext();
                evt.Use();
            }
        }

        GUI.Box(itemRect, "", EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        if (data.thumbnail != null) GUILayout.Label(data.thumbnail, GUILayout.Width(64), GUILayout.Height(64));
        else GUILayout.Box("No Image", GUILayout.Width(64), GUILayout.Height(64));
        EditorGUILayout.BeginVertical();
        GUILayout.Space(5);
        EditorGUILayout.LabelField(data.itemName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Category: {(data.tags != null && data.tags.Count > 0 ? data.tags[0] : "未分類")}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private async Task RunBulkUpdate()
    {
        var items = cachedAllItems.Where(i => i != null && BManagerPopup.IsBoothUrl(i.itemUrl)).ToList();
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("一括再取得", $"({i + 1}/{items.Count}) {items[i].itemName}", (float)i / items.Count)) break;
                await Task.Delay(Random.Range(1000, 2000));
                await BManagerPopup.StaticFetchAndSave(items[i], true);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets(); RequestRefresh();
        }
    }

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
                foreach (Object obj in DragAndDrop.objectReferences) BManagerPopup.ShowPopup(obj);
                evt.Use();
            }
        }
    }

    private void DeleteEntry(BManagerData data, bool deleteAsset)
    {
        // 修正: ユーザーに見せるメッセージから「JSONも削除されます」を削除
        if (EditorUtility.DisplayDialog("削除確認", "データを削除しますか？", "削除", "キャンセル"))
        {
            // 内部処理として該当アイテムのJSONバックアップのみを特定して削除
            string assetPath = AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string jsonPath = assetPath.Replace(".asset", ".json");
                if (File.Exists(jsonPath))
                {
                    AssetDatabase.DeleteAsset(jsonPath);
                }
            }

            if (deleteAsset && data.linkedAsset != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(data.linkedAsset));
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

public class BManagerPopup : EditorWindow
{
    // ── 基本データ ──────────────────────────────────────────────
    public string inputUrl   = "";
    public string inputTitle = "";
    public Object targetAsset;
    public BManagerData existingData;
    private int selectedCategoryIndex = 0;

    // ── 保存先 ───────────────────────────────────────────────────
    private const string ROOT_DATA_PATH = "Assets/UniMuseData";
    private const string SAVE_PATH      = "Assets/UniMuseData/B-Manager";

    // ── カテゴリー ────────────────────────────────────────────────
    private static readonly string[] CategoryOptions = new string[]
    {
        "未分類", "3Dキャラクター", "3D装飾品", "3D衣装", "3D小道具",
        "3Dテクスチャ", "3Dツール・システム", "3Dモーション・アニメーション",
        "3D環境・ワールド", "VRoid", "3Dキャラクター（その他）"
    };

    // ── フォルダ階層選択 ──────────────────────────────────────────
    private List<string> folderCandidates = new List<string>();   // 選択肢フォルダパス一覧
    private bool[] folderExpanded;                                 // ツリーの展開状態
    private bool[] folderRegisteredCache;                          // 登録状態キャッシュ
    private int selectedFolderIndex = 0;                          // 現在選択中のフォルダ
    private Vector2 folderScrollPos;
    private bool showFolderTree = false;                          // 階層選択UIを表示するか

    // ── サムネイル選択 ────────────────────────────────────────────
    private bool autoTopThumbnail = true;           // チェックON = トップサムネイルを自動取得
    private List<string> thumbnailUrls = new List<string>();
    private List<Texture2D> thumbnailPreviews = new List<Texture2D>();
    private int selectedThumbnailIndex = 0;
    private bool isFetchingThumbnails = false;
    private bool showThumbnailSelector = false;
    private Vector2 thumbScrollPos;

    // 既存データが持っていたサムネイルURL（再取得時に優先）
    private string previousThumbnailUrl = "";

    // ── ウィンドウサイズ ──────────────────────────────────────────
    private const float BASE_WIDTH = 420f;
    private float _measuredH = -1f; // Repaintで計測した実際のコンテンツ高さ

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    public static void ShowPopup(Object target, BManagerData data = null)
    {
        var window = GetWindow<BManagerPopup>(true, "アイテム登録・編集", true);
        // 幅のみ固定。高さは OnGUI 内の実測値で動的に決まるため初期値は最小限にとどめる
        window.minSize = new Vector2(BASE_WIDTH, 100f);
        window.maxSize = new Vector2(BASE_WIDTH, 900f);
        window.Setup(target, data);
        window.Show();
    }

    public void Setup(Object target, BManagerData data = null)
    {
        targetAsset  = target;
        existingData = data;

        if (data != null)
        {
            inputUrl   = data.itemUrl;
            inputTitle = data.itemName;
            int idx = System.Array.IndexOf(CategoryOptions,
                (data.tags != null && data.tags.Count > 0) ? data.tags[0] : "");
            selectedCategoryIndex = (idx >= 0) ? idx : 0;
            previousThumbnailUrl  = data.previousThumbnailUrl ?? "";
        }
        else
        {
            inputTitle = target != null ? target.name : "";
            inputUrl   = "";
            selectedCategoryIndex = 0;
            previousThumbnailUrl  = "";
        }

        autoTopThumbnail    = true;
        showThumbnailSelector = false;
        thumbnailUrls.Clear();
        thumbnailPreviews.Clear();

        // フォルダ候補を収集
        BuildFolderCandidates(target);
    }

    // ═══════════════════════════════════════════════════════════
    //  フォルダ候補ツリー構築
    // ═══════════════════════════════════════════════════════════

    private void BuildFolderCandidates(Object target)
    {
        folderCandidates.Clear();
        selectedFolderIndex = 0;

        if (target == null) { showFolderTree = false; return; }

        string rootPath = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(rootPath)) { showFolderTree = false; return; }

        // ルート自身を先頭に追加
        folderCandidates.Add(rootPath);

        // 配下のすべてのサブフォルダを収集
        CollectSubFolders(rootPath, folderCandidates);

        // 1つしかない（ルートのみ）場合はツリーUI不要
        showFolderTree = (folderCandidates.Count > 1);

        // 登録状態をキャッシュ化（Setup時に一度だけ計算）
        folderRegisteredCache = new bool[folderCandidates.Count];
        folderExpanded = new bool[folderCandidates.Count];
        for (int i = 0; i < folderCandidates.Count; i++)
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(folderCandidates[i]);
            folderRegisteredCache[i] = (obj != null && BManagerImportDetector.IsAlreadyRegistered(obj));
            folderExpanded[i] = true;
        }
    }

    private static void CollectSubFolders(string path, List<string> result)
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { path });
        var direct = new HashSet<string>();
        foreach (string g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!AssetDatabase.IsValidFolder(p)) continue;
            // path 直下の一段のみ
            string relative = p.Substring(path.Length).TrimStart('/');
            if (!relative.Contains('/')) direct.Add(p);
        }
        foreach (string sub in direct.OrderBy(x => x))
        {
            result.Add(sub);
            CollectSubFolders(sub, result);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  GUI
    // ═══════════════════════════════════════════════════════════

    private void OnGUI()
    {
        DrawInRect(new Rect(0, 0, position.width, position.height));
    }

    public void DrawInRect(Rect rect)
    {
        // 高さを 9999f にすることで BeginArea がコンテンツを縦方向に制限しない
        // → GUILayoutUtility.GetLastRect() で実際のコンテンツ高さを計測できる
        GUILayout.BeginArea(new Rect(0, 0, rect.width, 9999f));
        GUILayout.Space(8);

        // ── 基本情報 ─────────────────────────────────────────────
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("登録対象", targetAsset, typeof(Object), false);

            inputTitle = EditorGUILayout.TextField("アイテム名", inputTitle);

            string rawUrl = EditorGUILayout.TextField("商品URL", inputUrl);
            inputUrl = rawUrl != null ? rawUrl.Trim() : "";

            selectedCategoryIndex = EditorGUILayout.Popup("カテゴリー", selectedCategoryIndex, CategoryOptions);
        }

        // ── フォルダ階層選択ツリー ────────────────────────────────
        if (showFolderTree)
        {
            GUILayout.Space(4);
            DrawFolderTree();
        }

        // ── サムネイルオプション ──────────────────────────────────
        GUILayout.Space(4);
        DrawThumbnailOptions();

        // ── ボタン群 ──────────────────────────────────────────────
        GUILayout.Space(8);
        bool isBooth = IsBoothUrl(inputUrl);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!isBooth))
        {
            if (GUILayout.Button("情報を取得して保存", GUILayout.Height(30)))
                _ = RunFetchAndSaveFromUI();
        }
        if (GUILayout.Button("入力内容のみで保存", GUILayout.Height(30)))
        {
            string rootPath = (folderCandidates.Count > 0) ? folderCandidates[0] : "";
            PerformSave(inputTitle, null, null, inputUrl,
                GetSelectedTarget(), existingData, selectedCategoryIndex, false, rootPath);
            this.Close();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("キャンセル", GUILayout.Height(20)))
            this.Close();

        if (!isBooth && !string.IsNullOrEmpty(inputUrl))
            EditorGUILayout.HelpBox("BOOTH以外のURLです。情報は自動取得されないため、手動入力して保存してください。", MessageType.Info);

        // ── 動的サイズ計測（案B）────────────────────────────────
        // キャンセルボタン（または HelpBox）の直後に 1px の空要素を置き
        // Repaint イベント時にその底辺座標 = 実コンテンツ高さとして取得する
        GUILayout.Space(1);
        if (Event.current.type == EventType.Repaint)
        {
            float contentH = GUILayoutUtility.GetLastRect().yMax + 6f; // 6px 下余白
            if (Mathf.Abs(_measuredH - contentH) > 0.5f)
            {
                _measuredH = contentH;
                minSize = maxSize = new Vector2(BASE_WIDTH, contentH);
            }
        }

        GUILayout.EndArea();
    }

    // ── フォルダ階層ツリー描画 ────────────────────────────────────

    private void DrawFolderTree()
    {
        EditorGUILayout.LabelField("紐づけるフォルダを選択", EditorStyles.boldLabel);

        float treeHeight = Mathf.Min(folderCandidates.Count * 20f + 4f, 120f);
        folderScrollPos = EditorGUILayout.BeginScrollView(folderScrollPos,
            EditorStyles.helpBox, GUILayout.Height(treeHeight));

        for (int i = 0; i < folderCandidates.Count; i++)
        {
            string path    = folderCandidates[i];
            bool registered = (i < folderRegisteredCache.Length) ? folderRegisteredCache[i] : false;

            // インデント計算（ルートからの深さ）
            string root  = folderCandidates[0];
            int depth    = path == root ? 0
                : path.Substring(root.Length).TrimStart('/').Count(c => c == '/') + 1;
            float indent = depth * 14f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);

            using (new EditorGUI.DisabledScope(registered))
            {
                GUIStyle style = new GUIStyle(EditorStyles.label);
                if (registered)       style.normal.textColor = Color.gray;
                else if (i == selectedFolderIndex) style.fontStyle = FontStyle.Bold;

                string label = System.IO.Path.GetFileName(path)
                    + (registered ? "  ✓登録済み" : "");

                bool sel = (selectedFolderIndex == i);
                bool next = EditorGUILayout.ToggleLeft(label, sel, style);
                if (next && !registered && next != sel)
                    selectedFolderIndex = i;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    // ── サムネイルオプション描画 ──────────────────────────────────

    private void DrawThumbnailOptions()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            autoTopThumbnail = EditorGUILayout.ToggleLeft(
                "トップサムネイルを自動取得（推奨）", autoTopThumbnail);
            if (EditorGUI.EndChangeCheck())
            {
                // チェックを外したとき & URLが有効なら候補を取得
                if (!autoTopThumbnail && IsBoothUrl(inputUrl))
                    _ = FetchThumbnailList(inputUrl);
                else
                    showThumbnailSelector = false;
            }

            if (!autoTopThumbnail && showThumbnailSelector)
                DrawThumbnailSelector();
            else if (!autoTopThumbnail && isFetchingThumbnails)
                EditorGUILayout.LabelField("サムネイル一覧を取得中...", EditorStyles.miniLabel);
        }
    }

    private void DrawThumbnailSelector()
    {
        if (thumbnailUrls.Count == 0)
        {
            EditorGUILayout.LabelField("サムネイルが見つかりませんでした。", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.LabelField($"使用するサムネイルを選択: ({selectedThumbnailIndex + 1}/{thumbnailUrls.Count})", EditorStyles.miniLabel);
        float previewSize = 64f;
        float scrollHeight = Mathf.Min(thumbnailUrls.Count * (previewSize + 4f), 240f);

        thumbScrollPos = EditorGUILayout.BeginScrollView(
            thumbScrollPos, GUILayout.Height(scrollHeight));
        for (int i = 0; i < thumbnailUrls.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            Texture2D prev = (i < thumbnailPreviews.Count) ? thumbnailPreviews[i] : null;
            if (prev != null)
                GUILayout.Label(prev, GUILayout.Width(previewSize), GUILayout.Height(previewSize));
            else
                GUILayout.Box("読込中...", GUILayout.Width(previewSize), GUILayout.Height(previewSize));

            bool sel = (selectedThumbnailIndex == i);
            bool next = EditorGUILayout.ToggleLeft(
                $"画像 {i + 1}\n{GetThumbnailUrlDisplay(thumbnailUrls[i])}", sel, GUILayout.ExpandWidth(true));
            if (next && next != sel) selectedThumbnailIndex = i;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private string GetThumbnailUrlDisplay(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Length > 50) return url.Substring(0, 47) + "...";
        return url;
    }

    // ─── サムネイル一覧取得（全件並列） ───────────────────────────

    private async Task FetchThumbnailList(string url)
    {
        isFetchingThumbnails   = true;
        showThumbnailSelector  = false;
        thumbnailUrls.Clear();
        thumbnailPreviews.Clear();
        selectedThumbnailIndex = 0;
        Repaint();

        try
        {
            using (var r = UnityWebRequest.Get(url))
            {
                r.SetRequestHeader("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                r.timeout = 15;
                var op = r.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (r.result != UnityWebRequest.Result.Success) return;

                string html = System.Text.Encoding.UTF8.GetString(r.downloadHandler.data);
                thumbnailUrls = ExtractAllThumbnailUrls(html);

                if (thumbnailUrls.Count == 0) return;

                // 既存のサムネイルURLが候補に含まれていれば優先選択
                if (!string.IsNullOrEmpty(previousThumbnailUrl))
                {
                    int prevIdx = thumbnailUrls.IndexOf(previousThumbnailUrl);
                    if (prevIdx >= 0) selectedThumbnailIndex = prevIdx;
                }

                // プレビュー取得（全件並列取得）
                thumbnailPreviews = new List<Texture2D>(new Texture2D[thumbnailUrls.Count]);
                var tasks = thumbnailUrls.Select((imgUrl, idx) => FetchPreview(imgUrl, idx)).ToList();
                await Task.WhenAll(tasks);

                showThumbnailSelector = (thumbnailUrls.Count > 1);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[B-Manager] サムネイル一覧取得失敗: {e.Message}");
        }
        finally
        {
            isFetchingThumbnails = false;
            Repaint();
        }
    }

    private async Task FetchPreview(string imgUrl, int index)
    {
        try
        {
            using (var tr = UnityWebRequestTexture.GetTexture(imgUrl))
            {
                var op = tr.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (tr.result == UnityWebRequest.Result.Success)
                    thumbnailPreviews[index] = DownloadHandlerTexture.GetContent(tr);
            }
        }
        catch { /* プレビュー取得失敗は無視 */ }
    }

    /// <summary>
    /// BOOTHページから商品本体画像のURLを重複なく取得する。
    ///
    /// 取得対象:
    ///   1. og:image メタタグ（メイン画像・最優先）
    ///   2. data-origin 属性（market-item-detail-item-image の本体画像）
    ///   3. JavaScript JSON 内の image_url
    ///
    /// 除外対象:
    ///   ・/c/ を含む URL（BOOTHのリサイズCDNパス = スライダーサムネイル縮小版）
    ///   ・img src 属性（Slick カルーセルの slick-cloned 複製スライドを含むため全除外）
    ///
    /// Slick カルーセルはスライドを複製するため同一URLが複数回現れるが、
    /// HashSet による重複排除で対処している。
    /// </summary>
    public static List<string> ExtractAllThumbnailUrls(string html)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>();

        // 1) og:image（メイン画像・最優先）
        var ogMatch = Regex.Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
        if (ogMatch.Success) AddUnique(ogMatch.Groups[1].Value, urls, seen);

        // 2) data-origin 属性のみ取得
        //    (?!c/) でリサイズ版（/c/72x72_a2_g5/... 形式）を除外する
        //    スライダーサムネイルは src のみで data-origin を持たないため自然に除外される
        foreach (Match m in Regex.Matches(html,
            @"data-origin=""(https://booth\.pximg\.net/(?!c/)[^""]+)"""))
            AddUnique(m.Groups[1].Value, urls, seen);

        // 3) JavaScript JSON 内の image_url（BOOTH SPA 形式）
        //    同様に /c/ を含まない完全解像度URLのみ取得
        foreach (Match m in Regex.Matches(html,
            @"""image_url""\s*:\s*""(https://booth\.pximg\.net/(?!c/)[^""]+)"""))
            AddUnique(m.Groups[1].Value, urls, seen);

        // ※ img src 属性は取得しない
        //   スライダーの src は _base_resized.jpg（縮小版）かつ
        //   slick-cloned により同一画像が3〜5回複製されるため除外

        return urls;
    }

    private static void AddUnique(string url, List<string> list, HashSet<string> seen)
    {
        url = url.Trim();
        if (!string.IsNullOrEmpty(url) && seen.Add(url)) list.Add(url);
    }

    // ═══════════════════════════════════════════════════════════
    //  保存処理
    // ═══════════════════════════════════════════════════════════

    private Object GetSelectedTarget()
    {
        if (!showFolderTree || folderCandidates.Count == 0) return targetAsset;
        int idx = Mathf.Clamp(selectedFolderIndex, 0, folderCandidates.Count - 1);
        return AssetDatabase.LoadMainAssetAtPath(folderCandidates[idx]) ?? targetAsset;
    }

    private async Task RunFetchAndSaveFromUI()
    {
        string useThumbnailUrl = autoTopThumbnail ? "" : GetSelectedThumbnailUrl();
        string rootPath = (folderCandidates.Count > 0) ? folderCandidates[0] : "";
        await StaticFetchAndSave(existingData, true, inputUrl, inputTitle,
            GetSelectedTarget(), selectedCategoryIndex,
            autoTopThumbnail, useThumbnailUrl, rootPath);
        this.Close();
    }

    private string GetSelectedThumbnailUrl()
    {
        if (thumbnailUrls.Count == 0) return "";
        return thumbnailUrls[Mathf.Clamp(selectedThumbnailIndex, 0, thumbnailUrls.Count - 1)];
    }

    // ── 静的保存メソッド ──────────────────────────────────────────

    public static async Task StaticFetchAndSave(
        BManagerData data,
        bool keepCategory,
        string overrideUrl        = "",
        string overrideTitle      = "",
        Object overrideTarget     = null,
        int    catIdx             = 0,
        bool   autoTop            = true,
        string forceThumbnailUrl  = "",
        string rootFolderPath     = "")
    {
        string url = (data != null) ? data.itemUrl : overrideUrl;
        if (!IsBoothUrl(url)) return;

        string prevThumbUrl = (data != null) ? (data.previousThumbnailUrl ?? "") : forceThumbnailUrl;

        try
        {
            using (var r = UnityWebRequest.Get(url))
            {
                r.SetRequestHeader("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                r.timeout = 15;
                var op = r.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (r.result != UnityWebRequest.Result.Success) return;

                string html  = System.Text.Encoding.UTF8.GetString(r.downloadHandler.data);
                var titleMatch = Regex.Match(html, @"<title>(.*?)</title>");
                string title = titleMatch.Success ? titleMatch.Groups[1].Value
                    : (data != null ? data.itemName : overrideTitle);
                title = System.Net.WebUtility.HtmlDecode(title)
                    .Replace(" - BOOTH", "").Trim();

                string thumbUrl = "";
                if (autoTop)
                {
                    var imgMatch = Regex.Match(html,
                        @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
                    thumbUrl = imgMatch.Success ? imgMatch.Groups[1].Value : "";
                }
                else
                {
                    if (!string.IsNullOrEmpty(forceThumbnailUrl))
                    {
                        thumbUrl = forceThumbnailUrl;
                    }
                    else
                    {
                        var allUrls = ExtractAllThumbnailUrls(html);
                        thumbUrl = allUrls.Count > 0 ? allUrls[0] : "";
                    }
                }

                if (!string.IsNullOrEmpty(prevThumbUrl) &&
                    prevThumbUrl != thumbUrl &&
                    ExistsInHtml(prevThumbUrl, html))
                {
                    thumbUrl = prevThumbUrl;
                }

                Texture2D thumb = null;
                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    using (var tr = UnityWebRequestTexture.GetTexture(thumbUrl))
                    {
                        var op2 = tr.SendWebRequest();
                        while (!op2.isDone) await Task.Yield();
                        if (tr.result == UnityWebRequest.Result.Success)
                            thumb = DownloadHandlerTexture.GetContent(tr);
                    }
                }

                PerformSave(title, thumb, thumbUrl, url,
                    (data != null ? data.linkedAsset : overrideTarget),
                    data, catIdx, keepCategory, rootFolderPath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[B-Manager Error] {e.Message}");
        }
    }

    private static bool ExistsInHtml(string url, string html)
        => !string.IsNullOrEmpty(url) && html.Contains(url);

    private static void PerformSave(
        string      title,
        Texture2D   thumb,
        string      thumbUrl,
        string      url,
        Object      target,
        BManagerData data,
        int         catIdx,
        bool        keepCategory,
        string      rootFolderPath = "")
    {
        bool isNew = (data == null);
        if (isNew)
        {
            data = CreateInstance<BManagerData>();
            data.registrationTimestamp = System.DateTime.Now.Ticks;
        }

        data.itemName    = title;
        data.itemUrl     = url;
        data.linkedAsset = target;
        if (!string.IsNullOrEmpty(thumbUrl))
            data.previousThumbnailUrl = thumbUrl;
        if (isNew || !keepCategory)
            data.tags = new List<string> { CategoryOptions[catIdx] };

        EnsureFolderExists();

        string path = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(path))
        {
            string safeName = string.Join("_",
                title.Split(System.IO.Path.GetInvalidFileNameChars()));
            path = AssetDatabase.GenerateUniqueAssetPath($"{SAVE_PATH}/{safeName}.asset");
            AssetDatabase.CreateAsset(data, path);
        }

        if (thumb != null)
        {
            Texture2D newThumb = new Texture2D(thumb.width, thumb.height);
            newThumb.SetPixels(thumb.GetPixels());
            newThumb.Apply();
            DestroyImmediate(thumb);
            newThumb.name      = "Thumbnail";
            newThumb.hideFlags = HideFlags.HideInHierarchy;

            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is Texture2D && !AssetDatabase.IsMainAsset(sub))
                    DestroyImmediate(sub, true);

            AssetDatabase.AddObjectToAsset(newThumb, data);
            data.thumbnail = newThumb;
        }

        string highlightPath = string.IsNullOrEmpty(rootFolderPath)
            ? AssetDatabase.GetAssetPath(target)
            : rootFolderPath;
        EditorPrefs.SetString(BManagerWindow.PREF_LAST_REGISTERED_PATH, highlightPath);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.SaveAssets();
    }

    private static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder(ROOT_DATA_PATH))
            AssetDatabase.CreateFolder("Assets", "UniMuseData");
        if (!AssetDatabase.IsValidFolder(SAVE_PATH))
            AssetDatabase.CreateFolder(ROOT_DATA_PATH, "B-Manager");
    }

    // ═══════════════════════════════════════════════════════════
    //  ユーティリティ
    // ═══════════════════════════════════════════════════════════

    public static bool IsBoothUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return Regex.IsMatch(url.Trim(),
            @"^https://([a-zA-Z0-9-]+\.)?booth\.pm/([^/]+/)?items/\d+");
    }

    // ═══════════════════════════════════════════════════════════
    //  コンテキストメニュー
    // ═══════════════════════════════════════════════════════════

    [MenuItem("Assets/Register to B-Manager", false, 2000)]
    private static void RegisterFromContextMenu()
    {
        Object[] selectedObjects = Selection.GetFiltered<Object>(SelectionMode.Assets);
        foreach (Object obj in selectedObjects)
        {
            if (obj is MonoScript) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path)) { ShowPopup(obj); return; }
        }
    }

    [MenuItem("Assets/Register to B-Manager", true, 2000)]
    private static bool ValidateRegisterFromContextMenu()
    {
        Object[] selectedObjects = Selection.GetFiltered<Object>(SelectionMode.Assets);
        foreach (Object obj in selectedObjects)
        {
            if (obj is MonoScript) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            if (!BManagerImportDetector.IsAlreadyRegistered(obj)) return true;
        }
        return false;
    }
}
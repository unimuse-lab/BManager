using UnityEngine;
using UnityEditor;
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

    // BOOTHのカテゴリーは固定リストに無いものもあるため、選択肢を動的に持つ
    private List<string> categoryChoices = new List<string>();
    private GUIContent[] categoryLabels  = new GUIContent[0];

    // ── ユーザータグ（BOOTHのカテゴリーとは別構造） ───────────────
    private List<string> userTags = new List<string>();
    private string newTagInput = "";
    private const string TAG_FIELD_CONTROL = "BManagerNewTagField";

    // ── 自動取得の可否判定（ボタンを押す前にエディターへ表示する） ──
    private enum CheckState { Empty, NotBooth, Checking, Available, Unavailable, Saving }
    private CheckState checkState   = CheckState.Empty;
    private string checkMessage     = "";
    private string lastSeenUrl      = null;
    private bool   checkScheduled   = false;
    private string pendingCheckUrl  = "";
    private double checkFireTime    = 0;
    private const double CHECK_DELAY_SECONDS = 0.6;

    // 可否判定で取得した商品情報。保存時に使い回して同じリクエストを繰り返さない
    private BManagerBoothApi.ItemInfo cachedInfo = null;
    private string cachedInfoUrl = "";

    // ── 重複登録の検出 ────────────────────────────────────────────
    private BManagerData duplicateEntry = null;
    private Object       lastDupTarget  = null;

    // ── カテゴリー ────────────────────────────────────────────────
    /// <summary>後方互換のためのエイリアス。実体は BManagerData 側に移した。</summary>
    public const string UNCATEGORIZED = BManagerData.UNCATEGORIZED;

    private static readonly string[] CategoryOptions = new string[]
    {
        UNCATEGORIZED, "3Dキャラクター", "3D装飾品", "3D衣装", "3D髪型", "3D靴", "3D小道具",
        "3Dテクスチャ", "3Dツール・システム", "3Dモーション・アニメーション",
        "3D環境・ワールド", "VRoid", "3Dキャラクター（その他）"
    };

    // ── フォルダ階層選択 ──────────────────────────────────────────
    private List<string> folderCandidates = new List<string>();   // 選択肢フォルダパス一覧
    private bool[] folderRegisteredCache;                          // 登録状態キャッシュ
    private int selectedFolderIndex = 0;                          // 現在選択中のフォルダ
    private Vector2 folderScrollPos;
    private bool showFolderTree = false;                          // 階層選択UIを表示するか

    // ── サムネイル選択 ────────────────────────────────────────────
    private bool autoTopThumbnail = true;           // チェックON = トップサムネイルを自動取得
    private List<string> thumbnailUrls = new List<string>();      // 原寸URL
    private List<Texture2D> thumbnailPreviews = new List<Texture2D>();
    private int selectedThumbnailIndex = 0;
    private bool isFetchingThumbnails = false;
    private bool showThumbnailSelector = false;
    private Vector2 thumbScrollPos;

    // 既存データが持っていたサムネイルURL（再取得時に優先）
    private string previousThumbnailUrl = "";

    /// <summary>
    /// .asset に埋め込むサムネイルの最大辺。
    /// 原寸をそのまま非圧縮で持つとアイテム数に比例してメモリを食うため縮小する。
    /// </summary>
    private const int MAX_THUMBNAIL_SIZE = 512;

    // ── ウィンドウサイズ ──────────────────────────────────────────
    private const float BASE_WIDTH = 420f;
    private float _measuredH = -1f; // Repaintで計測した実際のコンテンツ高さ

    // ── 使い回すGUIStyle（毎フレーム new しない） ─────────────────
    private static GUIStyle _folderLabel;
    private static GUIStyle _folderLabelSelected;
    private static GUIStyle _folderLabelRegistered;

    private static void EnsureStyles()
    {
        if (_folderLabel != null) return;
        _folderLabel = new GUIStyle(EditorStyles.label);
        _folderLabelSelected = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
        _folderLabelRegistered = new GUIStyle(EditorStyles.label);
        _folderLabelRegistered.normal.textColor = Color.gray;
    }

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    public static void ShowPopup(Object target, BManagerData data = null)
    {
        var window = GetWindow<BManagerPopup>(true, BManagerL10n.Tr("popup.title"), true);
        // 幅のみ固定。高さは OnGUI 内の実測値で動的に決まるため初期値は最小限にとどめる
        window.minSize = new Vector2(BASE_WIDTH, 100f);
        window.maxSize = new Vector2(BASE_WIDTH, 900f);
        window.Setup(target, data);
        window.Show();
    }

    /// <summary>BOOTHの商品URLかどうか。</summary>
    public static bool IsBoothUrl(string url)
    {
        return BManagerBoothApi.IsBoothUrl(url);
    }

    private void OnEnable()  { EditorApplication.update += TickCheck; }
    private void OnDisable() { EditorApplication.update -= TickCheck; }

    public void Setup(Object target, BManagerData data = null)
    {
        targetAsset  = target;
        existingData = data;

        if (data != null)
        {
            inputUrl   = data.itemUrl;
            inputTitle = data.itemName;
            RebuildCategoryChoices(data.Category == BManagerData.UNCATEGORIZED ? "" : data.Category);
            userTags   = (data.userTags != null) ? new List<string>(data.userTags) : new List<string>();
            previousThumbnailUrl = data.previousThumbnailUrl ?? "";
        }
        else
        {
            inputTitle = target != null ? target.name : "";
            inputUrl   = "";
            RebuildCategoryChoices("");
            userTags.Clear();
            previousThumbnailUrl = "";
        }

        newTagInput = "";
        ResetCheckState();

        autoTopThumbnail      = true;
        showThumbnailSelector = false;
        thumbnailUrls.Clear();
        thumbnailPreviews.Clear();

        // フォルダ候補を収集
        BuildFolderCandidates(target);

        lastDupTarget  = null;
        duplicateEntry = null;
    }

    // ═══════════════════════════════════════════════════════════
    //  カテゴリー選択肢
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// BOOTHから同期したカテゴリーが固定リストに無い場合、選択肢へ追加して表示できるようにする。
    /// </summary>
    private void RebuildCategoryChoices(string currentCategory)
    {
        categoryChoices = new List<string>(CategoryOptions);
        if (!string.IsNullOrEmpty(currentCategory) && !categoryChoices.Contains(currentCategory))
            categoryChoices.Add(currentCategory);

        categoryLabels = new GUIContent[categoryChoices.Count];
        for (int i = 0; i < categoryChoices.Count; i++)
            categoryLabels[i] = new GUIContent(BManagerL10n.Category(categoryChoices[i]));

        int idx = categoryChoices.IndexOf(currentCategory);
        selectedCategoryIndex = (idx >= 0) ? idx : 0;
    }

    private string SelectedCategory
    {
        get
        {
            if (categoryChoices.Count == 0) return UNCATEGORIZED;
            return categoryChoices[Mathf.Clamp(selectedCategoryIndex, 0, categoryChoices.Count - 1)];
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  自動取得の可否判定
    // ═══════════════════════════════════════════════════════════

    private void ResetCheckState()
    {
        lastSeenUrl    = null;
        checkScheduled = false;
        checkState     = CheckState.Empty;
        checkMessage   = "";
        cachedInfo     = null;
        cachedInfoUrl  = "";
    }

    /// <summary>
    /// URLが書き換わったら判定をやり直す。入力中に毎文字リクエストしないよう少し待ってから実行する。
    /// </summary>
    private void HandleUrlChanged()
    {
        if (inputUrl == lastSeenUrl) return;
        lastSeenUrl = inputUrl;

        checkMessage  = "";
        cachedInfo    = null;
        cachedInfoUrl = "";

        // URLが変わればサムネイル候補も作り直す
        thumbnailUrls.Clear();
        thumbnailPreviews.Clear();
        showThumbnailSelector = false;

        if (string.IsNullOrEmpty(inputUrl))
        {
            checkScheduled = false;
            checkState     = CheckState.Empty;
        }
        else if (!IsBoothUrl(inputUrl))
        {
            checkScheduled = false;
            checkState     = CheckState.NotBooth;
        }
        else
        {
            pendingCheckUrl = inputUrl;
            checkFireTime   = EditorApplication.timeSinceStartup + CHECK_DELAY_SECONDS;
            checkScheduled  = true;
            checkState      = CheckState.Checking;
        }
    }

    private void TickCheck()
    {
        if (!checkScheduled) return;
        if (EditorApplication.timeSinceStartup < checkFireTime) return;
        checkScheduled = false;
        _ = RunCheck(pendingCheckUrl);
    }

    private async Task RunCheck(string url)
    {
        BManagerBoothApi.ItemInfo info = await BManagerBoothApi.FetchItem(url);

        // 判定中にURLが書き換えられていた場合は結果を破棄する
        if (url != inputUrl) return;

        if (info.ok)
        {
            cachedInfo    = info;
            cachedInfoUrl = url;
            checkState    = CheckState.Available;

            // 候補一覧は取得済みの情報から作れるので、追加のリクエストは不要
            thumbnailUrls = new List<string>(info.imageUrls);
            PreselectPreviousThumbnail();
        }
        else
        {
            checkMessage = info.error;
            checkState   = CheckState.Unavailable;
        }
        Repaint();
    }

    /// <summary>既存のサムネイルURLが候補に含まれていれば、それを初期選択にする。</summary>
    private void PreselectPreviousThumbnail()
    {
        selectedThumbnailIndex = 0;
        if (string.IsNullOrEmpty(previousThumbnailUrl) || thumbnailUrls.Count == 0) return;

        string prevKey = BManagerBoothApi.CanonicalImageKey(previousThumbnailUrl);
        for (int i = 0; i < thumbnailUrls.Count; i++)
        {
            if (BManagerBoothApi.CanonicalImageKey(thumbnailUrls[i]) == prevKey)
            {
                selectedThumbnailIndex = i;
                return;
            }
        }
    }

    /// <summary>
    /// 自動取得の可否表示。状態が変わってもコントロール数が変わらないよう常に1つのHelpBoxで描画する。
    /// </summary>
    private void DrawCheckStatus()
    {
        string message;
        MessageType type;

        switch (checkState)
        {
            case CheckState.NotBooth:
                message = BManagerL10n.Tr("check.notBooth");
                type    = MessageType.Warning;
                break;

            case CheckState.Checking:
                message = BManagerL10n.Tr("check.checking");
                type    = MessageType.None;
                break;

            case CheckState.Available:
                {
                    string unknown = BManagerL10n.Tr("check.unknown");
                    string cat = (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.category))
                        ? cachedInfo.category
                        : BManagerL10n.Tr("check.catUnavailable");

                    message = BManagerL10n.Tr("check.available",
                        (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.name))      ? cachedInfo.name      : unknown,
                        cat,
                        (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.shopName))  ? cachedInfo.shopName  : unknown,
                        (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.priceText)) ? cachedInfo.priceText : unknown);

                    type = MessageType.Info;
                    if (cachedInfo != null && cachedInfo.isEndOfSale)
                    {
                        message += BManagerL10n.Tr("check.endOfSale");
                        type = MessageType.Warning;
                    }
                    else if (cachedInfo != null && cachedInfo.isSoldOut)
                    {
                        message += BManagerL10n.Tr("check.soldOut");
                        type = MessageType.Warning;
                    }
                }
                break;

            case CheckState.Unavailable:
                message = BManagerL10n.Tr("check.unavailable", checkMessage);
                type    = MessageType.Error;
                break;

            case CheckState.Saving:
                message = BManagerL10n.Tr("check.saving");
                type    = MessageType.None;
                break;

            default:
                message = BManagerL10n.Tr("check.empty");
                type    = MessageType.None;
                break;
        }

        EditorGUILayout.HelpBox(message, type, true);
    }

    // ═══════════════════════════════════════════════════════════
    //  フォルダ候補ツリー構築
    // ═══════════════════════════════════════════════════════════

    private void BuildFolderCandidates(Object target)
    {
        folderCandidates.Clear();
        selectedFolderIndex   = 0;
        folderRegisteredCache = new bool[0];

        if (target == null) { showFolderTree = false; return; }

        string rootPath = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(rootPath)) { showFolderTree = false; return; }

        // ルート自身を先頭に追加
        folderCandidates.Add(rootPath);

        // 配下のすべてのサブフォルダを収集
        if (AssetDatabase.IsValidFolder(rootPath))
            CollectSubFolders(rootPath, folderCandidates);

        // 1つしかない（ルートのみ）場合はツリーUI不要
        showFolderTree = (folderCandidates.Count > 1);

        // 登録状態をキャッシュ化（索引経由なのでフォルダ数ぶんの辞書引きで済む）
        folderRegisteredCache = new bool[folderCandidates.Count];
        for (int i = 0; i < folderCandidates.Count; i++)
        {
            var found = BManagerRegistry.FindByPath(folderCandidates[i]);
            folderRegisteredCache[i] = (found != null && found != existingData);
        }

        // ルートが既に登録済みなら、最初の未登録フォルダを初期選択にして操作不能を避ける
        if (folderRegisteredCache.Length > 0 && folderRegisteredCache[0])
        {
            for (int i = 1; i < folderRegisteredCache.Length; i++)
            {
                if (!folderRegisteredCache[i]) { selectedFolderIndex = i; break; }
            }
        }
    }

    /// <summary>
    /// サブフォルダを再帰収集する。
    /// 以前は FindAssets("", path) でサブツリー全体を毎回走査していたため、
    /// 大きなアセットでは登録画面を開くだけで体感できるほど重かった。
    /// AssetDatabase.GetSubFolders は直下のフォルダだけを返すので無駄がない。
    /// </summary>
    private static void CollectSubFolders(string path, List<string> result)
    {
        string[] subs = AssetDatabase.GetSubFolders(path);
        if (subs == null) return;
        foreach (string sub in subs.OrderBy(x => x))
        {
            result.Add(sub);
            CollectSubFolders(sub, result);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  重複登録の検出
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 選択中のフォルダが既に別のデータとして登録されていないか調べる。
    /// Layout イベントでのみ更新し、同じフレーム内でコントロール数が変わらないようにする。
    /// </summary>
    private void RefreshDuplicateState()
    {
        Object t = GetSelectedTarget();
        if (t == lastDupTarget) return;
        lastDupTarget  = t;
        duplicateEntry = BManagerRegistry.FindDuplicate(t, existingData);
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
        EnsureStyles();
        if (Event.current.type == EventType.Layout) RefreshDuplicateState();

        // 高さを 9999f にすることで BeginArea がコンテンツを縦方向に制限しない
        // → GUILayoutUtility.GetLastRect() で実際のコンテンツ高さを計測できる
        GUILayout.BeginArea(new Rect(0, 0, rect.width, 9999f));
        GUILayout.Space(8);

        // ── 基本情報 ─────────────────────────────────────────────
        bool linkBroken = (targetAsset == null);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 紐づけ先が失われている場合のみ編集可にして、再リンクできるようにする
            using (new EditorGUI.DisabledScope(!linkBroken))
            {
                Object next = EditorGUILayout.ObjectField(
                    BManagerL10n.Tr("popup.target"), targetAsset, typeof(Object), false);
                if (linkBroken && next != targetAsset)
                {
                    targetAsset = next;
                    BuildFolderCandidates(next);
                    lastDupTarget = null;
                }
            }

            inputTitle = EditorGUILayout.TextField(BManagerL10n.Tr("popup.itemName"), inputTitle);

            string rawUrl = EditorGUILayout.TextField(BManagerL10n.Tr("popup.itemUrl"), inputUrl);
            inputUrl = rawUrl != null ? rawUrl.Trim() : "";

            var categoryLabel = new GUIContent(BManagerL10n.Tr("popup.category"),
                IsBoothUrl(inputUrl)
                    ? BManagerL10n.Tr("popup.category.tipBooth")
                    : BManagerL10n.Tr("popup.category.tipManual"));
            selectedCategoryIndex = EditorGUILayout.Popup(categoryLabel, selectedCategoryIndex, categoryLabels);
        }

        if (linkBroken)
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(BManagerL10n.Tr("popup.relinkHint"), MessageType.Warning, true);
        }

        // URLが変わっていれば自動取得の可否判定を予約する
        HandleUrlChanged();

        // ── ユーザータグ ─────────────────────────────────────────
        GUILayout.Space(4);
        DrawUserTags();

        // ── フォルダ階層選択ツリー ────────────────────────────────
        if (showFolderTree)
        {
            GUILayout.Space(4);
            DrawFolderTree();
        }

        // ボタン操作はレイアウトを閉じたあとで実行する（IMGUIのグループを壊さないため）
        bool openDuplicate = false;
        bool doSaveOnly    = false;
        bool doCancel      = false;

        // ── 重複登録の警告 ───────────────────────────────────────
        bool isDuplicate = (duplicateEntry != null);
        if (isDuplicate)
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                BManagerL10n.Tr("popup.dup", duplicateEntry.itemName), MessageType.Error, true);
            if (GUILayout.Button(BManagerL10n.Tr("popup.dup.open"), GUILayout.Height(22)))
                openDuplicate = true;
        }

        // ── サムネイルオプション ──────────────────────────────────
        GUILayout.Space(4);
        DrawThumbnailOptions();

        // ── 自動取得の可否 ────────────────────────────────────────
        GUILayout.Space(4);
        DrawCheckStatus();

        // ── ボタン群 ──────────────────────────────────────────────
        GUILayout.Space(8);
        bool isBooth  = IsBoothUrl(inputUrl);
        bool isSaving = (checkState == CheckState.Saving);
        bool canSave  = !isSaving && !isDuplicate && (GetSelectedTarget() != null);

        using (new EditorGUI.DisabledScope(!canSave))
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!isBooth))
            {
                if (GUILayout.Button(BManagerL10n.Tr("popup.btn.fetchSave"), GUILayout.Height(30)))
                    _ = RunFetchAndSaveFromUI();
            }
            if (GUILayout.Button(BManagerL10n.Tr("popup.btn.saveOnly"), GUILayout.Height(30)))
                doSaveOnly = true;
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button(BManagerL10n.Tr("common.cancel"), GUILayout.Height(20)))
            doCancel = true;

        // ── 動的サイズ計測 ──────────────────────────────────────
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

        // ── レイアウト確定後に実行する操作 ──────────────────────
        if (openDuplicate && duplicateEntry != null)
        {
            var dup = duplicateEntry;
            EditorApplication.delayCall += () => ShowPopup(dup.linkedAsset, dup);
        }
        else if (doSaveOnly)
        {
            SaveInputOnly();
        }
        else if (doCancel)
        {
            this.Close();
        }
    }

    // ── ユーザータグ描画 ──────────────────────────────────────────

    private void DrawUserTags()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                BManagerL10n.C("popup.userTags", "popup.userTags.tip"), EditorStyles.boldLabel);

            // Enterキーはテキストフィールドを描画する前に拾う必要がある
            bool enterPressed =
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == TAG_FIELD_CONTROL;

            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(TAG_FIELD_CONTROL);
            newTagInput = EditorGUILayout.TextField(newTagInput);
            bool addClicked = GUILayout.Button(
                BManagerL10n.Tr("common.add"), EditorStyles.miniButton, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            if (addClicked || enterPressed)
            {
                AddUserTag(newTagInput);
                newTagInput = "";
                if (enterPressed) Event.current.Use();
                GUI.FocusControl(TAG_FIELD_CONTROL);
            }

            if (userTags.Count == 0)
            {
                EditorGUILayout.LabelField(
                    BManagerL10n.Tr("popup.userTags.hint"), EditorStyles.miniLabel);
            }
            else
            {
                // 固定幅ウィンドウなので、累積幅を見ながら手動で折り返す
                float available = BASE_WIDTH - 50f;
                float used = 0f;
                int removeIndex = -1;

                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < userTags.Count; i++)
                {
                    var content = new GUIContent(userTags[i] + "  ×");
                    float w = EditorStyles.miniButton.CalcSize(content).x + 6f;
                    if (used > 0f && used + w > available)
                    {
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        used = 0f;
                    }
                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(w)))
                        removeIndex = i;
                    used += w;
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (removeIndex >= 0) userTags.RemoveAt(removeIndex);
            }
        }
    }

    private void AddUserTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        tag = tag.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (userTags.Contains(tag)) return;
        userTags.Add(tag);
    }

    // ── フォルダ階層ツリー描画 ────────────────────────────────────

    private void DrawFolderTree()
    {
        EditorGUILayout.LabelField(BManagerL10n.Tr("popup.folderSelect"), EditorStyles.boldLabel);

        float treeHeight = Mathf.Min(folderCandidates.Count * 20f + 4f, 120f);
        folderScrollPos = EditorGUILayout.BeginScrollView(folderScrollPos,
            EditorStyles.helpBox, GUILayout.Height(treeHeight));

        string root = folderCandidates.Count > 0 ? folderCandidates[0] : "";

        for (int i = 0; i < folderCandidates.Count; i++)
        {
            string path     = folderCandidates[i];
            bool registered = (folderRegisteredCache != null && i < folderRegisteredCache.Length)
                ? folderRegisteredCache[i] : false;

            // インデント計算（ルートからの深さ）
            int depth = (path == root) ? 0
                : path.Substring(root.Length).TrimStart('/').Count(c => c == '/') + 1;
            float indent = depth * 14f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);

            using (new EditorGUI.DisabledScope(registered))
            {
                GUIStyle style = registered ? _folderLabelRegistered
                               : (i == selectedFolderIndex ? _folderLabelSelected : _folderLabel);

                string label = System.IO.Path.GetFileName(path)
                    + (registered ? BManagerL10n.Tr("popup.registered") : "");

                bool sel  = (selectedFolderIndex == i);
                bool next = EditorGUILayout.ToggleLeft(label, sel, style);
                if (next && !registered && next != sel)
                {
                    selectedFolderIndex = i;
                    lastDupTarget = null;   // 選択が変わったので重複判定をやり直す
                }
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
                BManagerL10n.Tr("popup.autoThumb"), autoTopThumbnail);
            if (EditorGUI.EndChangeCheck())
            {
                // チェックを外したとき & URLが有効ならプレビューを取得
                if (!autoTopThumbnail && IsBoothUrl(inputUrl))
                    _ = FetchThumbnailPreviews();
                else
                    showThumbnailSelector = false;
            }

            if (!autoTopThumbnail)
            {
                if (showThumbnailSelector)
                    DrawThumbnailSelector();
                else if (isFetchingThumbnails)
                    EditorGUILayout.LabelField(
                        BManagerL10n.Tr("popup.thumbFetching"), EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(
                        BManagerL10n.Tr("popup.thumbNone"), EditorStyles.miniLabel);
            }
        }
    }

    private void DrawThumbnailSelector()
    {
        EditorGUILayout.LabelField(
            BManagerL10n.Tr("popup.thumbSelect", selectedThumbnailIndex + 1, thumbnailUrls.Count),
            EditorStyles.miniLabel);

        float previewSize  = 64f;
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
                GUILayout.Box("...", GUILayout.Width(previewSize), GUILayout.Height(previewSize));

            bool sel  = (selectedThumbnailIndex == i);
            bool next = EditorGUILayout.ToggleLeft(
                BManagerL10n.Tr("popup.image", i + 1), sel, GUILayout.ExpandWidth(true));
            if (next && next != sel) selectedThumbnailIndex = i;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // ─── サムネイル候補のプレビュー取得 ───────────────────────────

    /// <summary>
    /// 候補URLは可否判定のJSONから既に得られているため、ここでは画像だけを取る。
    /// 一覧表示用に小さいサイズで取得して待ち時間と転送量を抑える。
    /// </summary>
    private async Task FetchThumbnailPreviews()
    {
        // 判定がまだ終わっていない場合は先に商品情報を取る
        if (thumbnailUrls.Count == 0)
        {
            isFetchingThumbnails = true;
            Repaint();

            var info = await BManagerBoothApi.FetchItem(inputUrl);
            if (!info.ok)
            {
                isFetchingThumbnails = false;
                checkMessage = info.error;
                checkState   = CheckState.Unavailable;
                Repaint();
                return;
            }
            cachedInfo    = info;
            cachedInfoUrl = inputUrl;
            thumbnailUrls = new List<string>(info.imageUrls);
            PreselectPreviousThumbnail();
        }

        isFetchingThumbnails  = true;
        showThumbnailSelector = false;
        thumbnailPreviews     = new List<Texture2D>(new Texture2D[thumbnailUrls.Count]);
        Repaint();

        try
        {
            var tasks = thumbnailUrls
                .Select((url, idx) => FetchPreview(
                    BManagerBoothApi.BuildSizedUrl(url, BManagerBoothApi.PREVIEW_IMAGE_SIZE), idx))
                .ToList();
            await Task.WhenAll(tasks);
            showThumbnailSelector = (thumbnailUrls.Count > 0);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[B-Manager] サムネイル一覧取得失敗: " + e.Message);
        }
        finally
        {
            isFetchingThumbnails = false;
            Repaint();
        }
    }

    private async Task FetchPreview(string imgUrl, int index)
    {
        Texture2D tex = await BManagerBoothApi.DownloadTexture(imgUrl);
        if (tex != null && index < thumbnailPreviews.Count) thumbnailPreviews[index] = tex;
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

    private string RootFolderPath()
    {
        return (folderCandidates.Count > 0) ? folderCandidates[0] : "";
    }

    private void SaveInputOnly()
    {
        PerformSave(new SaveRequest
        {
            title          = inputTitle,
            url            = inputUrl,
            target         = GetSelectedTarget(),
            data           = existingData,
            manualCategory = SelectedCategory,
            keepCategory   = false,
            rootFolderPath = RootFolderPath(),
            userTags       = new List<string>(userTags),
        });
        this.Close();
    }

    private async Task RunFetchAndSaveFromUI()
    {
        checkState = CheckState.Saving;
        Repaint();

        string useThumbnailUrl = autoTopThumbnail ? "" : GetSelectedThumbnailUrl();

        // 可否判定で取得済みの情報があれば、同じリクエストを繰り返さない
        var preFetched = (cachedInfoUrl == inputUrl) ? cachedInfo : null;

        FetchResult result = await StaticFetchAndSave(
            existingData, true, inputUrl, inputTitle,
            GetSelectedTarget(), SelectedCategory,
            autoTopThumbnail, useThumbnailUrl, RootFolderPath(),
            preFetched, new List<string>(userTags));

        if (result.success)
        {
            this.Close();
            return;
        }

        // 失敗時は閉じずに理由を表示する
        checkState   = CheckState.Unavailable;
        checkMessage = result.error;
        Repaint();
    }

    /// <summary>選択中のサムネイルを og:image と同じ620x620で取得するURL。</summary>
    private string GetSelectedThumbnailUrl()
    {
        if (thumbnailUrls.Count == 0) return "";
        string original = thumbnailUrls[Mathf.Clamp(selectedThumbnailIndex, 0, thumbnailUrls.Count - 1)];
        return BManagerBoothApi.BuildSizedUrl(original, BManagerBoothApi.MAIN_IMAGE_SIZE);
    }

    // ── 静的保存メソッド ──────────────────────────────────────────

    public class FetchResult
    {
        public bool   success;
        public string error;
    }

    /// <summary>
    /// BOOTHから商品情報を取得して保存する。
    /// 取得は .json エンドポイント1本（BManagerBoothApi）で完結するため、
    /// 1アイテムあたりのリクエストは「商品情報1件 + サムネイル画像1件」で済む。
    /// </summary>
    public static async Task<FetchResult> StaticFetchAndSave(
        BManagerData data,
        bool keepCategory,
        string overrideUrl        = "",
        string overrideTitle      = "",
        Object overrideTarget     = null,
        string manualCategory     = null,
        bool   autoTop            = true,
        string forceThumbnailUrl  = "",
        string rootFolderPath     = "",
        BManagerBoothApi.ItemInfo preFetched = null,
        List<string> userTagsOverride        = null)
    {
        // 編集画面で書き換えたURLを優先する（一括再取得では未指定なので既存値を使う）
        string url = !string.IsNullOrEmpty(overrideUrl) ? overrideUrl
                   : ((data != null) ? data.itemUrl : "");
        if (!IsBoothUrl(url))
            return new FetchResult { success = false, error = BManagerL10n.Tr("err.notBoothUrl") };

        try
        {
            BManagerBoothApi.ItemInfo info = preFetched ?? await BManagerBoothApi.FetchItem(url);
            if (!info.ok)
            {
                Debug.LogWarning("[B-Manager] 自動取得に失敗しました: " + url + "\n" + info.error);
                return new FetchResult { success = false, error = info.error };
            }

            string title = !string.IsNullOrEmpty(info.name) ? info.name
                         : (!string.IsNullOrEmpty(overrideTitle) ? overrideTitle
                            : (data != null ? data.itemName : ""));

            // ── 使用するサムネイルURLを決める ───────────────────
            string thumbUrl = (!autoTop && !string.IsNullOrEmpty(forceThumbnailUrl))
                ? forceThumbnailUrl
                : info.MainThumbnailUrl;

            // 前回使っていた画像が今も商品ページに存在するなら、それを優先して維持する
            string prevThumbUrl = (data != null) ? (data.previousThumbnailUrl ?? "") : forceThumbnailUrl;
            if (!string.IsNullOrEmpty(prevThumbUrl) && prevThumbUrl != thumbUrl)
            {
                string prevKey = BManagerBoothApi.CanonicalImageKey(prevThumbUrl);
                if (info.imageUrls.Any(u => BManagerBoothApi.CanonicalImageKey(u) == prevKey))
                    thumbUrl = prevThumbUrl;
            }

            Texture2D thumb = await BManagerBoothApi.DownloadTexture(thumbUrl);

            Object target = (data != null && data.linkedAsset != null) ? data.linkedAsset : overrideTarget;

            PerformSave(new SaveRequest
            {
                title          = title,
                thumbnail      = thumb,
                thumbnailUrl   = thumbUrl,
                url            = url,
                target         = target,
                data           = data,
                manualCategory = manualCategory,
                keepCategory   = keepCategory,
                rootFolderPath = rootFolderPath,
                info           = info,
                userTags       = userTagsOverride,
            });

            return new FetchResult { success = true };
        }
        catch (System.Exception e)
        {
            Debug.LogError("[B-Manager Error] " + e.Message);
            return new FetchResult { success = false, error = e.Message };
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  保存本体
    // ═══════════════════════════════════════════════════════════

    private class SaveRequest
    {
        public string       title;
        public Texture2D    thumbnail;
        public string       thumbnailUrl;
        public string       url;
        public Object       target;
        public BManagerData data;
        public string       manualCategory;
        public bool         keepCategory;
        public string       rootFolderPath;

        /// <summary>BOOTHから取得した商品情報。null ならBOOTH情報なしの手動保存。</summary>
        public BManagerBoothApi.ItemInfo info;

        /// <summary>null なら既存のユーザータグを変更しない（再取得で消さないため）。</summary>
        public List<string> userTags;
    }

    private static void PerformSave(SaveRequest req)
    {
        string title = req.title;
        if (string.IsNullOrEmpty(title))
            title = (req.target != null) ? req.target.name : "Untitled";

        BManagerData data = req.data;
        bool isNew = (data == null);

        if (isNew)
        {
            data = CreateInstance<BManagerData>();
            data.registrationTimestamp = System.DateTime.Now.Ticks;
        }
        else
        {
            // 一括再取得で意図しない結果になった場合に取り消せるようにする
            Undo.RecordObject(data, "B-Manager Save");
        }

        data.itemName    = title;
        data.itemUrl     = req.url ?? "";
        data.linkedAsset = req.target;
        if (!string.IsNullOrEmpty(req.thumbnailUrl))
            data.previousThumbnailUrl = req.thumbnailUrl;

        // ── カテゴリー（BOOTHを最優先で同期） ───────────────────
        if (req.info != null && !string.IsNullOrEmpty(req.info.category))
            data.tags = new List<string> { req.info.category };
        else if (isNew || !req.keepCategory)
            data.tags = new List<string> {
                string.IsNullOrEmpty(req.manualCategory) ? BManagerData.UNCATEGORIZED : req.manualCategory };
        else if (data.tags == null || data.tags.Count == 0)
            data.tags = new List<string> { BManagerData.UNCATEGORIZED };

        // ── ユーザータグ（BOOTHとは独立。null なら触らない） ────
        if (req.userTags != null)
            data.userTags = new List<string>(req.userTags);
        else if (data.userTags == null)
            data.userTags = new List<string>();

        // ── BOOTH商品情報 ───────────────────────────────────────
        if (req.info != null)
        {
            data.shopName           = req.info.shopName ?? "";
            data.shopUrl            = req.info.shopUrl ?? "";
            data.priceText          = req.info.priceText ?? "";
            data.publishedAt        = req.info.publishedAt ?? "";
            data.publishedTimestamp = req.info.publishedTicks;
            data.isEndOfSale        = req.info.isEndOfSale;
            data.isSoldOut          = req.info.isSoldOut;
        }

        BManagerPaths.EnsureSaveFolder();

        string path = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(path))
        {
            path = AssetDatabase.GenerateUniqueAssetPath(
                BManagerPaths.SAVE_PATH + "/" + BManagerPaths.SafeFileName(title) + ".asset");
            AssetDatabase.CreateAsset(data, path);
        }

        if (req.thumbnail != null)
        {
            Texture2D newThumb = MakeThumbnailTexture(req.thumbnail);
            DestroyImmediate(req.thumbnail);

            newThumb.name      = "Thumbnail";
            newThumb.hideFlags = HideFlags.HideInHierarchy;

            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is Texture2D && !AssetDatabase.IsMainAsset(sub))
                    DestroyImmediate(sub, true);

            AssetDatabase.AddObjectToAsset(newThumb, data);
            data.thumbnail = newThumb;
        }

        // ── Projectウィンドウのハイライト（上位2階層） ──────────
        // rootFolderPath は登録画面からの保存時のみ渡される。
        // 一括再取得や単体の再取得ではハイライトを動かさない（登録操作ではないため）。
        if (!string.IsNullOrEmpty(req.rootFolderPath))
        {
            string selectedPath = (req.target != null) ? AssetDatabase.GetAssetPath(req.target) : "";
            BManagerHighlight.Set(req.rootFolderPath, selectedPath);
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.SaveAssets();

        BManagerRegistry.Invalidate();
        BManagerBackup.WriteBackup(data);
    }

    /// <summary>
    /// .asset に埋め込むサムネイルを作る。
    /// 原寸をそのまま RGBA32 で持つとアイテム数に比例してメモリを食うため、
    /// 最大辺 MAX_THUMBNAIL_SIZE まで縮小し、アルファ不要の RGB24 で保持する。
    /// </summary>
    private static Texture2D MakeThumbnailTexture(Texture2D src)
    {
        int w = Mathf.Max(1, src.width);
        int h = Mathf.Max(1, src.height);

        float scale = Mathf.Min(1f, (float)MAX_THUMBNAIL_SIZE / Mathf.Max(w, h));
        int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
        int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

        RenderTexture rt = RenderTexture.GetTemporary(
            nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var result = new Texture2D(nw, nh, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
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
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj))) continue;

            // 既に登録済みなら新規登録ではなく既存データの編集として開く
            ShowPopup(obj, BManagerRegistry.FindByAsset(obj));
            return;
        }
    }

    [MenuItem("Assets/Register to B-Manager", true, 2000)]
    private static bool ValidateRegisterFromContextMenu()
    {
        Object[] selectedObjects = Selection.GetFiltered<Object>(SelectionMode.Assets);
        foreach (Object obj in selectedObjects)
        {
            if (obj is MonoScript) continue;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj))) return true;
        }
        return false;
    }
}

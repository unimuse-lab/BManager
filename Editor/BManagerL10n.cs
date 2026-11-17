using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UI文字列のローカライズ。
///
/// 【設計方針】
/// ・保存されるデータ（カテゴリー名・ユーザータグ）は言語設定に依存させない。
///   表示だけを切り替えるため、BManagerData.UNCATEGORIZED のような「保存上のキー」は
///   常に日本語固定のまま、描画時に Tr() を通して表示名へ変換する。
/// ・キーが辞書に無い場合はキー自体を返す（表示が消えて原因不明になるのを避ける）。
/// </summary>
public static class BManagerL10n
{
    public const string PREF_LANG = "BManager_Language";

    public enum Lang { Japanese = 0, English = 1 }

    private static Lang? _cached;

    public static Lang Current
    {
        get
        {
            if (_cached == null)
                _cached = (Lang)EditorPrefs.GetInt(PREF_LANG, (int)Lang.Japanese);
            return _cached.Value;
        }
        set
        {
            _cached = value;
            EditorPrefs.SetInt(PREF_LANG, (int)value);
        }
    }

    public static bool IsEnglish { get { return Current == Lang.English; } }

    /// <summary>翻訳を引く。</summary>
    public static string Tr(string key)
    {
        string[] v;
        if (!Table.TryGetValue(key, out v)) return key;
        int i = (int)Current;
        if (i < 0 || i >= v.Length || string.IsNullOrEmpty(v[i])) return v[0];
        return v[i];
    }

    /// <summary>翻訳を引いて string.Format する。</summary>
    public static string Tr(string key, params object[] args)
    {
        return string.Format(Tr(key), args);
    }

    /// <summary>ラベル用 GUIContent を作る。tooltipKey を省略すると tooltip なし。</summary>
    public static GUIContent C(string key, string tooltipKey = null)
    {
        return new GUIContent(Tr(key), string.IsNullOrEmpty(tooltipKey) ? null : Tr(tooltipKey));
    }

    /// <summary>
    /// カテゴリー名の表示用変換。
    /// BOOTH由来のカテゴリー名は翻訳辞書を持たないのでそのまま返し、
    /// 「未分類」のような内部キーだけを表示言語へ合わせる。
    /// </summary>
    public static string Category(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return Tr("category.uncategorized");
        if (stored == BManagerData.UNCATEGORIZED) return Tr("category.uncategorized");
        return stored;
    }

    // ═══════════════════════════════════════════════════════════
    //  辞書  [0] = 日本語 / [1] = English
    // ═══════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        // ── 共通 ──────────────────────────────────────────────
        { "common.execute",        new[] { "実行", "Run" } },
        { "common.cancel",         new[] { "キャンセル", "Cancel" } },
        { "common.delete",         new[] { "削除", "Delete" } },
        { "common.close",          new[] { "閉じる", "Close" } },
        { "common.ok",             new[] { "OK", "OK" } },
        { "common.add",            new[] { "追加", "Add" } },
        { "category.uncategorized",new[] { "未分類", "Uncategorized" } },
        { "tab.all",               new[] { "全て", "All" } },

        // ── ツールバー ────────────────────────────────────────
        { "toolbar.clear",         new[] { "Clear", "Clear" } },
        { "toolbar.autoOpen",      new[] { "Auto Open", "Auto Open" } },
        { "toolbar.autoOpen.tip",  new[] { "アセットのインポート時に自動で登録画面を開く設定です",
                                           "Automatically opens the register window when an asset is imported" } },
        { "toolbar.bulk",          new[] { "一括再取得", "Refetch all" } },
        { "toolbar.bulk.tip",      new[] { "リスト内の全BOOTHアイテムの情報（アイテム名・サムネイル・カテゴリー）を再取得します",
                                           "Refetches name, thumbnail and category for every BOOTH item in the list" } },
        { "toolbar.sort.tip",      new[] { "並び順を変更します", "Change the sort order" } },
        { "toolbar.settings.tip",  new[] { "設定", "Settings" } },
        { "toolbar.tools.tip",     new[] { "書き出し・読み込み", "Export / Import" } },
        { "toolbar.tagFilter.tip", new[] { "ユーザータグで絞り込みます", "Filter by user tag" } },

        // ── 並び替え ──────────────────────────────────────────
        { "sort.nameAsc",          new[] { "名前順 / 昇順", "Name / Ascending" } },
        { "sort.nameDesc",         new[] { "名前順 / 降順", "Name / Descending" } },
        { "sort.dateNew",          new[] { "登録順 / 新しい順", "Registered / Newest first" } },
        { "sort.dateOld",          new[] { "登録順 / 古い順", "Registered / Oldest first" } },
        { "sort.pubNew",           new[] { "BOOTH公開日 / 新しい順", "Published on BOOTH / Newest first" } },
        { "sort.pubOld",           new[] { "BOOTH公開日 / 古い順", "Published on BOOTH / Oldest first" } },
        { "sort.priceHigh",        new[] { "価格 / 高い順", "Price / Highest first" } },
        { "sort.priceLow",         new[] { "価格 / 安い順", "Price / Lowest first" } },

        // ── タグ絞り込み ──────────────────────────────────────
        { "filter.tag.all",        new[] { "すべて（絞り込みなし）", "All (no filter)" } },
        { "filter.tag.none",       new[] { "タグなし", "No tag" } },
        { "filter.tag.label",      new[] { "タグ: {0}", "Tag: {0}" } },
        { "filter.tag.button",     new[] { "タグ", "Tag" } },

        // ── 設定パネル ────────────────────────────────────────
        { "settings.title",        new[] { "設定", "Settings" } },
        { "settings.highlight",    new[] { "最後に追加されたフォルダをハイライト",
                                           "Highlight the most recently registered folder" } },
        { "settings.highlight.tip",new[] { "登録直後のフォルダに色を付けてProjectツリー上で識別しやすくします。上位2階層（例: ショップ名 / 商品名）を対象にします。",
                                           "Tints the folder you just registered in the Project tree. Covers the top two levels (e.g. shop name / item name)." } },
        { "settings.highlightColor",new[] { "ハイライトカラー", "Highlight color" } },
        { "settings.clearHighlight",new[] { "ハイライトをクリア", "Clear highlight" } },
        { "settings.language",     new[] { "表示言語 / Language", "表示言語 / Language" } },
        { "settings.backup",       new[] { "保存時にJSONバックアップを作成", "Write a JSON backup on save" } },
        { "settings.backup.tip",   new[] { "アイテム保存時に プロジェクトルート直下の UniMuseData/B-Manager/Backup（Assets の外）へテキスト(JSON)の控えを書き出します。.asset が壊れても内容を復元できます。",
                                           "Writes a JSON copy to UniMuseData/B-Manager/Backup (outside Assets) on every save so the data survives a broken .asset file." } },
        { "settings.openBackup",   new[] { "バックアップフォルダを開く", "Open backup folder" } },

        // ── ツールメニュー ────────────────────────────────────
        { "tools.exportJson",      new[] { "JSONで書き出し...", "Export as JSON..." } },
        { "tools.exportCsv",       new[] { "CSVで書き出し...", "Export as CSV..." } },
        { "tools.importJson",      new[] { "JSONから読み込み...", "Import from JSON..." } },
        { "tools.importCsv",       new[] { "CSVから読み込み...", "Import from CSV..." } },
        { "tools.restoreBackup",   new[] { "バックアップから復元", "Restore from backup" } },
        { "tools.openBackup",      new[] { "バックアップフォルダを開く", "Open backup folder" } },

        // ── アイテム行 ────────────────────────────────────────
        { "item.noImage",          new[] { "No Image", "No Image" } },
        { "item.category",         new[] { "カテゴリー", "Category" } },
        { "item.missingAsset",     new[] { "アセットが見つかりません（リンク切れ）",
                                           "Linked asset is missing" } },
        { "badge.endOfSale",       new[] { "販売終了", "No longer on sale" } },
        { "badge.soldOut",         new[] { "売り切れ", "Sold out" } },

        // ── 右クリックメニュー ────────────────────────────────
        { "menu.openUrl",          new[] { "URLを開く", "Open item URL" } },
        { "menu.openShop",         new[] { "ショップページを開く", "Open shop page" } },
        { "menu.edit",             new[] { "編集", "Edit" } },
        { "menu.refetch",          new[] { "再取得（カテゴリー同期）", "Refetch (sync category)" } },
        { "menu.refetchDisabled",  new[] { "再取得（BOOTHのみ）", "Refetch (BOOTH only)" } },
        { "menu.relink",           new[] { "Projectで選択中のアセットに再リンク",
                                           "Relink to the asset selected in Project" } },
        { "menu.relinkDisabled",   new[] { "再リンク（Projectでアセットを選択してください）",
                                           "Relink (select an asset in Project first)" } },
        { "menu.removeFromList",   new[] { "リストから削除", "Remove from list" } },
        { "menu.deleteAsset",      new[] { "アセット本体も削除", "Delete the asset too" } },
        { "menu.copyUrl",          new[] { "URLをコピー", "Copy URL" } },

        // ── ダイアログ ────────────────────────────────────────
        { "dialog.bulk.title",     new[] { "一括更新", "Bulk update" } },
        { "dialog.bulk.body",      new[] { "全アイテム（{0}件）の情報を再取得し、カテゴリーをBOOTHと同期します。\nユーザータグは変更されません。",
                                           "Refetches all {0} items and syncs their category with BOOTH.\nUser tags are left untouched." } },
        { "dialog.delete.title",   new[] { "削除確認", "Confirm delete" } },
        { "dialog.delete.body",    new[] { "「{0}」のB-Managerデータを削除します。\nこの操作は元に戻せません。",
                                           "The B-Manager entry for \"{0}\" will be deleted.\nThis cannot be undone." } },
        { "dialog.deleteAsset.body",new[] { "「{0}」のB-Managerデータと、インポートフォルダ本体を削除します。\n\n削除対象: {1}\n\nこの操作は元に戻せません。",
                                           "The B-Manager entry for \"{0}\" and the imported folder itself will be deleted.\n\nTarget: {1}\n\nThis cannot be undone." } },
        { "dialog.relink.title",   new[] { "再リンク", "Relink" } },
        { "dialog.relink.body",    new[] { "「{0}」の紐づけ先を\n{1}\nに変更します。",
                                           "The link for \"{0}\" will be changed to\n{1}" } },
        { "progress.bulk.title",   new[] { "一括再取得", "Refetching" } },
        { "progress.bulk.body",    new[] { "({0}/{1}) {2}", "({0}/{1}) {2}" } },

        // ── 登録・編集画面 ────────────────────────────────────
        { "popup.title",           new[] { "アイテム登録・編集", "Register / Edit item" } },
        { "popup.target",          new[] { "登録対象", "Target asset" } },
        { "popup.itemName",        new[] { "アイテム名", "Item name" } },
        { "popup.itemUrl",         new[] { "商品URL", "Item URL" } },
        { "popup.category",        new[] { "カテゴリー", "Category" } },
        { "popup.category.tipBooth",new[] { "BOOTHのURLの場合、情報取得時にBOOTH側のカテゴリーで上書きされます",
                                            "For a BOOTH URL this is overwritten with the category from BOOTH when fetching" } },
        { "popup.category.tipManual",new[] { "手動でカテゴリーを指定します", "Set the category manually" } },
        { "popup.userTags",        new[] { "ユーザータグ", "User tags" } },
        { "popup.userTags.tip",    new[] { "BOOTHのカテゴリーとは別の自由分類です。再取得しても消えません。",
                                           "A free-form classification separate from the BOOTH category. Refetching will not clear it." } },
        { "popup.userTags.hint",   new[] { "タグを入力して Enter または「追加」",
                                           "Type a tag and press Enter or Add" } },
        { "popup.userTags.empty",  new[] { "タグなし", "No tags" } },
        { "popup.folderSelect",    new[] { "紐づけるフォルダを選択", "Choose the folder to link" } },
        { "popup.registered",      new[] { "  ✓登録済み", "  ✓ registered" } },
        { "popup.autoThumb",       new[] { "トップサムネイルを自動取得（推奨）",
                                           "Use the main thumbnail automatically (recommended)" } },
        { "popup.thumbFetching",   new[] { "サムネイル一覧を取得中...", "Loading thumbnails..." } },
        { "popup.thumbNone",       new[] { "サムネイルが見つかりませんでした。", "No thumbnails found." } },
        { "popup.thumbSelect",     new[] { "使用するサムネイルを選択: ({0}/{1})",
                                           "Choose the thumbnail to use: ({0}/{1})" } },
        { "popup.image",           new[] { "画像 {0}", "Image {0}" } },
        { "popup.btn.fetchSave",   new[] { "情報を取得して保存", "Fetch and save" } },
        { "popup.btn.saveOnly",    new[] { "入力内容のみで保存", "Save input only" } },
        { "popup.relinkHint",      new[] { "紐づけ先のアセットが見つかりません。Projectからフォルダをドラッグして再設定してください。",
                                           "The linked asset is missing. Drag a folder from the Project window to relink it." } },

        // ── 重複登録 ──────────────────────────────────────────
        { "popup.dup",             new[] { "このフォルダは「{0}」として既に登録されています。\n重複登録を避けるため保存できません。既存データを編集してください。",
                                           "This folder is already registered as \"{0}\".\nSaving is blocked to avoid a duplicate entry. Edit the existing entry instead." } },
        { "popup.dup.open",        new[] { "既に登録済みのデータを開く", "Open the existing entry" } },

        // ── 取得可否表示 ──────────────────────────────────────
        { "check.empty",           new[] { "BOOTHの商品URLを入力すると、自動取得が可能かどうかをここに表示します。",
                                           "Enter a BOOTH item URL and this box will tell you whether it can be fetched." } },
        { "check.notBooth",        new[] { "自動取得: 不可（BOOTH以外のURL）\n「入力内容のみで保存」で手動登録してください。",
                                           "Fetch: not available (not a BOOTH URL)\nUse \"Save input only\" to register it manually." } },
        { "check.checking",        new[] { "自動取得: 判定中...", "Fetch: checking..." } },
        { "check.available",       new[] { "自動取得: 可能\nアイテム名: {0}\nカテゴリー: {1}\nショップ: {2}\n価格: {3}",
                                           "Fetch: available\nItem: {0}\nCategory: {1}\nShop: {2}\nPrice: {3}" } },
        { "check.unavailable",     new[] { "自動取得: 不可\n{0}", "Fetch: not available\n{0}" } },
        { "check.saving",          new[] { "情報を取得して保存中...", "Fetching and saving..." } },
        { "check.catUnavailable",  new[] { "取得できません（上のカテゴリー欄の値を使用）",
                                           "unavailable (the category chosen above will be used)" } },
        { "check.endOfSale",       new[] { "\n※この商品は販売終了しています。",
                                           "\nNote: this item is no longer on sale." } },
        { "check.soldOut",         new[] { "\n※この商品は売り切れです。",
                                           "\nNote: this item is sold out." } },
        { "check.unknown",         new[] { "不明", "unknown" } },

        // ── エラー ────────────────────────────────────────────
        { "err.notBoothUrl",       new[] { "BOOTHの商品URLではありません。", "This is not a BOOTH item URL." } },
        { "err.http",              new[] { "BOOTHへの通信に失敗しました (HTTP {0}): {1}",
                                           "Request to BOOTH failed (HTTP {0}): {1}" } },
        { "err.notFound",          new[] { "商品が見つかりません (HTTP 404)。削除された、または非公開の可能性があります。",
                                           "Item not found (HTTP 404). It may have been removed or made private." } },
        { "err.emptyBody",         new[] { "BOOTHの応答が空でした (HTTP {0})。", "BOOTH returned an empty response (HTTP {0})." } },
        { "err.parse",             new[] { "BOOTHの応答から商品情報を読み取れませんでした。",
                                           "Could not read the item information from BOOTH's response." } },

        // ── 書き出し・読み込み ────────────────────────────────
        { "export.title",          new[] { "B-Manager データを書き出し", "Export B-Manager data" } },
        { "export.done.title",     new[] { "書き出し完了", "Export complete" } },
        { "export.done",           new[] { "{0}件を書き出しました。\n\n{1}", "Exported {0} items.\n\n{1}" } },
        { "export.none",           new[] { "書き出せるアイテムがありません。", "There are no items to export." } },
        { "import.title",          new[] { "B-Manager データを読み込み", "Import B-Manager data" } },
        { "import.confirm.title",  new[] { "読み込み確認", "Confirm import" } },
        { "import.confirm",        new[] { "{0}件のアイテムを読み込みます。\n\n・同じアセットに紐づく既存データは上書きされます\n・サムネイル画像は再取得が必要です（読み込み後に「一括再取得」を実行してください）",
                                           "{0} items will be imported.\n\n- Existing entries linked to the same asset are overwritten\n- Thumbnails are not included; run \"Refetch all\" afterwards" } },
        { "import.done.title",     new[] { "読み込み完了", "Import complete" } },
        { "import.done",           new[] { "読み込みが完了しました。\n\n更新: {0}件 / 新規: {1}件 / スキップ: {2}件",
                                           "Import finished.\n\nUpdated: {0} / Added: {1} / Skipped: {2}" } },
        { "import.failed.title",   new[] { "読み込み失敗", "Import failed" } },
        { "import.failed",         new[] { "ファイルを読み取れませんでした。\n\n{0}", "Could not read the file.\n\n{0}" } },
        { "backup.restore.title",  new[] { "バックアップから復元", "Restore from backup" } },
        { "backup.restore.confirm",new[] { "バックアップ（{0}件）から復元します。\n同じアセットに紐づく既存データは上書きされます。",
                                           "Restores {0} items from backup.\nExisting entries linked to the same asset are overwritten." } },
        { "backup.none",           new[] { "バックアップが見つかりませんでした。", "No backup was found." } },
    };
}

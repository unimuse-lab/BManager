using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// データの安全性まわり：JSONバックアップと、JSON/CSVの書き出し・読み込み。
///
/// 【なぜ必要か】
/// B-Manager のデータは ScriptableObject(.asset) に保存されるため、
///   ・Unityのバージョン差やシリアライズ事故で .asset が読めなくなると内容ごと失われる
///   ・プロジェクトを跨いだ移行手段が無い（READMEにも「全件再登録が必要」と書かれていた）
/// という弱点があった。保存のたびにテキスト(JSON)の控えを書き出し、
/// さらに手動での書き出し／読み込みを用意することで、ユーザー自身が復旧・移行できるようにする。
///
/// サムネイル画像はバイナリのため控えには含めない。
/// 読み込み後に「一括再取得」を実行すればBOOTHから取り直せる。
/// </summary>
public static class BManagerBackup
{
    public const string PREF_BACKUP_ENABLED = "BManager_BackupEnabled";
    private const string FORMAT_VERSION = "1";

    public static bool BackupEnabled
    {
        get { return EditorPrefs.GetBool(PREF_BACKUP_ENABLED, true); }
        set { EditorPrefs.SetBool(PREF_BACKUP_ENABLED, value); }
    }

    // ═══════════════════════════════════════════════════════════
    //  シリアライズ用レコード
    // ═══════════════════════════════════════════════════════════

    [System.Serializable]
    public class Record
    {
        public string itemName        = "";
        public string itemUrl         = "";
        public string category        = "";
        public List<string> userTags  = new List<string>();
        public string shopName        = "";
        public string shopUrl         = "";
        public string priceText       = "";
        public string publishedAt     = "";
        public bool   isEndOfSale;
        public bool   isSoldOut;
        public string registeredAt    = "";
        public string thumbnailUrl    = "";
        public string linkedAssetGuid = "";
        public string linkedAssetPath = "";
    }

    [System.Serializable]
    public class RecordSet
    {
        public string formatVersion = FORMAT_VERSION;
        public string exportedAt    = "";
        public List<Record> items   = new List<Record>();
    }

    // ═══════════════════════════════════════════════════════════
    //  変換
    // ═══════════════════════════════════════════════════════════

    public static Record ToRecord(BManagerData data)
    {
        var r = new Record();
        if (data == null) return r;

        r.itemName     = data.itemName ?? "";
        r.itemUrl      = data.itemUrl ?? "";
        r.category     = data.Category;
        r.userTags     = (data.userTags != null) ? new List<string>(data.userTags) : new List<string>();
        r.shopName     = data.shopName ?? "";
        r.shopUrl      = data.shopUrl ?? "";
        r.priceText    = data.priceText ?? "";
        r.publishedAt  = data.publishedAt ?? "";
        r.isEndOfSale  = data.isEndOfSale;
        r.isSoldOut    = data.isSoldOut;
        r.registeredAt = TicksToIso(data.registrationTimestamp);
        r.thumbnailUrl = data.previousThumbnailUrl ?? "";

        string path = (data.linkedAsset != null) ? AssetDatabase.GetAssetPath(data.linkedAsset) : "";
        r.linkedAssetPath = path ?? "";
        r.linkedAssetGuid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        return r;
    }

    private static string TicksToIso(long ticks)
    {
        if (ticks <= 0) return "";
        try
        {
            return new System.DateTime(ticks).ToString("o",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (System.Exception) { return ""; }
    }

    private static long IsoToTicks(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return 0;
        System.DateTime dt;
        if (System.DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out dt))
            return dt.Ticks;
        return 0;
    }

    // ═══════════════════════════════════════════════════════════
    //  自動バックアップ（保存時・削除時）
    // ═══════════════════════════════════════════════════════════

    private static string BackupFileFor(BManagerData data)
    {
        string assetPath = AssetDatabase.GetAssetPath(data);
        string baseName  = string.IsNullOrEmpty(assetPath)
            ? BManagerPaths.SafeFileName(data.itemName)
            : System.IO.Path.GetFileNameWithoutExtension(assetPath);
        return System.IO.Path.Combine(BManagerPaths.BackupDirectory, baseName + ".json");
    }

    /// <summary>1アイテム分の控えを書き出す。失敗しても保存処理は止めない。</summary>
    public static void WriteBackup(BManagerData data)
    {
        if (data == null || !BackupEnabled) return;
        try
        {
            System.IO.Directory.CreateDirectory(BManagerPaths.BackupDirectory);
            System.IO.File.WriteAllText(
                BackupFileFor(data),
                JsonUtility.ToJson(ToRecord(data), true),
                new UTF8Encoding(false));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[B-Manager] バックアップの書き出しに失敗しました: " + e.Message);
        }
    }

    /// <summary>データ削除時に控えも片付ける。</summary>
    public static void DeleteBackup(BManagerData data)
    {
        if (data == null) return;
        try
        {
            string file = BackupFileFor(data);
            if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[B-Manager] バックアップの削除に失敗しました: " + e.Message);
        }
    }

    public static void OpenBackupFolder()
    {
        string dir = BManagerPaths.BackupDirectory;
        System.IO.Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir + System.IO.Path.DirectorySeparatorChar.ToString());
    }

    /// <summary>バックアップフォルダの全JSONを読み込む。</summary>
    public static List<Record> ReadAllBackups()
    {
        var records = new List<Record>();
        var readNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // 新しい保存先（Assets の外）を優先して読む
        ReadBackupsFrom(BManagerPaths.BackupDirectory, records, readNames);

        // 後方互換：旧バージョンは Assets の中に控えを置いていた。
        // 更新後も旧バージョンの控えを失わないよう、残っていればそこも読む。
        // 同名ファイルは新しい場所を優先し、旧フォルダの自動削除はしない。
        ReadBackupsFrom(BManagerPaths.ToAbsolute(BManagerPaths.LEGACY_BACKUP_ASSET_PATH), records, readNames);
        return records;
    }

    private static void ReadBackupsFrom(string dir, List<Record> records, HashSet<string> readNames)
    {
        if (!System.IO.Directory.Exists(dir)) return;

        foreach (string file in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            // 同じファイル名が既に読み込み済み（新しい場所にある）ならスキップ
            if (!readNames.Add(System.IO.Path.GetFileName(file))) continue;
            try
            {
                var r = JsonUtility.FromJson<Record>(System.IO.File.ReadAllText(file));
                if (r != null && !string.IsNullOrEmpty(r.itemName)) records.Add(r);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[B-Manager] バックアップを読めませんでした: " + file + "\n" + e.Message);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  書き出し
    // ═══════════════════════════════════════════════════════════

    public static string BuildJson(IEnumerable<BManagerData> items)
    {
        var set = new RecordSet
        {
            formatVersion = FORMAT_VERSION,
            exportedAt    = System.DateTime.Now.ToString("o",
                                System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var d in items) if (d != null) set.items.Add(ToRecord(d));
        return JsonUtility.ToJson(set, true);
    }

    private static readonly string[] CsvHeader =
    {
        "itemName", "itemUrl", "category", "userTags", "shopName", "shopUrl",
        "priceText", "publishedAt", "isEndOfSale", "isSoldOut",
        "registeredAt", "thumbnailUrl", "linkedAssetGuid", "linkedAssetPath"
    };

    public static string BuildCsv(IEnumerable<BManagerData> items)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", CsvHeader)).Append("\r\n");

        foreach (var d in items)
        {
            if (d == null) continue;
            var r = ToRecord(d);
            string[] cells =
            {
                r.itemName, r.itemUrl, r.category,
                string.Join("|", r.userTags.ToArray()),
                r.shopName, r.shopUrl, r.priceText, r.publishedAt,
                r.isEndOfSale ? "true" : "false",
                r.isSoldOut   ? "true" : "false",
                r.registeredAt, r.thumbnailUrl, r.linkedAssetGuid, r.linkedAssetPath
            };
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvEscape(cells[i]));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvEscape(string s)
    {
        if (s == null) s = "";
        bool needsQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Excel等で文字化けしないよう UTF-8 BOM 付きで書き出す。</summary>
    public static void WriteTextFile(string path, string content, bool withBom)
    {
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(withBom));
    }

    // ═══════════════════════════════════════════════════════════
    //  読み込み
    // ═══════════════════════════════════════════════════════════

    public static List<Record> ParseJson(string text)
    {
        var set = JsonUtility.FromJson<RecordSet>(text);
        if (set != null && set.items != null && set.items.Count > 0) return set.items;

        // 単一レコード（バックアップファイル1件）も受け付ける
        var single = JsonUtility.FromJson<Record>(text);
        if (single != null && !string.IsNullOrEmpty(single.itemName))
            return new List<Record> { single };

        return new List<Record>();
    }

    public static List<Record> ParseCsv(string text)
    {
        var rows = SplitCsv(text);
        var records = new List<Record>();
        if (rows.Count == 0) return records;

        // ヘッダー行から列位置を決める（列順が違っても読めるようにする）
        var header = rows[0];
        var index = new Dictionary<string, int>();
        for (int i = 0; i < header.Count; i++)
        {
            string key = (header[i] ?? "").Trim().Trim('﻿');
            if (!string.IsNullOrEmpty(key) && !index.ContainsKey(key)) index[key] = i;
        }
        if (!index.ContainsKey("itemName"))
            throw new System.Exception("CSVの1行目にヘッダー（itemName 等）が必要です。");

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == 0) continue;
            if (row.Count == 1 && string.IsNullOrEmpty(row[0])) continue;

            var rec = new Record
            {
                itemName        = Cell(row, index, "itemName"),
                itemUrl         = Cell(row, index, "itemUrl"),
                category        = Cell(row, index, "category"),
                shopName        = Cell(row, index, "shopName"),
                shopUrl         = Cell(row, index, "shopUrl"),
                priceText       = Cell(row, index, "priceText"),
                publishedAt     = Cell(row, index, "publishedAt"),
                isEndOfSale     = Cell(row, index, "isEndOfSale").ToLowerInvariant() == "true",
                isSoldOut       = Cell(row, index, "isSoldOut").ToLowerInvariant() == "true",
                registeredAt    = Cell(row, index, "registeredAt"),
                thumbnailUrl    = Cell(row, index, "thumbnailUrl"),
                linkedAssetGuid = Cell(row, index, "linkedAssetGuid"),
                linkedAssetPath = Cell(row, index, "linkedAssetPath"),
            };

            string tags = Cell(row, index, "userTags");
            if (!string.IsNullOrEmpty(tags))
                foreach (string t in tags.Split('|'))
                    if (!string.IsNullOrEmpty(t.Trim())) rec.userTags.Add(t.Trim());

            if (!string.IsNullOrEmpty(rec.itemName)) records.Add(rec);
        }
        return records;
    }

    private static string Cell(List<string> row, Dictionary<string, int> index, string key)
    {
        int i;
        if (!index.TryGetValue(key, out i)) return "";
        return (i < row.Count && row[i] != null) ? row[i] : "";
    }

    /// <summary>
    /// RFC4180 相当のCSV分割。引用符内のカンマ・改行・エスケープされた引用符("")に対応する。
    /// </summary>
    private static List<List<string>> SplitCsv(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(c);
                continue;
            }

            if (c == '"')      { inQuotes = true; }
            else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
            else if (c == '\r')
            {
                // CRLF / CR どちらも行末として扱う
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Length = 0;
                rows.Add(row); row = new List<string>();
            }
            else if (c == '\n')
            {
                row.Add(cell.ToString()); cell.Length = 0;
                rows.Add(row); row = new List<string>();
            }
            else cell.Append(c);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // ═══════════════════════════════════════════════════════════
    //  レコードの反映
    // ═══════════════════════════════════════════════════════════

    public class ApplyResult
    {
        public int updated;
        public int added;
        public int skipped;
    }

    /// <summary>
    /// レコードを BManagerData へ反映する。
    /// 紐づけ先はGUID優先・パス次点で解決し、どちらでも見つからない場合は
    /// リンク切れ状態で登録する（ウィンドウ側に「再リンク」を用意してある）。
    /// </summary>
    public static ApplyResult ApplyRecords(List<Record> records)
    {
        var result = new ApplyResult();
        if (records == null || records.Count == 0) return result;

        BManagerPaths.EnsureSaveFolder();
        BManagerRegistry.Invalidate();

        // 索引にはこの処理中に作ったデータが載らないため、
        // 同じアセットを指すレコードが複数あっても重複を作らないよう自前で覚えておく
        var createdByAssetPath = new Dictionary<string, BManagerData>();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var rec in records)
            {
                if (rec == null || string.IsNullOrEmpty(rec.itemName)) { result.skipped++; continue; }

                Object asset = ResolveAsset(rec);
                string assetPath = (asset != null) ? AssetDatabase.GetAssetPath(asset) : "";

                BManagerData data = null;
                if (asset != null)
                {
                    if (!createdByAssetPath.TryGetValue(assetPath, out data))
                        data = BManagerRegistry.FindByAsset(asset);
                }
                bool isNew = (data == null);
                if (isNew)
                {
                    data = ScriptableObject.CreateInstance<BManagerData>();
                }
                else
                {
                    Undo.RecordObject(data, "B-Manager Import");
                }

                data.itemName    = rec.itemName;
                data.itemUrl     = rec.itemUrl ?? "";
                data.linkedAsset = asset;
                data.tags        = new List<string> {
                    string.IsNullOrEmpty(rec.category) ? BManagerData.UNCATEGORIZED : rec.category };
                data.userTags    = (rec.userTags != null)
                    ? new List<string>(rec.userTags) : new List<string>();
                data.shopName    = rec.shopName ?? "";
                data.shopUrl     = rec.shopUrl ?? "";
                data.priceText   = rec.priceText ?? "";
                data.publishedAt = rec.publishedAt ?? "";
                data.isEndOfSale = rec.isEndOfSale;
                data.isSoldOut   = rec.isSoldOut;
                data.previousThumbnailUrl = rec.thumbnailUrl ?? "";

                System.DateTimeOffset dto;
                data.publishedTimestamp =
                    (!string.IsNullOrEmpty(data.publishedAt) &&
                     System.DateTimeOffset.TryParse(data.publishedAt,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out dto))
                    ? dto.UtcTicks : 0;

                long regTicks = IsoToTicks(rec.registeredAt);
                data.registrationTimestamp = (regTicks > 0) ? regTicks : System.DateTime.Now.Ticks;

                if (isNew)
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(
                        BManagerPaths.SAVE_PATH + "/" + BManagerPaths.SafeFileName(rec.itemName) + ".asset");
                    AssetDatabase.CreateAsset(data, path);
                    result.added++;
                }
                else
                {
                    result.updated++;
                }

                if (!string.IsNullOrEmpty(assetPath)) createdByAssetPath[assetPath] = data;

                EditorUtility.SetDirty(data);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            BManagerRegistry.Invalidate();
        }

        // 控えの書き出しは AssetDatabase 編集ブロックの外で行う
        if (BackupEnabled)
            foreach (var d in BManagerRegistry.All()) WriteBackup(d);

        return result;
    }

    private static Object ResolveAsset(Record rec)
    {
        if (!string.IsNullOrEmpty(rec.linkedAssetGuid))
        {
            string path = AssetDatabase.GUIDToAssetPath(rec.linkedAssetGuid);
            if (!string.IsNullOrEmpty(path))
            {
                Object obj = AssetDatabase.LoadMainAssetAtPath(path);
                if (obj != null) return obj;
            }
        }
        if (!string.IsNullOrEmpty(rec.linkedAssetPath))
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(rec.linkedAssetPath);
            if (obj != null) return obj;
        }
        return null;
    }
}

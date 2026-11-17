using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 登録済み BManagerData の索引キャッシュ。
///
/// 【なぜ必要か】
/// 以前は「このアセットは登録済みか？」を調べるたびに
/// AssetDatabase.FindAssets("t:BManagerData") で全データをロードしていた。
/// 登録画面はフォルダ候補の数だけこれを呼ぶため、
/// フォルダ100個 × 登録200件 = 2万回のアセットロードが発生していた。
///
/// ここで一度だけ索引を作り、以後は GUID の辞書引きで O(1) にする。
/// アセットの保存・削除・読み込み時に Invalidate() を呼んで作り直す。
/// </summary>
public static class BManagerRegistry
{
    private static List<BManagerData> _all;
    private static Dictionary<string, BManagerData> _byAssetGuid;
    private static int _version;

    /// <summary>索引が作り直されるたびに増える。ウィンドウ側のキャッシュ判定に使う。</summary>
    public static int Version { get { return _version; } }

    public static void Invalidate()
    {
        _all = null;
        _byAssetGuid = null;
        _version++;
    }

    private static void EnsureBuilt()
    {
        if (_all != null && _byAssetGuid != null) return;

        _all = new List<BManagerData>();
        _byAssetGuid = new Dictionary<string, BManagerData>();

        string[] guids = AssetDatabase.FindAssets("t:BManagerData");
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;

            _all.Add(data);

            string assetGuid = GuidOf(data.linkedAsset);
            if (string.IsNullOrEmpty(assetGuid)) continue;
            // 既存の重複データがある場合は先に見つかったものを代表にする
            if (!_byAssetGuid.ContainsKey(assetGuid))
                _byAssetGuid[assetGuid] = data;
        }
    }

    /// <summary>登録済みデータの一覧（内部リストのコピー）。</summary>
    public static List<BManagerData> All()
    {
        EnsureBuilt();
        return new List<BManagerData>(_all);
    }

    /// <summary>指定アセットに紐づく登録データ。無ければ null。</summary>
    public static BManagerData FindByAsset(Object asset)
    {
        string guid = GuidOf(asset);
        if (string.IsNullOrEmpty(guid)) return null;

        EnsureBuilt();
        BManagerData data;
        return _byAssetGuid.TryGetValue(guid, out data) ? data : null;
    }

    /// <summary>アセットパスから登録データを探す。</summary>
    public static BManagerData FindByPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return null;

        EnsureBuilt();
        BManagerData data;
        return _byAssetGuid.TryGetValue(guid, out data) ? data : null;
    }

    /// <summary>アセットのGUIDから登録データを探す。</summary>
    public static BManagerData FindByGuid(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid)) return null;
        EnsureBuilt();
        BManagerData data;
        return _byAssetGuid.TryGetValue(assetGuid, out data) ? data : null;
    }

    public static bool IsRegistered(Object asset)
    {
        return FindByAsset(asset) != null;
    }

    /// <summary>except で渡したデータ自身は重複とみなさない（編集中の自分を除外するため）。</summary>
    public static BManagerData FindDuplicate(Object asset, BManagerData except)
    {
        var found = FindByAsset(asset);
        if (found == null) return null;
        if (except != null && found == except) return null;
        return found;
    }

    private static string GuidOf(Object asset)
    {
        if (asset == null) return null;
        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return null;
        string guid = AssetDatabase.AssetPathToGUID(path);
        return string.IsNullOrEmpty(guid) ? null : guid;
    }
}

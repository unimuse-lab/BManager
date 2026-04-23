using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// .unitypackage のインポート完了イベントのみを検出し、登録ポップアップを表示する。
/// AssetDatabase.importPackageCompleted を使うことで確実に unitypackage のみを対象にできる。
/// </summary>
[InitializeOnLoad]
public class BManagerImportDetector
{
    static BManagerImportDetector()
    {
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
    }

    private static void OnImportPackageCompleted(string packageName)
    {
        if (!EditorPrefs.GetBool(BManagerWindow.PREF_AUTO_OPEN, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;

        // インポート完了直後はまだアセットDBが安定していない場合があるので遅延実行
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying || BuildPipeline.isBuildingPlayer) return;
            TryShowPopupForPackage(packageName);
        };
    }

    private static void TryShowPopupForPackage(string packageName)
    {
        // パッケージ名からインポート先フォルダを推定する
        // unitypackage は通常 Assets/ 直下にルートフォルダを作る
        string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });

        // 最近作成されたフォルダ（30秒以内）を候補として収集
        var recentFolders = new System.Collections.Generic.List<string>();
        double thresholdSeconds = 30.0;

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(assetPath)) continue;

            // Assets/直下の第一階層フォルダのみを対象
            string[] parts = assetPath.Split('/');
            if (parts.Length != 2) continue;
            if (assetPath.Contains("/B-Manager/") || assetPath.EndsWith("/B-Manager")) continue;
            if (assetPath.Contains("/UniMuseData")) continue;

            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!Directory.Exists(fullPath)) continue;

            System.DateTime created = Directory.GetCreationTime(fullPath);
            if ((System.DateTime.Now - created).TotalSeconds <= thresholdSeconds)
            {
                recentFolders.Add(assetPath);
            }
        }

        if (recentFolders.Count == 0) return;

        // 重複登録済みのフォルダを除外
        var unregistered = new System.Collections.Generic.List<string>();
        foreach (string folder in recentFolders)
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(folder);
            if (obj != null && !IsAlreadyRegistered(obj))
                unregistered.Add(folder);
        }

        if (unregistered.Count == 0) return;

        // 対象フォルダが1つならそのままポップアップ、複数なら先頭を対象にポップアップ
        // （フォルダ階層選択UIはポップアップ内で行う）
        string targetPath = unregistered[0];
        Object target = AssetDatabase.LoadMainAssetAtPath(targetPath);
        if (target != null) BManagerPopup.ShowPopup(target);
    }

    public static bool IsAlreadyRegistered(Object target)
    {
        if (target == null) return false;
        string[] guids = AssetDatabase.FindAssets("t:BManagerData");
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && data.linkedAsset == target) return true;
        }
        return false;
    }
}
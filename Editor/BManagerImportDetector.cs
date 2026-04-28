using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// .unitypackage のインポートを確実に検出し、登録ポップアップを表示する。
///
/// 【設計方針】
/// 旧実装は importPackageCompleted 後にファイルシステムの作成時刻（30秒以内）で
/// 対象フォルダを推定していたが、以下の理由で不確実だった：
///   - 既存フォルダへの上書き/マージ時は作成時刻が更新されない
///   - NAS・WSL2・ウイルス対策ソフト等で作成時刻がズレる
///   - 低速環境では delayCall 時点で AssetDB がまだ更新中の場合がある
///
/// 新実装は Unity が提供する3つのイベントを組み合わせる：
///   1. importPackageStarted    → フラグ ON・収集バッファをリセット
///   2. OnPostprocessAllAssets  → フラグが ON のときのみ importedAssets[] からルートフォルダを収集
///   3. importPackageCompleted  → 収集済みフォルダを確定し、未登録のものをポップアップ表示
///
/// これにより「Unityが実際にインポートしたファイルのパス」を直接使うため、
/// タイムスタンプ推測が不要になり、どの環境・どのパッケージ構造でも動作する。
/// キャンセル・失敗イベントも処理してフラグを確実にリセットする。
/// </summary>
[InitializeOnLoad]
public class BManagerImportDetector : AssetPostprocessor
{
    // ── 状態フラグ ────────────────────────────────────────────────
    private static bool _isImportingPackage = false;

    // importPackageStarted 〜 importPackageCompleted の間に
    // OnPostprocessAllAssets で収集したルートフォルダ
    private static readonly HashSet<string> _pendingRootFolders = new HashSet<string>();

    // ═══════════════════════════════════════════════════════════
    //  イベント登録
    // ═══════════════════════════════════════════════════════════

    static BManagerImportDetector()
    {
        AssetDatabase.importPackageStarted   += OnImportPackageStarted;
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
        AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
        AssetDatabase.importPackageFailed    += OnImportPackageFailed;
    }

    // ═══════════════════════════════════════════════════════════
    //  importPackage イベント
    // ═══════════════════════════════════════════════════════════

    private static void OnImportPackageStarted(string packageName)
    {
        _isImportingPackage = true;
        _pendingRootFolders.Clear();
    }

    private static void OnImportPackageCancelled(string packageName)
    {
        _isImportingPackage = false;
        _pendingRootFolders.Clear();
    }

    private static void OnImportPackageFailed(string packageName, string errorMessage)
    {
        _isImportingPackage = false;
        _pendingRootFolders.Clear();
    }

    private static void OnImportPackageCompleted(string packageName)
    {
        _isImportingPackage = false;

        if (!EditorPrefs.GetBool(BManagerWindow.PREF_AUTO_OPEN, false))
        {
            _pendingRootFolders.Clear();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
        {
            _pendingRootFolders.Clear();
            return;
        }

        // 収集済みフォルダを確定コピーし、バッファはすぐ解放
        var folders = new HashSet<string>(_pendingRootFolders);
        _pendingRootFolders.Clear();

        if (folders.Count == 0) return;

        // delayCall でスタックを抜けてからポップアップを表示
        // importPackageCompleted の呼び出しコンテキストで直接 ShowPopup すると
        // エディタが不安定になる場合があるため
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying || BuildPipeline.isBuildingPlayer) return;
            ShowPopupForFirstUnregistered(folders);
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  OnPostprocessAllAssets
    //  importPackage 中のみパス収集を行う
    // ═══════════════════════════════════════════════════════════

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // package import 中でなければ何もしない（通常インポートには反応しない）
        if (!_isImportingPackage) return;

        foreach (string assetPath in importedAssets)
        {
            // Assets/RootFolder/... の形式から Assets/RootFolder を取り出す
            string[] parts = assetPath.Split('/');
            if (parts.Length < 2) continue;

            string rootFolder = parts[0] + "/" + parts[1];

            // B-Manager 自身のフォルダ・データフォルダは除外
            if (rootFolder.Contains("UniMuseData")) continue;
            if (rootFolder.EndsWith("B-Manager"))   continue;

            _pendingRootFolders.Add(rootFolder);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ポップアップ表示
    // ═══════════════════════════════════════════════════════════

    private static void ShowPopupForFirstUnregistered(HashSet<string> folders)
    {
        foreach (string folder in folders)
        {
            // フォルダが実際に存在するか確認（インポート後に削除された等の保険）
            if (!AssetDatabase.IsValidFolder(folder)) continue;

            Object obj = AssetDatabase.LoadMainAssetAtPath(folder);
            if (obj == null) continue;
            if (IsAlreadyRegistered(obj)) continue;

            BManagerPopup.ShowPopup(obj);
            return; // 最初の未登録フォルダ1件のみ表示（複数は階層ツリーUIで選択）
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  重複チェック（BManagerPopup からも参照）
    // ═══════════════════════════════════════════════════════════

    public static bool IsAlreadyRegistered(Object target)
    {
        if (target == null) return false;
        string[] guids = AssetDatabase.FindAssets("t:BManagerData");
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && data.linkedAsset == target) return true;
        }
        return false;
    }
}
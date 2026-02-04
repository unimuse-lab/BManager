using UnityEngine;
using UnityEditor;
using System.IO;

public class BManagerMigrationTool : Editor
{
    // 旧データの場所（削除対象）
    private const string LEGACY_ROOT_DIR = "Assets/UniMuse.lab/B-Manager";
    private const string LEGACY_DATA_DIR = "Assets/UniMuse.lab/B-Manager/BManagerItems";

    // 避難先・新データの場所
    private const string NEW_ROOT_DIR = "Assets/UniMuseData/B-Manager";
    private const string BACKUP_JSON_DIR = "Assets/UniMuseData/B-Manager/MigrationBackup";

    // -----------------------------------------------------------
    // 手順1: データをJSONに避難し、旧フォルダを削除する
    // -----------------------------------------------------------
    [MenuItem("UniMuse.lab/B-Manager/Migration/Step 1: Backup & Delete Legacy Folder")]
    public static void Step1_BackupAndDelete()
    {
        // 1. 旧データがあるか確認
        if (!AssetDatabase.IsValidFolder(LEGACY_DATA_DIR))
        {
            EditorUtility.DisplayDialog("Migration", $"旧データフォルダが見つかりません。\n{LEGACY_DATA_DIR}", "OK");
            return;
        }

        // 2. 避難先フォルダの作成
        if (!Directory.Exists(BACKUP_JSON_DIR))
        {
            Directory.CreateDirectory(BACKUP_JSON_DIR);
        }

        // 3. データをJSONとして避難
        string[] guids = AssetDatabase.FindAssets("t:BManagerData", new[] { LEGACY_DATA_DIR });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BManagerData data = AssetDatabase.LoadAssetAtPath<BManagerData>(path);
            if (data != null)
            {
                string json = JsonUtility.ToJson(data, true);
                // ファイル名にGUIDを含めるなどして重複回避してもよいが、今回はシンプルに名前で保存
                string fileName = Path.GetFileNameWithoutExtension(path) + ".json";
                File.WriteAllText(Path.Combine(BACKUP_JSON_DIR, fileName), json);
                count++;
            }
        }

        // 4. 旧フォルダの削除
        if (EditorUtility.DisplayDialog("Delete Confirmation",
            $"{count} 個のデータをJSONに避難しました。\n\n旧フォルダ '{LEGACY_ROOT_DIR}' を削除しますか？\n(この操作は元に戻せません)", "削除を実行", "キャンセル"))
        {
            AssetDatabase.DeleteAsset(LEGACY_ROOT_DIR);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Step 1 Complete",
                "旧フォルダを削除しました。\n\n続いて 'Step 2: Restore from Json' を実行してデータを復元してください。", "OK");
        }
    }

    // -----------------------------------------------------------
    // 手順2: 避難させたJSONから新しく登録し直す
    // -----------------------------------------------------------
    [MenuItem("UniMuse.lab/B-Manager/Migration/Step 2: Restore from Json")]
    public static void Step2_RestoreFromJson()
    {
        if (!Directory.Exists(BACKUP_JSON_DIR))
        {
            EditorUtility.DisplayDialog("Error", "バックアップJSONが見つかりません。Step 1を実行してください。", "OK");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(BACKUP_JSON_DIR, "*.json");
        int restoreCount = 0;

        // 保存先フォルダの確保
        if (!AssetDatabase.IsValidFolder(NEW_ROOT_DIR))
        {
            if (!AssetDatabase.IsValidFolder("Assets/UniMuseData")) AssetDatabase.CreateFolder("Assets", "UniMuseData");
            AssetDatabase.CreateFolder("Assets/UniMuseData", "B-Manager");
            AssetDatabase.CreateFolder(NEW_ROOT_DIR, "BManagerItems");
        }

        // JSONから復元
        foreach (string jsonPath in jsonFiles)
        {
            string json = File.ReadAllText(jsonPath);

            // 一時的なインスタンスを作成してデータを読み込む
            BManagerData temp = CreateInstance<BManagerData>();
            JsonUtility.FromJsonOverwrite(json, temp);

            // 保存処理 (BManagerPopup.PerformSave 相当の処理をここで実行)
            RestoreDataAsset(temp);

            DestroyImmediate(temp);
            restoreCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 復元完了後、JSONバックアップを削除するか確認
        if (EditorUtility.DisplayDialog("Migration Complete",
            $"{restoreCount} 個のデータを復元しました。\n\n避難用JSONファイル（MigrationBackupフォルダ）を削除しますか？", "削除する", "残す"))
        {
            AssetDatabase.DeleteAsset(BACKUP_JSON_DIR);
            AssetDatabase.Refresh();
        }
    }

    // データ復元用の保存ロジック
    private static void RestoreDataAsset(BManagerData temp)
    {
        BManagerData newData = CreateInstance<BManagerData>();
        newData.itemName = temp.itemName;
        newData.itemUrl = temp.itemUrl;
        newData.linkedAsset = temp.linkedAsset; // GUIDが同じならリンクは維持される
        newData.tags = temp.tags;
        newData.registrationTimestamp = temp.registrationTimestamp;

        // サムネイルの復元（JSONにはシリアライズされないため、別途取得が必要だが
        // ScriptableObject内に埋め込まれていた場合はバイナリ消滅につき復元不可。
        // ※URLから再取得する機能を使えば後で復旧可能）

        string fileName = string.Join("_", newData.itemName.Split(Path.GetInvalidFileNameChars()));
        string saveDir = $"{NEW_ROOT_DIR}/BManagerItems";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{saveDir}/{fileName}.asset");

        AssetDatabase.CreateAsset(newData, path);

        // ※復元時に各アセットごとのバックアップJSONも生成しておく
        string jsonPath = path.Replace(".asset", ".json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(newData, true));
    }
}
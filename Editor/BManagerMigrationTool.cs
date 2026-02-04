using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace UniMuse.BManager.Editor
{
    public class BManagerMigrationTool
    {
        private const string LEGACY_DATA_DIR = "Assets/UniMuse.lab/B-Manager/BManagerItems";
        private const string NEW_ROOT_DIR = "Assets/UniMuseData/B-Manager";
        private const string BACKUP_JSON_DIR = "Assets/UniMuseData/B-Manager/MigrationBackup";
        private const string LEGACY_ROOT_DIR = "Assets/UniMuse.lab/B-Manager";

        [MenuItem("UniMuse.lab/B-Manager/Migration/Step 1: Backup & Convert")]
        public static void Step1_BackupAndConvert()
        {
            if (!AssetDatabase.IsValidFolder(LEGACY_DATA_DIR))
            {
                EditorUtility.DisplayDialog("Migration", $"旧データが見つかりません: {LEGACY_DATA_DIR}", "OK");
                return;
            }

            if (!Directory.Exists(BACKUP_JSON_DIR)) Directory.CreateDirectory(BACKUP_JSON_DIR);

            // "BManagerData"という名前のスクリプトを持つアセットを全て探す（型参照なし）
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { LEGACY_DATA_DIR });
            int count = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object oldObj = AssetDatabase.LoadAssetAtPath<Object>(path);

                if (oldObj != null)
                {
                    // SerializedObjectを使ってプロパティを読み取る（クラス型に依存しない）
                    SerializedObject so = new SerializedObject(oldObj);
                    SerializedProperty scriptProp = so.FindProperty("m_Script");

                    if (scriptProp != null && scriptProp.objectReferenceValue != null &&
                        scriptProp.objectReferenceValue.name == "BManagerData")
                    {
                        // データを抽出して新形式で作成
                        CreateNewDataFromOldSerialized(so, path);
                        count++;
                    }
                }
            }

            AssetDatabase.Refresh();

            if (EditorUtility.DisplayDialog("Step 1 Complete",
                $"{count} 個のデータを新しい形式(BManagerDataNew)に変換・保存しました。\n旧フォルダを削除しますか？", "削除", "後で"))
            {
                AssetDatabase.DeleteAsset(LEGACY_ROOT_DIR);
                AssetDatabase.Refresh();
            }
        }

        private static void CreateNewDataFromOldSerialized(SerializedObject oldSO, string oldPath)
        {
            BManagerDataNew newData = ScriptableObject.CreateInstance<BManagerDataNew>();

            // 古いデータから値を読み出す
            newData.itemName = oldSO.FindProperty("itemName")?.stringValue;
            newData.itemUrl = oldSO.FindProperty("itemUrl")?.stringValue;
            newData.linkedAsset = oldSO.FindProperty("linkedAsset")?.objectReferenceValue;
            newData.registrationTimestamp = oldSO.FindProperty("registrationTimestamp")?.longValue ?? 0;

            SerializedProperty tagsProp = oldSO.FindProperty("tags");
            if (tagsProp != null && tagsProp.isArray)
            {
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    newData.tags.Add(tagsProp.GetArrayElementAtIndex(i).stringValue);
                }
            }

            // 保存処理
            string safeName = string.Join("_", newData.itemName.Split(Path.GetInvalidFileNameChars()));
            string saveDir = $"{NEW_ROOT_DIR}/BManagerItems";

            if (!AssetDatabase.IsValidFolder("Assets/UniMuseData")) AssetDatabase.CreateFolder("Assets", "UniMuseData");
            if (!AssetDatabase.IsValidFolder("Assets/UniMuseData/B-Manager")) AssetDatabase.CreateFolder("Assets/UniMuseData", "B-Manager");
            if (!AssetDatabase.IsValidFolder(saveDir)) AssetDatabase.CreateFolder("Assets/UniMuseData/B-Manager", "BManagerItems");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{saveDir}/{safeName}.asset");
            AssetDatabase.CreateAsset(newData, path);

            // JSONも生成
            string jsonPath = path.Replace(".asset", ".json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(newData, true));
        }
    }
}
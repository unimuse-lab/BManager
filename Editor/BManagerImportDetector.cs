using UnityEngine;
using UnityEditor;
using System.IO;

public class BManagerImportDetector : AssetPostprocessor
{
    private static string pendingTargetPath = "";

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (!EditorPrefs.GetBool(BManagerWindow.PREF_AUTO_OPEN, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        if (deletedAssets.Length > 0 || movedAssets.Length > 0) return;

        foreach (string str in importedAssets)
        {
            if (str.EndsWith(".asset") || str.EndsWith(".cs") || str.EndsWith(".meta")) continue;
            if (str.Contains("/B-Manager/")) continue;

            string[] pathParts = str.Split('/');
            if (pathParts.Length < 2) continue;
            string targetPath = pathParts[0] + "/" + pathParts[1];

            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", targetPath));
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
            {
                System.DateTime creationTime = Directory.Exists(fullPath) ? Directory.GetCreationTime(fullPath) : File.GetCreationTime(fullPath);
                if ((System.DateTime.Now - creationTime).TotalSeconds > 5.0) continue;
            }

            pendingTargetPath = targetPath;
            EditorApplication.update -= WaitUntilImportFinished;
            EditorApplication.update += WaitUntilImportFinished;
            break;
        }
    }

    private static void WaitUntilImportFinished()
    {
        if (EditorApplication.isUpdating || EditorApplication.isCompiling) return;
        EditorApplication.update -= WaitUntilImportFinished;
        if (EditorApplication.isPlaying || string.IsNullOrEmpty(pendingTargetPath)) return;

        string path = pendingTargetPath;
        pendingTargetPath = "";
        EditorApplication.delayCall += () => {
            if (EditorApplication.isPlaying || BuildPipeline.isBuildingPlayer) return;
            Object target = AssetDatabase.LoadMainAssetAtPath(path);
            if (target != null && !IsAlreadyRegistered(target)) BManagerPopup.ShowPopup(target);
        };
    }

    private static bool IsAlreadyRegistered(Object target)
    {
        string[] guids = AssetDatabase.FindAssets("t:BManagerDataNew");
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<BManagerDataNew>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && data.linkedAsset == target) return true;
        }
        return false;
    }
}
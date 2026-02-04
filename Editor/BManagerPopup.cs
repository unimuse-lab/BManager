using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

public class BManagerPopup : EditorWindow
{
    public string inputUrl = "";
    public string inputTitle = "";
    public Object targetAsset;
    public BManagerData existingData;
    private int selectedCategoryIndex = 0;

    private const string ROOT_DATA_PATH = "Assets/UniMuseData";
    private const string SAVE_PATH = "Assets/UniMuseData/B-Manager";

    private static readonly string[] CategoryOptions = new string[] { "未分類", "3Dキャラクター", "3D装飾品", "3D衣装", "3D小道具", "3Dテクスチャ", "3Dツール・システム", "3Dモーション・アニメーション", "3D環境・ワールド", "VRoid", "3Dキャラクター（その他）" };

    public static void ShowPopup(Object target, BManagerData data = null)
    {
        // ヒエラルキー（シーン上のオブジェクト）からの登録をブロック
        if (target != null && !EditorUtility.IsPersistent(target)) return;

        var window = GetWindow<BManagerPopup>(true, "アイテム登録・編集", true);
        Vector2 size = new Vector2(400, 200);
        window.minSize = size;
        window.maxSize = size;
        window.Setup(target, data);
        window.Show();
    }

    public void Setup(Object target, BManagerData data = null)
    {
        this.targetAsset = target;
        this.existingData = data;
        if (data != null)
        {
            this.inputUrl = data.itemUrl;
            this.inputTitle = data.itemName;
            int index = System.Array.IndexOf(CategoryOptions, (data.tags != null && data.tags.Count > 0) ? data.tags[0] : "");
            this.selectedCategoryIndex = (index >= 0) ? index : 0;
        }
        else
        {
            this.inputTitle = target != null ? target.name : "";
            this.inputUrl = "";
            this.selectedCategoryIndex = 0;
        }
    }

    public static bool IsBoothUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        url = url.Trim();
        return Regex.IsMatch(url, @"^https://([a-zA-Z0-9-]+\.)?booth\.pm/([^/]+/)?items/\d+");
    }

    // ヒエラルキーでの右クリックメニュー表示を制限するバリデーション
    [MenuItem("Assets/Register to B-Manager", true)]
    private static bool ValidateRegisterFromContextMenu()
    {
        return Selection.activeObject != null && EditorUtility.IsPersistent(Selection.activeObject);
    }

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

    private void OnGUI()
    {
        DrawInRect(new Rect(0, 0, position.width, position.height));
    }

    public void DrawInRect(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Space(10);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("登録対象", targetAsset, typeof(Object), false);
            }
            inputTitle = EditorGUILayout.TextField("アイテム名", inputTitle);

            string rawUrl = EditorGUILayout.TextField("商品URL", inputUrl);
            inputUrl = rawUrl != null ? rawUrl.Trim() : "";

            selectedCategoryIndex = EditorGUILayout.Popup("カテゴリー", selectedCategoryIndex, CategoryOptions);
        }

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();

        bool isBooth = IsBoothUrl(inputUrl);

        using (new EditorGUILayout.VerticalScope())
        {
            using (new EditorGUI.DisabledScope(!isBooth))
            {
                if (GUILayout.Button("情報を取得して保存", GUILayout.Height(30)))
                {
                    _ = RunFetchAndSaveFromUI();
                }
            }

            if (GUILayout.Button("入力内容のみで保存", GUILayout.Height(30)))
            {
                PerformSave(inputTitle, null, inputUrl, targetAsset, existingData, selectedCategoryIndex, false);
                this.Close();
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("キャンセル", GUILayout.Height(20)))
        {
            this.Close();
        }

        if (!isBooth && !string.IsNullOrEmpty(inputUrl))
        {
            EditorGUILayout.HelpBox("BOOTH以外のURLです。情報は自動取得されないため、手動で入力して保存してください。", MessageType.Info);
        }
        GUILayout.EndArea();
    }

    private async Task RunFetchAndSaveFromUI()
    {
        await StaticFetchAndSave(existingData, true, inputUrl, inputTitle, targetAsset, selectedCategoryIndex);
        this.Close();
    }

    public static async Task StaticFetchAndSave(BManagerData data, bool keepCategory, string overrideUrl = "", string overrideTitle = "", Object overrideTarget = null, int catIdx = 0)
    {
        string url = (data != null) ? data.itemUrl : overrideUrl;
        if (!IsBoothUrl(url)) return;

        try
        {
            using (UnityWebRequest r = UnityWebRequest.Get(url))
            {
                r.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                r.timeout = 15;
                var op = r.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (r.result != UnityWebRequest.Result.Success) return;

                string html = System.Text.Encoding.UTF8.GetString(r.downloadHandler.data);
                var titleMatch = Regex.Match(html, @"<title>(.*?)</title>");
                string title = titleMatch.Success ? titleMatch.Groups[1].Value : (data != null ? data.itemName : overrideTitle);
                title = System.Net.WebUtility.HtmlDecode(title).Replace(" - BOOTH", "").Trim();

                var imgMatch = Regex.Match(html, @"<meta property=""og:image"" content=""(.*?)""");
                Texture2D thumb = null;
                if (imgMatch.Success)
                {
                    using (UnityWebRequest tr = UnityWebRequestTexture.GetTexture(imgMatch.Groups[1].Value))
                    {
                        var op2 = tr.SendWebRequest();
                        while (!op2.isDone) await Task.Yield();
                        if (tr.result == UnityWebRequest.Result.Success) thumb = DownloadHandlerTexture.GetContent(tr);
                    }
                }
                PerformSave(title, thumb, url, (data != null ? data.linkedAsset : overrideTarget), data, catIdx, keepCategory);
            }
        }
        catch (System.Exception e) { Debug.LogError($"[B-Manager Error] {e.Message}"); }
    }

    private static void PerformSave(string title, Texture2D thumb, string url, Object target, BManagerData data, int catIdx, bool keepCategory)
    {
        bool isNew = (data == null);
        if (isNew)
        {
            data = CreateInstance<BManagerData>();
            data.registrationTimestamp = System.DateTime.Now.Ticks;
        }

        data.itemName = title;
        data.itemUrl = url;
        data.linkedAsset = target;
        if (isNew || !keepCategory) data.tags = new List<string> { CategoryOptions[catIdx] };

        EnsureFolderExists();

        string path = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(path))
        {
            string safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
            path = AssetDatabase.GenerateUniqueAssetPath($"{SAVE_PATH}/{safeName}.asset");
            AssetDatabase.CreateAsset(data, path);
        }

        if (thumb != null)
        {
            Texture2D newThumb = new Texture2D(thumb.width, thumb.height);
            newThumb.SetPixels(thumb.GetPixels()); newThumb.Apply();
            DestroyImmediate(thumb);
            newThumb.name = "Thumbnail"; newThumb.hideFlags = HideFlags.HideInHierarchy;

            Object[] allSubAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var sub in allSubAssets)
            {
                if (sub is Texture2D && !AssetDatabase.IsMainAsset(sub))
                {
                    DestroyImmediate(sub, true);
                }
            }

            AssetDatabase.AddObjectToAsset(newThumb, data);
            data.thumbnail = newThumb;
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.SaveAssets();

        // JSONバックアップ作成
        UpdateJsonBackup(data);
    }

    private static void UpdateJsonBackup(BManagerData data)
    {
        string assetPath = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(assetPath)) return;

        string jsonPath = assetPath.Replace(".asset", ".json");
        string json = JsonUtility.ToJson(data, true);

        File.WriteAllText(jsonPath, json);
        AssetDatabase.ImportAsset(jsonPath);
    }

    private static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder(ROOT_DATA_PATH)) AssetDatabase.CreateFolder("Assets", "UniMuseData");
        if (!AssetDatabase.IsValidFolder(SAVE_PATH)) AssetDatabase.CreateFolder(ROOT_DATA_PATH, "B-Manager");
    }
}
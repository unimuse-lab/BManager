using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Projectウィンドウのカラーハイライト。
///
/// 【2階層ハイライトについて】
/// BOOTHのアイテムは「ショップ名 / 商品名 / ...」という階層でインポートされることが多い。
/// 1フォルダだけ着色すると、登録したのがショップのフォルダなのか商品のフォルダなのかが
/// 分かりにくいため、上位2階層（ルート＋その直下）を対象にする。
///
/// 登録画面のツリーで下の階層を選んだ場合も、ハイライトは必ず一番上のフォルダから始める。
/// 例: ルート Assets/ShopName、選択 Assets/ShopName/ItemName/Textures
///     → Assets/ShopName と Assets/ShopName/ItemName を着色する
///
/// ウィンドウを閉じていても着色が残るよう、描画は [InitializeOnLoadMethod] で常時登録する。
/// </summary>
public static class BManagerHighlight
{
    public const string PREF_ENABLED  = "BManager_HighlightEnabled";
    public const string PREF_COLOR_R  = "BManager_HighlightColor_R";
    public const string PREF_COLOR_G  = "BManager_HighlightColor_G";
    public const string PREF_COLOR_B  = "BManager_HighlightColor_B";

    /// <summary>ハイライト対象（";" 区切り・上位階層から順）。</summary>
    public const string PREF_PATHS    = "BManager_LastRegisteredPaths";

    /// <summary>旧バージョンが使っていた単一パスのキー。読み込み時のフォールバックとして残す。</summary>
    public const string PREF_PATH_LEGACY = "BManager_LastRegisteredPath";

    /// <summary>ハイライトする階層の数。</summary>
    public const int MAX_LEVELS = 2;

    private static readonly Color DEFAULT_COLOR = new Color(0.4f, 0.8f, 1.0f, 1f);

    // ═══════════════════════════════════════════════════════════
    //  設定
    // ═══════════════════════════════════════════════════════════

    public static bool Enabled
    {
        get { return EditorPrefs.GetBool(PREF_ENABLED, true); }
        set { EditorPrefs.SetBool(PREF_ENABLED, value); }
    }

    public static Color LoadColor()
    {
        return new Color(
            EditorPrefs.GetFloat(PREF_COLOR_R, DEFAULT_COLOR.r),
            EditorPrefs.GetFloat(PREF_COLOR_G, DEFAULT_COLOR.g),
            EditorPrefs.GetFloat(PREF_COLOR_B, DEFAULT_COLOR.b),
            1f);
    }

    public static void SaveColor(Color c)
    {
        EditorPrefs.SetFloat(PREF_COLOR_R, c.r);
        EditorPrefs.SetFloat(PREF_COLOR_G, c.g);
        EditorPrefs.SetFloat(PREF_COLOR_B, c.b);
    }

    // ═══════════════════════════════════════════════════════════
    //  対象パスの決定
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ルートフォルダと実際に選択されたフォルダから、着色する上位2階層を組み立てる。
    /// 選択がルートより下にある場合は、ルートと「ルートの直下にある祖先」を返す。
    /// </summary>
    public static string[] BuildPaths(string rootPath, string selectedPath)
    {
        rootPath     = Normalize(rootPath);
        selectedPath = Normalize(selectedPath);

        // ルートが取れない場合は選択側をルートとして扱う
        if (string.IsNullOrEmpty(rootPath)) rootPath = selectedPath;
        if (string.IsNullOrEmpty(rootPath)) return new string[0];

        var paths = new List<string> { rootPath };

        // 選択がルート配下にあるなら、ルート直下の階層（= 2階層目）も対象にする
        if (!string.IsNullOrEmpty(selectedPath) &&
            selectedPath != rootPath &&
            selectedPath.StartsWith(rootPath + "/"))
        {
            string relative = selectedPath.Substring(rootPath.Length + 1);
            int slash = relative.IndexOf('/');
            string secondLevel = rootPath + "/" + ((slash >= 0) ? relative.Substring(0, slash) : relative);
            if (!paths.Contains(secondLevel)) paths.Add(secondLevel);
        }

        if (paths.Count > MAX_LEVELS) paths.RemoveRange(MAX_LEVELS, paths.Count - MAX_LEVELS);
        return paths.ToArray();
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>登録直後に呼び、ハイライト対象を記録する。</summary>
    public static void Set(string rootPath, string selectedPath)
    {
        string[] paths = BuildPaths(rootPath, selectedPath);
        EditorPrefs.SetString(PREF_PATHS, string.Join(";", paths));
        // 旧キーも更新しておく（他バージョンと行き来した場合の保険）
        EditorPrefs.SetString(PREF_PATH_LEGACY, paths.Length > 0 ? paths[0] : "");
        EditorApplication.RepaintProjectWindow();
    }

    public static void Clear()
    {
        EditorPrefs.DeleteKey(PREF_PATHS);
        EditorPrefs.DeleteKey(PREF_PATH_LEGACY);
        EditorApplication.RepaintProjectWindow();
    }

    public static string[] GetPaths()
    {
        string joined = EditorPrefs.GetString(PREF_PATHS, "");
        if (string.IsNullOrEmpty(joined))
        {
            string legacy = EditorPrefs.GetString(PREF_PATH_LEGACY, "");
            return string.IsNullOrEmpty(legacy) ? new string[0] : new[] { legacy };
        }
        return joined.Split(';');
    }

    // ═══════════════════════════════════════════════════════════
    //  描画
    // ═══════════════════════════════════════════════════════════

    [InitializeOnLoadMethod]
    private static void Register()
    {
        // 二重登録を避けるため一度外してから付ける
        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        if (!Enabled) return;

        string[] paths = GetPaths();
        if (paths.Length == 0) return;

        string itemPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(itemPath)) return;

        int level = -1;
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == itemPath) { level = i; break; }
        }
        if (level < 0) return;

        // 上の階層をより濃く塗り、階層の差が見た目で分かるようにする
        Color color = LoadColor();
        color.a = (level == 0) ? 0.30f : 0.18f;
        EditorGUI.DrawRect(selectionRect, color);

        Rect bar = new Rect(selectionRect.xMax - 4, selectionRect.y, 4, selectionRect.height);
        color.a = (level == 0) ? 0.85f : 0.50f;
        EditorGUI.DrawRect(bar, color);
    }
}

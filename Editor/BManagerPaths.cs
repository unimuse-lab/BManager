using UnityEditor;

/// <summary>
/// B-Manager が使うプロジェクト内パスと、ファイル名の正規化。
/// 複数のクラスから参照するため1箇所にまとめている。
/// </summary>
public static class BManagerPaths
{
    public const string ROOT_DATA_PATH = "Assets/UniMuseData";
    public const string SAVE_PATH      = "Assets/UniMuseData/B-Manager";

    /// <summary>
    /// 旧バージョンのバックアップ保存先（Assets の中）。
    /// Unity に取り込まれてしまう不具合があったため現在は使わない。
    /// 旧バージョンで作った控えを失わないよう、読み込み時の後方互換にのみ使う。
    /// </summary>
    public const string LEGACY_BACKUP_ASSET_PATH = "Assets/UniMuseData/B-Manager/Backup";

    /// <summary>
    /// プロジェクトルート（Assets の親ディレクトリ）の絶対パス。
    /// バックアップを Assets の外に置くための基準になる。
    /// </summary>
    public static string ProjectRoot
    {
        get { return System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath); }
    }

    /// <summary>
    /// JSONバックアップの保存先（絶対パス）。プロジェクトルート直下の UniMuseData/B-Manager/Backup。
    /// Assets の外に置く理由は2つある。
    ///   1. Unity に取り込まれないようにするため
    ///      （Project ウィンドウに並ばず、ビルドやパッケージにも含まれない）。
    ///   2. Library を削除してもバックアップが消えないようにするため
    ///      （Assets の中だと再インポートや .meta の再生成の影響を受ける）。
    /// </summary>
    public static string BackupDirectory
    {
        get
        {
            string sub = "UniMuseData/B-Manager/Backup".Replace('/', System.IO.Path.DirectorySeparatorChar);
            return System.IO.Path.Combine(ProjectRoot, sub);
        }
    }

    /// <summary>データ保存先フォルダを用意する。</summary>
    public static void EnsureSaveFolder()
    {
        if (!AssetDatabase.IsValidFolder(ROOT_DATA_PATH))
            AssetDatabase.CreateFolder("Assets", "UniMuseData");
        if (!AssetDatabase.IsValidFolder(SAVE_PATH))
            AssetDatabase.CreateFolder(ROOT_DATA_PATH, "B-Manager");
    }

    /// <summary>プロジェクトルート（Assets の親）からの絶対パスに変換する。</summary>
    public static string ToAbsolute(string assetPath)
    {
        string projectRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
        return System.IO.Path.Combine(projectRoot, assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    /// <summary>ファイル名に使えない文字を "_" に置き換える。</summary>
    public static string SafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Untitled";
        string safe = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        safe = safe.Trim();
        return string.IsNullOrEmpty(safe) ? "Untitled" : safe;
    }
}

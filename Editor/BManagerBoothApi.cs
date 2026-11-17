using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>
/// BOOTH商品情報の取得。
///
/// 【設計方針】
/// 商品ページのHTMLをスクレイピングするのをやめ、商品URLに .json を付けた
/// エンドポイント1本に統一している。
///
///   旧: HTML（商品名の &lt;title&gt; / og:image / data-origin 等の正規表現） + JSON（カテゴリー）
///        → 1アイテムにつき2リクエスト。HTML構造が変わると壊れる。
///   新: JSON のみ
///        → 1アイテムにつき1リクエスト。必要な情報は全て含まれている：
///           name / category.name / category.parent.name / price / shop{name,url}
///           / images[].original / published_at / is_end_of_sale / is_sold_out
///
/// og:image は images[0].original のパスに "c/620x620/" を挿入したものと完全に一致するため、
/// 旧バージョンが previousThumbnailUrl に保存したURLとの互換性も保たれる。
/// </summary>
public static class BManagerBoothApi
{
    private const string USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    private const int TIMEOUT_SECONDS = 15;

    /// <summary>og:image と同じサイズ指定。旧データとの互換のため変更しないこと。</summary>
    public const string MAIN_IMAGE_SIZE = "620x620";

    /// <summary>候補一覧のプレビュー用。本体より小さく取って一覧表示を軽くする。</summary>
    public const string PREVIEW_IMAGE_SIZE = "300x300";

    // ═══════════════════════════════════════════════════════════
    //  取得結果
    // ═══════════════════════════════════════════════════════════

    public class ItemInfo
    {
        public bool   ok;
        public string error = "";

        public string name            = "";
        public string category        = "";
        public string categoryParent  = "";
        public string shopName        = "";
        public string shopUrl         = "";
        public string priceText       = "";
        public string publishedAt     = "";
        public long   publishedTicks;
        public bool   isEndOfSale;
        public bool   isSoldOut;

        /// <summary>商品画像の原寸URL（先頭が og:image と同じメイン画像）。</summary>
        public List<string> imageUrls = new List<string>();

        /// <summary>メイン画像を og:image と同じ620x620で取得するURL。</summary>
        public string MainThumbnailUrl
        {
            get
            {
                return imageUrls.Count > 0 ? BuildSizedUrl(imageUrls[0], MAIN_IMAGE_SIZE) : "";
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  URL判定
    // ═══════════════════════════════════════════════════════════

    private const string ITEM_URL_PATTERN =
        @"^https://([a-zA-Z0-9-]+\.)?booth\.pm/([^/]+/)?items/\d+";

    public static bool IsBoothUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return Regex.IsMatch(url.Trim(), ITEM_URL_PATTERN);
    }

    /// <summary>商品URLから .json エンドポイントのURLを作る。BOOTH以外なら null。</summary>
    public static string GetJsonUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var m = Regex.Match(url.Trim(), ITEM_URL_PATTERN);
        return m.Success ? m.Value + ".json" : null;
    }

    // ═══════════════════════════════════════════════════════════
    //  画像URLのサイズ変換
    // ═══════════════════════════════════════════════════════════

    private const string PXIMG_HOST = "https://booth.pximg.net/";

    /// <summary>
    /// 原寸URL https://booth.pximg.net/UUID/i/ID/FILE から
    /// リサイズURL https://booth.pximg.net/c/SIZE/UUID/i/ID/FILE を作る。
    /// 既にサイズ指定が入っている場合は差し替える。
    /// </summary>
    public static string BuildSizedUrl(string url, string size)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(size)) return url;
        if (!url.StartsWith(PXIMG_HOST)) return url;

        string path = StripSizeSegment(url.Substring(PXIMG_HOST.Length));
        return PXIMG_HOST + "c/" + size + "/" + path;
    }

    /// <summary>
    /// サイズ違いの同一画像を同じものとして扱うためのキー。
    /// "c/620x620/UUID/i/ID/FILE" と "UUID/i/ID/FILE" は同じキーになる。
    /// </summary>
    public static string CanonicalImageKey(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (!url.StartsWith(PXIMG_HOST)) return url.Trim();
        return StripSizeSegment(url.Substring(PXIMG_HOST.Length));
    }

    private static string StripSizeSegment(string path)
    {
        // 先頭が "c/<サイズ指定>/" なら取り除く
        if (!path.StartsWith("c/")) return path;
        int second = path.IndexOf('/', 2);
        if (second < 0) return path;
        return path.Substring(second + 1);
    }

    // ═══════════════════════════════════════════════════════════
    //  価格
    // ═══════════════════════════════════════════════════════════

    /// <summary>"¥ 6,000" のような表示文字列から並び替え用の数値を取り出す。取れなければ -1。</summary>
    public static long ParsePrice(string priceText)
    {
        if (string.IsNullOrEmpty(priceText)) return -1;
        var m = Regex.Match(priceText.Replace(",", ""), @"\d+");
        long v;
        if (m.Success && long.TryParse(m.Value, out v)) return v;
        return -1;
    }

    // ═══════════════════════════════════════════════════════════
    //  商品情報の取得（1リクエスト）
    // ═══════════════════════════════════════════════════════════

    public static async Task<ItemInfo> FetchItem(string url)
    {
        var info = new ItemInfo();

        string jsonUrl = GetJsonUrl(url);
        if (string.IsNullOrEmpty(jsonUrl))
        {
            info.error = BManagerL10n.Tr("err.notBoothUrl");
            return info;
        }

        string json = await DownloadText(jsonUrl, e => info.error = e);
        if (json == null) return info;

        if (!Parse(json, info))
        {
            if (string.IsNullOrEmpty(info.error))
                info.error = BManagerL10n.Tr("err.parse");
            return info;
        }

        info.ok = true;
        return info;
    }

    // ═══════════════════════════════════════════════════════════
    //  JSONパース
    // ═══════════════════════════════════════════════════════════

    [System.Serializable] private class JParent   { public string name; }
    [System.Serializable] private class JCategory { public int id; public string name; public JParent parent; }
    [System.Serializable] private class JShop     { public string name; public string url; public string subdomain; }
    [System.Serializable] private class JImage    { public string original; public string resized; }

    [System.Serializable]
    private class JItem
    {
        public string   name;
        public string   price;
        public string   published_at;
        public bool     is_end_of_sale;
        public bool     is_sold_out;
        public JCategory category;
        public JShop     shop;
        public JImage[]  images;
    }

    /// <summary>
    /// JsonUtility で読み、失敗した項目だけ正規表現で補う。
    /// BOOTH側のスキーマが一部変わっても取れるものは取る方針。
    /// </summary>
    private static bool Parse(string json, ItemInfo info)
    {
        JItem raw = null;
        try { raw = JsonUtility.FromJson<JItem>(json); }
        catch (System.Exception) { /* 下の正規表現フォールバックに任せる */ }

        if (raw != null)
        {
            info.name        = Clean(raw.name);
            info.priceText   = Clean(raw.price);
            info.publishedAt = Clean(raw.published_at);
            info.isEndOfSale = raw.is_end_of_sale;
            info.isSoldOut   = raw.is_sold_out;

            if (raw.category != null)
            {
                info.category = Clean(raw.category.name);
                if (raw.category.parent != null)
                    info.categoryParent = Clean(raw.category.parent.name);
            }
            if (raw.shop != null)
            {
                info.shopName = Clean(raw.shop.name);
                info.shopUrl  = Clean(raw.shop.url);
            }
            if (raw.images != null)
            {
                foreach (var img in raw.images)
                {
                    if (img == null) continue;
                    string original = Clean(img.original);
                    if (!string.IsNullOrEmpty(original) && !info.imageUrls.Contains(original))
                        info.imageUrls.Add(original);
                }
            }
        }

        // ── 正規表現によるフォールバック ───────────────────────
        if (string.IsNullOrEmpty(info.name))
            info.name = Clean(Match(json, @"""name""\s*:\s*""((?:[^""\\]|\\.)*)"""));

        if (string.IsNullOrEmpty(info.category))
            info.category = Clean(Match(json,
                @"""category""\s*:\s*\{[^{}]*?""name""\s*:\s*""((?:[^""\\]|\\.)*)"""));

        if (string.IsNullOrEmpty(info.priceText))
            info.priceText = Clean(Match(json, @"""price""\s*:\s*""((?:[^""\\]|\\.)*)"""));

        if (info.imageUrls.Count == 0)
        {
            foreach (Match m in Regex.Matches(json,
                @"""original""\s*:\s*""(https://booth\.pximg\.net/[^""]+)"""))
            {
                string u = Unescape(m.Groups[1].Value);
                if (!info.imageUrls.Contains(u)) info.imageUrls.Add(u);
            }
        }

        // 公開日を並び替え用の Ticks にする
        if (!string.IsNullOrEmpty(info.publishedAt))
        {
            System.DateTimeOffset dto;
            if (System.DateTimeOffset.TryParse(info.publishedAt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dto))
                info.publishedTicks = dto.UtcTicks;
        }

        // 名前が取れていれば「商品情報を読めた」と見なす
        return !string.IsNullOrEmpty(info.name);
    }

    private static string Match(string s, string pattern)
    {
        var m = Regex.Match(s, pattern);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return Unescape(s).Trim();
    }

    /// <summary>JSON文字列中のエスケープを最低限戻す。</summary>
    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
        return s.Replace("\\\"", "\"")
                .Replace("\\/", "/")
                .Replace("\\n", "\n")
                .Replace("\\r", "")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");
    }

    // ═══════════════════════════════════════════════════════════
    //  通信
    // ═══════════════════════════════════════════════════════════

    public static async Task<string> DownloadText(string url, System.Action<string> onError)
    {
        try
        {
            using (var r = UnityWebRequest.Get(url))
            {
                r.SetRequestHeader("User-Agent", USER_AGENT);
                r.timeout = TIMEOUT_SECONDS;
                var op = r.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (r.result != UnityWebRequest.Result.Success)
                {
                    string msg = (r.responseCode == 404)
                        ? BManagerL10n.Tr("err.notFound")
                        : BManagerL10n.Tr("err.http", r.responseCode, r.error);
                    if (onError != null) onError(msg);
                    return null;
                }

                byte[] body = (r.downloadHandler != null) ? r.downloadHandler.data : null;
                if (body == null || body.Length == 0)
                {
                    if (onError != null)
                        onError(BManagerL10n.Tr("err.emptyBody", r.responseCode));
                    return null;
                }
                return System.Text.Encoding.UTF8.GetString(body);
            }
        }
        catch (System.Exception e)
        {
            if (onError != null) onError(e.Message);
            return null;
        }
    }

    public static async Task<Texture2D> DownloadTexture(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            using (var r = UnityWebRequestTexture.GetTexture(url))
            {
                r.SetRequestHeader("User-Agent", USER_AGENT);
                r.timeout = TIMEOUT_SECONDS;
                var op = r.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (r.result != UnityWebRequest.Result.Success) return null;
                return DownloadHandlerTexture.GetContent(r);
            }
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}

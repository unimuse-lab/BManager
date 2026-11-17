using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// B-Manager が管理する1アイテム分のデータ。
/// ScriptableObject として Assets/UniMuseData/B-Manager/ に保存される。
/// </summary>
[CreateAssetMenu(fileName = "BManagerData", menuName = "B-Manager/Data", order = 1)]
public class BManagerData : ScriptableObject
{
    /// <summary>
    /// カテゴリー未設定を表す保存上のキー。
    /// UIの表示言語に関わらずデータ側の値は常にこの文字列で固定する。
    /// </summary>
    public const string UNCATEGORIZED = "未分類";

    [Header("アセット情報")]
    public Object linkedAsset;
    public string itemName   = "";
    public string itemUrl    = "";
    public Texture2D thumbnail;

    /// <summary>
    /// BOOTHから同期したカテゴリー。tags[0] のみを使用する。
    /// ユーザーが自由に付けるタグは userTags 側に保持し、ここには混ぜない。
    /// </summary>
    [Header("カテゴリー（BOOTH同期）")]
    public List<string> tags = new List<string>();

    /// <summary>
    /// ユーザーが自由に付けるタグ。BOOTHのカテゴリーとは独立した分類で、
    /// 再取得やカテゴリー同期を行っても上書きされない。
    /// </summary>
    [Header("ユーザータグ（BOOTHとは独立）")]
    public List<string> userTags = new List<string>();

    [Header("BOOTH商品情報")]
    public string shopName   = "";
    public string shopUrl    = "";
    public string priceText  = "";
    public string publishedAt = "";          // BOOTHの公開日時（ISO8601文字列）
    public long   publishedTimestamp;        // 上記をパースした Ticks（並び替え用・0は不明）
    public bool   isEndOfSale;               // 販売終了
    public bool   isSoldOut;                 // 売り切れ

    [Header("登録日時")]
    public long registrationTimestamp;  // System.DateTime.Now.Ticks

    /// <summary>
    /// 最後に使用したサムネイル画像のURL。
    /// 再取得時にこのURLが依然有効なら優先して取得する。
    /// </summary>
    [Header("サムネイル管理")]
    public string previousThumbnailUrl = "";

    /// <summary>保存されているカテゴリー（未設定なら UNCATEGORIZED）。</summary>
    public string Category
    {
        get
        {
            if (tags == null || tags.Count == 0 || string.IsNullOrEmpty(tags[0]))
                return UNCATEGORIZED;
            return tags[0];
        }
    }

    /// <summary>販売状態に問題がある（販売終了または売り切れ）か。</summary>
    public bool HasSaleWarning { get { return isEndOfSale || isSoldOut; } }
}

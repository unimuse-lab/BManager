using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// B-Manager が管理する1アイテム分のデータ。
/// ScriptableObject として Assets/UniMuseData/B-Manager/ に保存される。
/// </summary>
[CreateAssetMenu(fileName = "BManagerData", menuName = "B-Manager/Data", order = 1)]
public class BManagerData : ScriptableObject
{
    [Header("アセット情報")]
    public Object linkedAsset;
    public string itemName   = "";
    public string itemUrl    = "";
    public Texture2D thumbnail;

    [Header("カテゴリー")]
    public List<string> tags = new List<string>();

    [Header("登録日時")]
    public long registrationTimestamp;  // System.DateTime.Now.Ticks

    /// <summary>
    /// 最後に使用したサムネイル画像のURL。
    /// 再取得時にこのURLが依然有効なら優先して取得する。
    /// </summary>
    [Header("サムネイル管理")]
    public string previousThumbnailUrl = "";
}
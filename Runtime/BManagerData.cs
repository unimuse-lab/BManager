using UnityEngine;
using System.Collections.Generic;

public class BManagerData : ScriptableObject
{
    public string itemName;
    public string itemUrl;
    public Texture2D thumbnail;
    public List<string> tags = new List<string>();
    public Object linkedAsset;
    public long registrationTimestamp;
}
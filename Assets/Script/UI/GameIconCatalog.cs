using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional art only. Missing licensed assets keep the Japanese text usable.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/表示/ゲームアイコン")]
public sealed class GameIconCatalog : ScriptableObject
{
    [Serializable] public sealed class Entry { public string key; public Sprite sprite; }
    [Header("資源・状態異常・獲得品")]
    public List<Entry> entries = new List<Entry>();
    static GameIconCatalog loaded;
    static bool didLoad;
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    bool indexed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = null; didLoad = false; }
    void OnEnable() { indexed = false; sprites.Clear(); }
    void OnValidate() { indexed = false; sprites.Clear(); }
    public static Sprite Get(string key)
    {
        if (!didLoad) { loaded = Resources.Load<GameIconCatalog>("GeneratedUI/GameIcons"); didLoad = true; }
        if (loaded == null || string.IsNullOrEmpty(key)) return null;
        if (!loaded.indexed)
        {
            loaded.sprites.Clear();
            foreach (var entry in loaded.entries) if (entry != null && !string.IsNullOrEmpty(entry.key)) loaded.sprites[entry.key] = entry.sprite;
            loaded.indexed = true;
        }
        return loaded.sprites.TryGetValue(key, out var sprite) ? sprite : null;
    }
    public static UnityEngine.UI.Image Create(Transform parent, string key, float size = 32)
    {
        var sprite = Get(key); if (sprite == null) return null;
        var go = new GameObject("Icon_" + key, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var icon = go.GetComponent<UnityEngine.UI.Image>(); icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
        icon.rectTransform.sizeDelta = new Vector2(size, size);
        return icon;
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 汎用 UI 生成ヘルパー。すべてのメソッドは static で、どこからでも呼び出せる。
/// </summary>
public static class UIFactory
{
    // ---- Panel ----

    public static RectTransform CreatePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        return rt;
    }

    public static RectTransform CreatePanel(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        return CreatePanel(name, (Transform)parent, anchorMin, anchorMax, pivot, sizeDelta);
    }

    // ---- TextMeshPro ----

    public static TextMeshProUGUI CreateTMP(string name, RectTransform parent,
        string text, float fontSize, TMP_FontAsset font = null)
    {
        return CreateTMP(name, (Transform)parent, text, fontSize, font);
    }

    public static TextMeshProUGUI CreateTMP(string name, Transform parent,
        string text, float fontSize, TMP_FontAsset font = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = BrandGuide.TextPrimary;
        if (font != null) tmp.font = font;
        return tmp;
    }

    // ---- Button ----

    public static Button CreateButton(string name, RectTransform parent,
        string label, float fontSize, Color bgColor, TMP_FontAsset font = null)
    {
        return CreateButton(name, (Transform)parent, label, fontSize, bgColor, font);
    }

    public static Button CreateButton(string name, Transform parent,
        string label, float fontSize, Color bgColor, TMP_FontAsset font = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        BrandGuide.ApplyButtonStyle(btn, bgColor);

        var txt = CreateTMP("Label", go.transform, label, fontSize, font);
        txt.color = BrandGuide.TextPrimary;
        txt.fontStyle = FontStyles.Bold;
        StretchFill(txt.GetComponent<RectTransform>());

        return btn;
    }

    // ---- Image ----

    public static Image AddImage(GameObject go, Color color)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    // ---- Layout ----

    public static void AddHorizontalLayout(GameObject go, float spacing = 4)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(2, 2, 0, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
    }

    // ---- RectTransform helpers ----

    public static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = new Vector2(2, 2);
        rt.offsetMax = new Vector2(-2, -2);
    }

    public static void SetAnchors(TextMeshProUGUI tmp, float xMin, float yMin, float xMax, float yMax)
    {
        SetAnchors(tmp.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
    }

    // ---- Font ----

    public static TMP_FontAsset LoadDefaultFont()
    {
        // NotoSansJP SDF を優先読み込み（日本語対応）
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF");
        if (font != null) return font;

        // SDF アセットがない場合、TTF から動的に生成
        var ttf = Resources.Load<Font>("Fonts & Materials/NotoSansJP-VariableFont_wght");
        if (ttf != null)
        {
            font = TMP_FontAsset.CreateFontAsset(ttf);
            if (font != null)
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                return font;
            }
        }

        // フォールバック: LiberationSans SDF
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            font = TMP_Settings.defaultFontAsset;
        return font;
    }

    public static void SetAsTMPFallback(TMP_FontAsset font)
    {
        if (font == null) return;
        var settings = TMP_Settings.defaultFontAsset;
        if (settings == null || settings == font) return;
        if (settings.fallbackFontAssetTable == null)
            settings.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (!settings.fallbackFontAssetTable.Contains(font))
            settings.fallbackFontAssetTable.Add(font);
    }

    // ---- Reflection helper ----

    public static void SetSerializedField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);
        if (field != null)
            field.SetValue(target, value);
    }

    // ---- Scrollbar ----

    public static Scrollbar CreateVerticalScrollbar(Transform parent)
    {
        // スクロールバー本体
        var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
        sbGo.transform.SetParent(parent, false);
        var sbRT = sbGo.GetComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0);
        sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 0.5f);
        sbRT.sizeDelta = new Vector2(8, 0);
        sbRT.anchoredPosition = Vector2.zero;

        var sbImg = sbGo.AddComponent<Image>();
        sbImg.color = new Color(0.1f, 0.1f, 0.1f, 0.4f);

        // スライドエリア
        var slideArea = new GameObject("SlidingArea", typeof(RectTransform));
        slideArea.transform.SetParent(sbGo.transform, false);
        var saRT = slideArea.GetComponent<RectTransform>();
        StretchFill(saRT);

        // ハンドル
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(slideArea.transform, false);
        var hRT = handle.GetComponent<RectTransform>();
        StretchFill(hRT);

        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(0.55f, 0.50f, 0.38f, 0.65f);

        // Scrollbar コンポーネント
        var sb = sbGo.AddComponent<Scrollbar>();
        sb.handleRect = hRT;
        sb.targetGraphic = hImg;
        sb.direction = Scrollbar.Direction.BottomToTop;

        // ハンドルのホバー色設定
        var colors = sb.colors;
        colors.normalColor = new Color(0.55f, 0.50f, 0.38f, 0.65f);
        colors.highlightedColor = new Color(0.72f, 0.65f, 0.45f, 0.85f);
        colors.pressedColor = new Color(0.42f, 0.38f, 0.28f, 0.90f);
        sb.colors = colors;

        return sb;
    }
}

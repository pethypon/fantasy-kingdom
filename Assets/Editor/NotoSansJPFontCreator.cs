using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// NotoSansJP-VariableFont_wght.ttf から SDF フォントアセットを自動生成する。
/// メニュー: Tools > Generate NotoSansJP SDF
/// また、アセット読み込み後に未生成なら自動実行する。
/// </summary>
public static class NotoSansJPFontCreator
{
    private const string TtfPath = "Assets/Fonts/NotoSansJP-VariableFont_wght.ttf";
    private const string OutputPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/NotoSansJP-VariableFont_wght SDF.asset";

    [MenuItem("Tools/Generate NotoSansJP SDF")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
        {
            if (!EditorUtility.DisplayDialog("NotoSansJP SDF",
                "SDF アセットは既に存在します。再生成しますか？", "はい", "いいえ"))
                return;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (font == null)
        {
            Debug.LogError($"[NotoSansJPFontCreator] TTF が見つかりません: {TtfPath}");
            return;
        }

        // 日本語の基本文字セット (ひらがな、カタカナ、基本漢字、ASCII、記号)
        string characters = BuildCharacterSet();

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            font,
            42,                                    // サンプリングポイントサイズ
            4,                                     // パディング
            GlyphRenderMode.SDFAA,                 // レンダーモード
            2048,                                  // アトラス幅
            2048                                   // アトラス高さ
        );

        if (fontAsset == null)
        {
            Debug.LogError("[NotoSansJPFontCreator] SDF フォントアセットの生成に失敗しました。");
            return;
        }

        // Dynamic にしておけば、足りない文字は実行時に自動追加される
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

        AssetDatabase.CreateAsset(fontAsset, OutputPath);

        // アトラステクスチャをサブアセットとして保存
        if (fontAsset.atlasTextures != null)
        {
            foreach (var tex in fontAsset.atlasTextures)
            {
                if (tex != null)
                {
                    tex.name = fontAsset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }
        }

        // マテリアルをサブアセットとして保存
        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[NotoSansJPFontCreator] SDF フォントアセットを生成しました: {OutputPath}");
    }

    /// <summary>
    /// 基本的な ASCII + ひらがな + カタカナ + 主要記号の文字セットを返す。
    /// Dynamic モードなので漢字は実行時に自動生成される。
    /// </summary>
    private static string BuildCharacterSet()
    {
        var sb = new System.Text.StringBuilder(512);

        // ASCII printable
        for (char c = (char)0x20; c <= (char)0x7E; c++) sb.Append(c);

        // ひらがな (U+3040-U+309F)
        for (char c = '\u3040'; c <= '\u309F'; c++) sb.Append(c);

        // カタカナ (U+30A0-U+30FF)
        for (char c = '\u30A0'; c <= '\u30FF'; c++) sb.Append(c);

        // 全角記号・数字 (U+FF01-U+FF5E)
        for (char c = '\uFF01'; c <= '\uFF5E'; c++) sb.Append(c);

        // 主要な日本語記号
        sb.Append("、。「」『』【】〈〉《》〔〕…―─×÷±≠≦≧∞∴♠♣♥♦★☆○●◎◇◆□■△▲▽▼→←↑↓");

        return sb.ToString();
    }

    /// <summary>
    /// Unity エディタ起動時に SDF アセットが存在しなければ自動生成を促す。
    /// </summary>
    [InitializeOnLoadMethod]
    private static void CheckOnLoad()
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null) return;
        if (AssetDatabase.LoadAssetAtPath<Font>(TtfPath) == null) return;

        Debug.Log("[NotoSansJPFontCreator] NotoSansJP SDF が未生成です。Tools > Generate NotoSansJP SDF から生成してください。");

        // 初回インポート完了後に自動生成を試みる
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null) return;
            Debug.Log("[NotoSansJPFontCreator] NotoSansJP SDF を自動生成します...");
            Generate();
        };
    }
}

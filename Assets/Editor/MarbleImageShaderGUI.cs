using UnityEditor;
using UnityEngine;

public sealed class MarbleImageShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        void Property(string id, string label, string tip = "") => editor.ShaderProperty(FindProperty(id, properties), new GUIContent(label, tip));
        void Heading(string title) { EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel); }
        Heading("画像と地色");
        var image=FindProperty("_MainTex",properties);
        editor.TexturePropertySingleLine(new GUIContent("大理石の画像"),image);
        editor.TextureScaleOffsetProperty(image);
        Property("_Color","地色","筋を除いた部分だけの色です。");
        Property("_PatternStrength","元画像の濃さ","色分けの範囲には影響しません。");
        Heading("薄い筋");
        Property("_PatternColor","薄い筋の色");
        Property("_FineTint","色の強さ","0は元画像、1は指定色です。");
        Property("_VeinThreshold","筋として拾う暗さ","上げるほど明るい地色を除外し、色を付ける範囲が狭くなります。");
        Property("_VeinFeather","境界のぼかし");
        Heading("濃い筋");
        Property("_DeepPatternColor","濃い筋の色");
        Property("_DeepTint","色の強さ","0は元画像、1は指定色です。");
        Property("_DeepThreshold","濃い筋として拾う暗さ","薄い筋の暗さより高くすると、特に暗い筋だけを分離できます。");
        Property("_DeepFeather","境界のぼかし");
        Heading("色分けの確認");
        Property("_MaskPreview","色分けを表示");
        EditorGUILayout.HelpBox("確認表示：白＝地色、青＝薄い筋、赤＝濃い筋。通常の見た目に戻すにはチェックを外してください。",MessageType.Info);
        Heading("投影と質感");
        Property("_Triplanar","UVを使わずに投影");
        Property("_ProjectionScale","模様の繰り返し密度");
        var normal=FindProperty("_BumpMap",properties);
        editor.TexturePropertySingleLine(new GUIContent("法線マップ"),normal);
        editor.TextureScaleOffsetProperty(normal);
        Property("_BumpScale","凹凸の強さ");
        Property("_Glossiness","光沢");
        Property("_Metallic","メタリック");
        editor.EnableInstancingField();
    }
}

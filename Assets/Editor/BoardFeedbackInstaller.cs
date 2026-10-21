#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, non-destructive defaults. Existing user sound/volume choices are preserved.</summary>
public static class BoardFeedbackInstaller
{
    public const string SettingsPath = "Assets/Resources/GameContent/BoardFeedback.asset";
    const string Folder = "Assets/_FantasyKingdom/FeedbackAudio";
    [MenuItem("Fantasy Kingdom/開発スタジオ/見た目/エフェクトと効果音を導入・不足分を補完")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("ゲームを停止してから導入してください。");
        EnsureFolder("Assets/Resources/GameContent"); EnsureFolder(Folder);
        var shader = Shader.Find("Fantasy Kingdom/BoardFeedback");
        if (shader == null) throw new InvalidOperationException("演出用シェーダーが見つかりません。");
        string materialPath = Folder + "/BoardFeedback.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader) { name = "短時間の盤面エフェクト" }; AssetDatabase.CreateAsset(material, materialPath); }
        var settings = AssetDatabase.LoadAssetAtPath<BoardFeedbackSettings>(SettingsPath);
        if (settings == null) { settings = ScriptableObject.CreateInstance<BoardFeedbackSettings>(); settings.name = "エフェクトと効果音"; AssetDatabase.CreateAsset(settings, SettingsPath); }
        if (settings.sounds == null) settings.sounds = new System.Collections.Generic.List<BoardFeedbackSettings.SoundEntry>();
        if (settings.effectMaterial == null) settings.effectMaterial = material;
        foreach (BoardFeedbackKind kind in Enum.GetValues(typeof(BoardFeedbackKind)))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Clips/Board" + kind + ".wav");
            if (clip == null) throw new InvalidOperationException("効果音が見つかりません: " + kind);
            var entry = settings.sounds.Find(value => value != null && value.kind == kind);
            if (entry == null) settings.sounds.Add(new BoardFeedbackSettings.SoundEntry { kind = kind, clip = clip,
                volume = kind == BoardFeedbackKind.Move || kind == BoardFeedbackKind.UiClick ? .35f : .7f });
            else if (entry.clip == null) entry.clip = clip;
        }
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Debug.Log("[BoardFeedback] 13種類の効果音と共有エフェクトを導入。既存の音量・割り当ては保持しました。");
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif

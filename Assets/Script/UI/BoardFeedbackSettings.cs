using System;
using System.Collections.Generic;
using UnityEngine;

public enum BoardFeedbackKind
{
    [InspectorName("移動")] Move, [InspectorName("近接攻撃")] Attack,
    [InspectorName("射撃")] Projectile, [InspectorName("魔法攻撃")] Magic,
    [InspectorName("被弾")] Hit, [InspectorName("回復")] Heal,
    [InspectorName("シールド")] Shield, [InspectorName("召喚")] Summon,
    [InspectorName("建築")] Build, [InspectorName("補助スキル")] Skill,
    [InspectorName("勝利")] Victory, [InspectorName("敗北")] Defeat,
    [InspectorName("ボタン")] UiClick
}

/// <summary>Shared, editable presentation settings; never part of combat or saved game rules.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/見た目/エフェクトと効果音")]
public sealed class BoardFeedbackSettings : ScriptableObject
{
    public const string ResourcePath = "GameContent/BoardFeedback";
    [Serializable] public sealed class SoundEntry
    {
        [InspectorName("種類")] public BoardFeedbackKind kind;
        [InspectorName("効果音")] public AudioClip clip;
        [InspectorName("音量"), Range(0, 1)] public float volume = .7f;
    }
    [Header("エフェクト")]
    [InspectorName("エフェクトを有効化")] public bool effectsEnabled = true;
    [InspectorName("大きさ"), Range(.25f, 2)] public float effectScale = 1;
    [InspectorName("同時表示の上限"), Range(1, 16)] public int maxVisualEffects = 16;
    [InspectorName("共通マテリアル")] public Material effectMaterial;
    [Header("効果音")]
    [InspectorName("効果音を有効化")] public bool soundsEnabled = true;
    [InspectorName("全体音量"), Range(0, 1)] public float masterVolume = .35f;
    [InspectorName("同時再生の上限"), Range(1, 8)] public int maxAudioVoices = 8;
    [InspectorName("連続音の最小間隔（秒）"), Range(.02f, .3f)] public float minimumSoundInterval = .045f;
    [InspectorName("効果音一覧")] public List<SoundEntry> sounds = new List<SoundEntry>();

    static BoardFeedbackSettings cached;
    static bool loaded;
    public static BoardFeedbackSettings Active
    {
        get
        {
            if (!loaded) { cached = Resources.Load<BoardFeedbackSettings>(ResourcePath); loaded = true; }
            return cached;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { cached = null; loaded = false; }
}

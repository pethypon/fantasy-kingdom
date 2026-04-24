using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 実績システム。ゲーム中の特定イベントを検知して実績フラグを立て、
/// 永続化（PlayerPrefs）と画面通知を行う。
///
/// 新しい実績を追加するには:
///   1. AchievementId に enum を追加
///   2. _definitions に Title/Description を登録
///   3. 対応するゲームイベントで AchievementSystem.Unlock(id) を呼ぶ
/// </summary>
public class AchievementSystem : MonoBehaviour
{
    public static AchievementSystem Instance { get; private set; }

    public enum AchievementId
    {
        FirstBlood,            // 初討伐
        WildBossSlayer,        // 強敵撃破
        DungeonConqueror,      // ダンジョン1つ制覇
        CrystalSniper,         // クリスタル残HP 10% でのトドメ
        MassSummoner,          // 1試合で10体召喚
        BuilderTycoon,         // 1試合で15建築
        Unscathed,             // 被ダメ 0 で勝利
        LvMax,                 // Lv15 到達
        SkillMaster,           // スキル 10回使用
        ArtifactCollector,     // アーティファクト 3個入手（複数試合累計）
    }

    public class AchievementInfo
    {
        public string Title;
        public string Description;
        public string PrefsKey;
    }

    static readonly Dictionary<AchievementId, AchievementInfo> _definitions
        = new Dictionary<AchievementId, AchievementInfo>
    {
        { AchievementId.FirstBlood,        new AchievementInfo { Title = "初討伐",                Description = "最初のユニットを倒した" } },
        { AchievementId.WildBossSlayer,    new AchievementInfo { Title = "縄張り荒らし",          Description = "強敵を撃破した" } },
        { AchievementId.DungeonConqueror,  new AchievementInfo { Title = "迷宮制覇",              Description = "ダンジョンを1箇所制圧した" } },
        { AchievementId.CrystalSniper,     new AchievementInfo { Title = "狙撃者",                Description = "敵クリスタル残HP10%以下を討ち取った" } },
        { AchievementId.MassSummoner,      new AchievementInfo { Title = "軍団指揮官",            Description = "1試合で10体以上召喚した" } },
        { AchievementId.BuilderTycoon,     new AchievementInfo { Title = "都市計画家",            Description = "1試合で15棟以上建築した" } },
        { AchievementId.Unscathed,         new AchievementInfo { Title = "無傷の王",              Description = "被ダメージ0で勝利した" } },
        { AchievementId.LvMax,             new AchievementInfo { Title = "極致",                  Description = "いずれかの駒をLv15に到達させた" } },
        { AchievementId.SkillMaster,       new AchievementInfo { Title = "スキル職人",            Description = "1試合でスキルを10回使用した" } },
        { AchievementId.ArtifactCollector, new AchievementInfo { Title = "収集家",                Description = "累計でアーティファクトを3個獲得" } },
    };

    /// <summary>解除時イベント（通知UI等のフック用）</summary>
    public event Action<AchievementId, AchievementInfo> OnUnlocked;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        foreach (var kvp in _definitions)
            kvp.Value.PrefsKey = $"ach_{kvp.Key}";
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public static AchievementSystem GetOrCreate()
    {
        if (Instance == null)
        {
            var go = new GameObject("AchievementSystem");
            go.AddComponent<AchievementSystem>();
        }
        return Instance;
    }

    public bool IsUnlocked(AchievementId id)
    {
        if (!_definitions.TryGetValue(id, out var info)) return false;
        return PlayerPrefs.GetInt(info.PrefsKey, 0) == 1;
    }

    public void Unlock(AchievementId id)
    {
        if (!_definitions.TryGetValue(id, out var info)) return;
        if (IsUnlocked(id)) return;
        PlayerPrefs.SetInt(info.PrefsKey, 1);
        PlayerPrefs.Save();
        Debug.Log($"[Achievement] 解除: {info.Title} — {info.Description}");
        ToastUI.Show($"<b>実績解除</b>\n{info.Title}", 3.5f);
        OnUnlocked?.Invoke(id, info);
    }

    // ================================================================
    //  ゲームイベントからの呼び出しポイント
    // ================================================================
    public void OnKill()
    {
        Unlock(AchievementId.FirstBlood);
    }

    public void OnWildBossDefeated()
    {
        Unlock(AchievementId.WildBossSlayer);
    }

    public void OnDungeonCleared()
    {
        Unlock(AchievementId.DungeonConqueror);
        int total = PlayerPrefs.GetInt("artifact_total", 0) + 1;
        PlayerPrefs.SetInt("artifact_total", total);
        if (total >= 3) Unlock(AchievementId.ArtifactCollector);
    }

    public void OnMatchEnd(bool victory)
    {
        var m = MatchStats.Instance;
        if (m == null) return;
        if (m.PlayerSummons >= 10) Unlock(AchievementId.MassSummoner);
        if (m.PlayerBuildings >= 15) Unlock(AchievementId.BuilderTycoon);
        if (m.PlayerSkillsUsed >= 10) Unlock(AchievementId.SkillMaster);
        if (victory && m.PlayerDamageTaken == 0) Unlock(AchievementId.Unscathed);
    }

    public void OnLevelUp(int newLevel)
    {
        if (newLevel >= 15) Unlock(AchievementId.LvMax);
    }

    public void OnCrystalKill(float targetHPRatio)
    {
        if (targetHPRatio <= 0.10f) Unlock(AchievementId.CrystalSniper);
    }

    public IReadOnlyDictionary<AchievementId, AchievementInfo> AllDefinitions => _definitions;
}

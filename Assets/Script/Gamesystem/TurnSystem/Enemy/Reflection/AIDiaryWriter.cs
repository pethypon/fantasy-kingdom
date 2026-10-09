using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class AIReflectionPatternSummary
{
    public string Key;
    public int Samples;
    public float Reward, LearnedChange;
}

/// <summary>Incremental interval aggregation; diary output never replays the complete action history.</summary>
[Serializable]
public sealed class AIReflectionInterval
{
    public int FirstTurn = -1, LastTurn, Actions, Successful, Failed, Repeated, NormalKills, FormationKills;
    public int Artifacts, StableTurns, OwnLosses;
    public float Reward, PenaltyReward, VictoryReward;
    public int TurningPointTurn;
    public float TurningPointReward;
    public string TurningPointPattern;
    public AIActionContextSnapshot Before, After;
    public List<FailureReasonCount> Failures = new List<FailureReasonCount>();
    public List<AIReflectionPatternSummary> Patterns = new List<AIReflectionPatternSummary>();
    [NonSerialized] Dictionary<string, AIReflectionPatternSummary> index;
    public void Observe(AIActionRecord record, float learnedChange, int patternLimit)
    {
        if (FirstTurn < 0) FirstTurn = record.Turn;
        LastTurn = record.Turn; Actions++;
        if (Before == null) Before = record.Before;
        After = record.After;
        if (record.StrategicOutcome == AIStrategicOutcome.Progress) Successful++;
        if (record.StrategicOutcome == AIStrategicOutcome.Failure) Failed++;
        if (record.IsRepeatedAction) Repeated++;
        Reward += record.Reward;
        var outcome = record.Outcome;
        NormalKills += outcome.EnemyKills - outcome.FormationKills;
        FormationKills += outcome.FormationKills; Artifacts += outcome.ArtifactsAcquired; OwnLosses += outcome.OwnLosses;
        PenaltyReward += record.Reward - (outcome.EnemyKills + outcome.FormationKills + outcome.ArtifactsAcquired)
            - record.EconomyStableReward;
        AIActionLearningProfile.CountFailure(Failures, record.FailureReason);
        EnsureIndex();
        string key = record.ContextKey + " / " + record.ActionKey;
        if (!index.TryGetValue(key, out var pattern))
        {
            if (Patterns.Count >= patternLimit) return;
            pattern = new AIReflectionPatternSummary { Key = key }; Patterns.Add(pattern); index[key] = pattern;
        }
        pattern.Samples++; pattern.Reward += record.Reward; pattern.LearnedChange += learnedChange;
        if (Mathf.Abs(record.Reward) > Mathf.Abs(TurningPointReward))
        { TurningPointTurn = record.Turn; TurningPointReward = record.Reward; TurningPointPattern = key; }
    }
    public void Correct(AIActionRecord record, float rewardDelta, float learnedChange)
    {
        Reward += rewardDelta; NormalKills--; FormationKills++;
        EnsureIndex();
        if (index.TryGetValue(record.ContextKey + " / " + record.ActionKey, out var pattern))
        { pattern.Reward += rewardDelta; pattern.LearnedChange += learnedChange; }
    }
    void EnsureIndex()
    {
        if (index != null) return;
        index = new Dictionary<string, AIReflectionPatternSummary>(Patterns.Count);
        foreach (var item in Patterns) if (item != null && item.Key != null) index[item.Key] = item;
    }
}

public sealed class AIDiaryWriter
{
    readonly AIReflectionConfig config;
    bool persistent;
    readonly string directory;
    public string LastText { get; private set; }
    public string LastPath { get; private set; }
    public void SetPersistenceAllowed(bool allowed) { persistent &= allowed; }
    public AIDiaryWriter(string storageRoot, AIReflectionConfig config, bool persistent = true)
    {
        this.config = config; this.persistent = persistent;
        directory = Path.Combine(storageRoot ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI"), "Diary");
    }
    public void Write(AIReflectionBattleState state, string result)
    {
        var interval = state.Interval;
        var builder = new StringBuilder(2048);
        builder.AppendLine("Fantasy Kingdom AI日記").AppendLine("=============================");
        builder.Append("戦闘ID: ").AppendLine(state.BattleId);
        builder.Append("陣営: ").Append(state.Faction).Append("　脅威度: ").Append(state.ThreatLevel)
            .Append("　性格: ").AppendLine(state.Personality);
        builder.Append("ターン: ").Append(interval.FirstTurn < 0 ? state.LastStartedTurn : interval.FirstTurn)
            .Append(" ～ ").Append(state.LastStartedTurn).Append("　自軍行動ターン: ").AppendLine(state.OwnTurns.ToString());
        builder.Append("結果: ").AppendLine(result);
        builder.Append("主要作戦: ").AppendLine(interval.After?.Strategy ?? state.LastSnapshot?.Strategy ?? "未記録");
        if (result != "INTERVAL")
            foreach (var usage in state.StrategyUse)
                builder.Append("作戦利用: ").Append(usage.Strategy).Append(" 脅威度帯 ").Append(usage.ThreatBand)
                    .Append(" / ").Append(usage.OwnTurns).AppendLine(" 自軍ターン");
        builder.Append("期間報酬: ").Append(interval.Reward.ToString("F2"))
            .Append("　戦闘累計報酬: ").AppendLine(state.BattleReward.ToString("F2"));
        builder.Append("内訳: 通常撃破 ").Append(interval.NormalKills).Append("　陣形撃破 ")
            .Append(interval.FormationKills).Append(" ×2　Artifact ").Append(interval.Artifacts)
            .Append("　経済安定 ").Append(interval.StableTurns).Append("　反復・失敗減点 ")
            .Append(interval.PenaltyReward.ToString("F2")).Append("　勝敗報酬 ").AppendLine(interval.VictoryReward.ToString("F2"));
        builder.Append("行動数: ").Append(interval.Actions).Append("　戦略的進展: ").Append(interval.Successful)
            .Append("　失敗: ").Append(interval.Failed).Append("　無意味な反復: ").AppendLine(interval.Repeated.ToString());
        builder.Append("自軍損失: ").Append(interval.OwnLosses).Append("（累計 ").Append(state.TotalOwnLosses)
            .Append("）　敵撃破累計: ").Append(state.TotalKills).Append("　Artifact累計: ").AppendLine(state.TotalArtifacts.ToString());
        foreach (var failure in interval.Failures)
            builder.Append("失敗理由: ").Append(failure.Reason).Append(" ").AppendLine(failure.Count.ToString());
        AppendChanges(builder, interval.Before, interval.After ?? state.LastSnapshot);
        AIReflectionPatternSummary best = null, worst = null, positive = null, negative = null;
        foreach (var pattern in interval.Patterns)
        {
            if (best == null || pattern.Reward / Math.Max(1, pattern.Samples) > best.Reward / Math.Max(1, best.Samples)) best = pattern;
            if (worst == null || pattern.Reward / Math.Max(1, pattern.Samples) < worst.Reward / Math.Max(1, worst.Samples)) worst = pattern;
            if (positive == null || pattern.LearnedChange > positive.LearnedChange) positive = pattern;
            if (negative == null || pattern.LearnedChange < negative.LearnedChange) negative = pattern;
        }
        AppendPattern(builder, "最多成功パターン", best); AppendPattern(builder, "最低評価パターン", worst);
        AppendPattern(builder, "学習評価の最大上昇", positive); AppendPattern(builder, "学習評価の最大低下", negative);
        if (result == "DEFEAT")
            builder.Append("敗北時の状態: クリスタルHP ").Append(state.LastSnapshot?.OwnCrystalHP ?? 0)
                .Append("　経済 ").AppendLine((state.LastSnapshot?.EconomyState ?? EconomicState.Healthy).ToString());
        if (result != "INTERVAL") AppendBattleSummary(builder, state.BattleSummary);
        LastText = builder.ToString(); LastPath = null;
        if (persistent && config.EnableFileDiary)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string name = "AI_Diary_" + state.Faction + "_" + AIReflectionSaveRepository.SafeFileName(state.BattleId)
                    + "_" + (result == "INTERVAL" ? "Turn" + state.OwnTurns : result) + ".txt";
                LastPath = Path.Combine(directory, name);
                File.WriteAllText(LastPath, LastText, new UTF8Encoding(false));
            }
            catch (Exception exception) when (AIReflectionSaveRepository.IsRecoverable(exception))
            { Debug.LogWarning("[AI自己評価] 日記を保存できませんでした。AIは続行します: " + exception.Message); }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (config.EnableConsoleDiary)
            Debug.Log("[AI日記] " + state.Faction + " 自軍ターン" + state.OwnTurns + " " + result
                + " 行動=" + interval.Actions + " 成功=" + interval.Successful + " 失敗=" + interval.Failed
                + " 反復=" + interval.Repeated + " 期間報酬=" + interval.Reward.ToString("F2")
                + " 累計=" + state.BattleReward.ToString("F2") + (LastPath == null ? "" : " 保存=" + LastPath));
#endif
    }
    static void AppendPattern(StringBuilder builder, string label, AIReflectionPatternSummary pattern)
    {
        if (pattern == null) return;
        builder.Append(label).Append(": ").AppendLine(pattern.Key);
        builder.Append("  サンプル ").Append(pattern.Samples).Append("　平均報酬 ")
            .Append((pattern.Reward / Math.Max(1, pattern.Samples)).ToString("F2"))
            .Append("　学習変化 ").AppendLine(pattern.LearnedChange.ToString("F3"));
    }
    static void AppendBattleSummary(StringBuilder builder, AIReflectionInterval summary)
    {
        if (summary == null) return;
        builder.AppendLine("戦闘全体の評価:");
        builder.Append("  行動 ").Append(summary.Actions).Append("　成功 ").Append(summary.Successful)
            .Append("　失敗 ").Append(summary.Failed).Append("　反復 ").AppendLine(summary.Repeated.ToString());
        foreach (var reason in summary.Failures)
            builder.Append("  累計失敗理由: ").Append(reason.Reason).Append(" ").AppendLine(reason.Count.ToString());
        AIReflectionPatternSummary best = null, worst = null, increase = null, decrease = null;
        foreach (var pattern in summary.Patterns)
        {
            if (best == null || pattern.Reward > best.Reward) best = pattern;
            if (worst == null || pattern.Reward < worst.Reward) worst = pattern;
            if (increase == null || pattern.LearnedChange > increase.LearnedChange) increase = pattern;
            if (decrease == null || pattern.LearnedChange < decrease.LearnedChange) decrease = pattern;
        }
        AppendPattern(builder, "戦闘全体の成功パターン", best); AppendPattern(builder, "戦闘全体の失敗パターン", worst);
        AppendPattern(builder, "今後優先度が上がった行動", increase); AppendPattern(builder, "今後優先度が下がった行動", decrease);
        if (summary.TurningPointPattern != null)
            builder.Append("最大の評価変化: Turn ").Append(summary.TurningPointTurn).Append(" ")
                .Append(summary.TurningPointReward.ToString("F2")).Append(" ").AppendLine(summary.TurningPointPattern);
    }
    static void AppendChanges(StringBuilder builder, AIActionContextSnapshot before, AIActionContextSnapshot after)
    {
        if (before == null || after == null) return;
        builder.Append("経済: ").Append(before.EconomyState).Append(" → ").AppendLine(after.EconomyState.ToString());
        builder.Append("資源推移: 木 ").Append(before.Wood).Append('→').Append(after.Wood)
            .Append(" 石 ").Append(before.Stone).Append('→').Append(after.Stone)
            .Append(" 鉄 ").Append(before.Iron).Append('→').Append(after.Iron)
            .Append(" 魔石 ").Append(before.MagicOre).Append('→').Append(after.MagicOre)
            .Append(" 小麦 ").Append(before.Wheat).Append('→').Append(after.Wheat)
            .Append(" パン ").Append(before.Bread).Append('→').Append(after.Bread)
            .Append(" 水 ").Append(before.Water).Append('→').Append(after.Water)
            .Append(" 市民 ").Append(before.Citizen).Append('→').AppendLine(after.Citizen.ToString());
        builder.Append("自軍駒 ").Append(before.OwnUnitCount).Append('→').Append(after.OwnUnitCount)
            .Append("　視認した敵 ").Append(before.VisibleEnemyCount).Append('→').Append(after.VisibleEnemyCount)
            .Append("　領土 ").Append(before.TerritoryCount).Append('→').Append(after.TerritoryCount)
            .Append("　クリスタルHP ").Append(before.OwnCrystalHP).Append('→').AppendLine(after.OwnCrystalHP.ToString());
        builder.Append("局所戦力比 ").Append(before.LocalPowerRatio.ToString("F2")).Append('→')
            .AppendLine(after.LocalPowerRatio.ToString("F2"));
    }
}

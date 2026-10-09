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
    public int FirstOwnTurn = -1;
    public int PatternLimit = 2048;
    public AIActionContextSnapshot Before, After;
    public AIRewardBreakdown Breakdown = new AIRewardBreakdown();
    public List<FailureReasonCount> Failures = new List<FailureReasonCount>();
    public List<SuccessReasonCount> Successes = new List<SuccessReasonCount>();
    public List<AIReflectionPatternSummary> Patterns = new List<AIReflectionPatternSummary>();
    public List<AIReflectionDecisionTrace> TraceExamples = new List<AIReflectionDecisionTrace>();
    [NonSerialized] Dictionary<string, AIReflectionPatternSummary> index;
    public void Observe(AIActionRecord record, float learnedChange, int patternLimit)
    {
        if (record == null) return;
        PatternLimit = Mathf.Clamp(patternLimit, 1, 10000); EnsureIndex();
        if (FirstTurn < 0) FirstTurn = record.Turn;
        LastTurn = record.Turn; Actions++;
        if (Before == null) Before = record.Before;
        After = record.After;
        if (record.StrategicOutcome == AIStrategicOutcome.Progress) Successful++;
        if (record.StrategicOutcome == AIStrategicOutcome.Failure) Failed++;
        if (record.IsRepeatedAction) Repeated++;
        if (record.RewardBreakdown == null)
        { record.RewardBreakdown = LegacyBreakdown(record); record.ImmediateReward = record.Reward; }
        var breakdown = record.RewardBreakdown;
        Reward += record.Reward; AddBreakdown(Breakdown, breakdown);
        var outcome = record.Outcome ?? new AIActionOutcome();
        NormalKills += Math.Max(0, outcome.EnemyKills - outcome.FormationKills);
        FormationKills += outcome.FormationKills; Artifacts += outcome.ArtifactsAcquired; OwnLosses += outcome.OwnLosses;
        PenaltyReward += Penalties(breakdown);
        AIActionLearningProfile.CountFailure(Failures, record.FailureReason);
        CountSuccesses(record.SuccessReasons);
        AIReflectionDecisionTrace.UpdateOutcome(record); AddTrace(record.DecisionTrace);
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
        CorrectReward(record, rewardDelta, learnedChange, new AIRewardBreakdown { Combat = rewardDelta });
        NormalKills = Math.Max(0, NormalKills - 1); FormationKills++;
    }
    public void CorrectReward(AIActionRecord record, float rewardDelta, float learnedChange,
        AIRewardBreakdown breakdownDelta = null, int occurrenceTurn = -1)
    {
        if (record == null) return;
        EnsureIndex();
        bool belongs = FirstTurn >= 0 && record.Turn >= FirstTurn && record.Turn <= LastTurn;
        int occurred = occurrenceTurn >= 0 ? occurrenceTurn : Math.Max(LastTurn, record.Turn);
        if (FirstTurn < 0) FirstTurn = occurred;
        LastTurn = Math.Max(LastTurn, occurred);
        rewardDelta = AIReflectionConfig.Finite(rewardDelta); learnedChange = AIReflectionConfig.Finite(learnedChange);
        var delta = breakdownDelta ?? new AIRewardBreakdown { Efficiency = rewardDelta };
        Reward += rewardDelta; AddBreakdown(Breakdown, delta); PenaltyReward += Penalties(delta);
        string key = record.ContextKey + " / " + record.ActionKey;
        EnsureIndex();
        if (!index.TryGetValue(key, out var pattern) && Patterns.Count < Mathf.Clamp(PatternLimit, 1, 10000))
        { pattern = new AIReflectionPatternSummary { Key = key }; Patterns.Add(pattern); index[key] = pattern; }
        if (pattern != null) { pattern.Reward += rewardDelta; pattern.LearnedChange += learnedChange; }
        if (belongs)
        {
            AIReflectionDecisionTrace.UpdateOutcome(record); AddTrace(record.DecisionTrace);
        }
        else AddTrace(AIReflectionDecisionTrace.BuildCorrection(record, rewardDelta, learnedChange, occurred));
        if (Mathf.Abs(rewardDelta) > Mathf.Abs(TurningPointReward))
        { TurningPointTurn = occurred; TurningPointReward = rewardDelta; TurningPointPattern = key; }
    }
    public void AddReward(AIRewardBreakdown delta, int occurrenceTurn)
    {
        if (delta == null) return;
        EnsureIndex();
        if (FirstTurn < 0) FirstTurn = occurrenceTurn;
        LastTurn = Math.Max(LastTurn, occurrenceTurn);
        AddBreakdown(Breakdown, delta); Reward += delta.Total; PenaltyReward += Penalties(delta);
    }
    public void MergeFrom(AIReflectionInterval source, int patternLimit)
    {
        if (source == null || ReferenceEquals(source, this)) return;
        PatternLimit = Mathf.Clamp(patternLimit, 1, 10000); EnsureIndex();
        if (source.FirstTurn >= 0) FirstTurn = FirstTurn < 0 ? source.FirstTurn : Math.Min(FirstTurn, source.FirstTurn);
        LastTurn = Math.Max(LastTurn, source.LastTurn);
        if (source.FirstOwnTurn >= 0) FirstOwnTurn = FirstOwnTurn < 0 ? source.FirstOwnTurn : Math.Min(FirstOwnTurn, source.FirstOwnTurn);
        Actions = AddCount(Actions, source.Actions); Successful = AddCount(Successful, source.Successful);
        Failed = AddCount(Failed, source.Failed); Repeated = AddCount(Repeated, source.Repeated);
        NormalKills = AddCount(NormalKills, source.NormalKills); FormationKills = AddCount(FormationKills, source.FormationKills);
        Artifacts = AddCount(Artifacts, source.Artifacts); StableTurns = AddCount(StableTurns, source.StableTurns);
        OwnLosses = AddCount(OwnLosses, source.OwnLosses);
        Reward = AddValue(Reward, source.Reward); PenaltyReward = AddValue(PenaltyReward, source.PenaltyReward);
        VictoryReward = AddValue(VictoryReward, source.VictoryReward);
        if (Before == null && source.Before != null) Before = source.Before.Copy();
        if (source.After != null) After = source.After.Copy();
        if (source.Breakdown != null) AddBreakdown(Breakdown, source.Breakdown);
        if (source.Failures != null)
            foreach (var reason in source.Failures)
            {
                if (reason == null || reason.Reason == AIFailureReason.None) continue;
                var existing = Failures.Find(item => item.Reason == reason.Reason);
                if (existing != null) existing.Count = AddCount(existing.Count, reason.Count);
                else if (Failures.Count < 32) Failures.Add(new FailureReasonCount { Reason = reason.Reason, Count = Math.Max(0, reason.Count) });
            }
        if (source.Successes != null)
            foreach (var reason in source.Successes)
            {
                if (reason == null || reason.Reason == AISuccessReason.None) continue;
                var existing = Successes.Find(item => item.Reason == reason.Reason);
                if (existing != null) existing.Count = AddCount(existing.Count, reason.Count);
                else if (Successes.Count < 32) Successes.Add(new SuccessReasonCount { Reason = reason.Reason, Count = Math.Max(0, reason.Count) });
            }
        if (source.Patterns != null)
            foreach (var item in source.Patterns)
            {
                if (item == null || item.Key == null) continue;
                if (!index.TryGetValue(item.Key, out var pattern))
                {
                    if (Patterns.Count >= PatternLimit) continue;
                    pattern = new AIReflectionPatternSummary { Key = item.Key }; Patterns.Add(pattern); index[item.Key] = pattern;
                }
                pattern.Samples = AddCount(pattern.Samples, item.Samples);
                pattern.Reward = AddValue(pattern.Reward, item.Reward); pattern.LearnedChange = AddValue(pattern.LearnedChange, item.LearnedChange);
            }
        if (source.TraceExamples != null) foreach (var trace in source.TraceExamples) AddTrace(trace);
        if (Mathf.Abs(source.TurningPointReward) > Mathf.Abs(TurningPointReward))
        { TurningPointTurn = source.TurningPointTurn; TurningPointReward = source.TurningPointReward; TurningPointPattern = source.TurningPointPattern; }
        index = null; EnsureIndex();
    }
    static int AddCount(int first, int second) => (int)Math.Min(int.MaxValue, (long)Math.Max(0, first) + Math.Max(0, second));
    static float AddValue(float first, float second) => (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue,
        (double)AIReflectionConfig.Finite(first) + AIReflectionConfig.Finite(second)));
    void CountSuccesses(List<AISuccessReason> reasons)
    {
        if (reasons == null) return;
        var seen = new HashSet<AISuccessReason>();
        foreach (var reason in reasons)
        {
            if (reason == AISuccessReason.None || !seen.Add(reason)) continue;
            SuccessReasonCount found = null;
            foreach (var item in Successes) if (item.Reason == reason) { found = item; break; }
            if (found != null) found.Count = Math.Min(1000000, found.Count + 1);
            else Successes.Add(new SuccessReasonCount { Reason = reason, Count = 1 });
        }
    }
    void AddTrace(AIReflectionDecisionTrace trace)
    {
        if (trace == null) return;
        for (int i = 0; i < TraceExamples.Count; i++)
            if (TraceExamples[i].ActionId == trace.ActionId && TraceExamples[i].IsRewardCorrection == trace.IsRewardCorrection)
            { TraceExamples[i] = trace.Copy(); return; }
        if (TraceExamples.Count < 3) { TraceExamples.Add(trace.Copy()); return; }
        int least = 0;
        for (int i = 1; i < TraceExamples.Count; i++) if (Significance(TraceExamples[i]) < Significance(TraceExamples[least])) least = i;
        if (Significance(trace) > Significance(TraceExamples[least])) TraceExamples[least] = trace.Copy();
    }
    static float Significance(AIReflectionDecisionTrace trace) => Mathf.Abs(trace.FinalReward) + Mathf.Abs(trace.LearningValueChange);
    internal static AIRewardBreakdown LegacyBreakdown(AIActionRecord record)
    {
        var outcome = record.Outcome;
        var result = new AIRewardBreakdown { Combat = (outcome?.EnemyKills ?? 0) + (outcome?.FormationKills ?? 0),
            Artifact = outcome?.ArtifactsAcquired ?? 0, Economy = record.EconomyStableReward };
        result.Efficiency = record.Reward - result.Total;
        return result;
    }
    internal static float Penalties(AIRewardBreakdown value) => value.RepeatPenalty + value.FailurePenalty
        + value.IdleArmyPenalty + value.OverproductionPenalty + value.MissedOpportunityPenalty;
    internal static void AddBreakdown(AIRewardBreakdown target, AIRewardBreakdown delta)
    {
        target.Combat += delta.Combat; target.Artifact += delta.Artifact; target.Survival += delta.Survival;
        target.Position += delta.Position; target.LocalPower += delta.LocalPower; target.Objective += delta.Objective;
        target.Economy += delta.Economy; target.Information += delta.Information; target.Defense += delta.Defense;
        target.Efficiency += delta.Efficiency; target.Preparation += delta.Preparation; target.DelayedReward += delta.DelayedReward;
        target.RepeatPenalty += delta.RepeatPenalty; target.FailurePenalty += delta.FailurePenalty;
        target.IdleArmyPenalty += delta.IdleArmyPenalty; target.OverproductionPenalty += delta.OverproductionPenalty;
        target.MissedOpportunityPenalty += delta.MissedOpportunityPenalty;
    }
    void EnsureIndex()
    {
        if (Breakdown == null) Breakdown = new AIRewardBreakdown();
        if (Failures == null) Failures = new List<FailureReasonCount>();
        if (Successes == null) Successes = new List<SuccessReasonCount>();
        if (TraceExamples == null) TraceExamples = new List<AIReflectionDecisionTrace>();
        if (Patterns == null) Patterns = new List<AIReflectionPatternSummary>();
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
        if (interval.FirstOwnTurn < 0) interval.FirstOwnTurn = Math.Max(1, state.LastDiaryOwnTurn < state.OwnTurns
            ? state.LastDiaryOwnTurn + 1 : state.OwnTurns - Math.Max(1, config.DiaryIntervalOwnTurns) + 1);
        var builder = new StringBuilder(2048);
        builder.AppendLine("Fantasy Kingdom AI日記").AppendLine("=============================");
        builder.Append("戦闘ID: ").AppendLine(state.BattleId);
        builder.Append("陣営: ").Append(state.Faction).Append("　脅威度: ").Append(state.ThreatLevel)
            .Append("　性格: ").AppendLine(state.Personality);
        builder.Append("ターン: ").Append(interval.FirstTurn < 0 ? state.LastStartedTurn : interval.FirstTurn)
            .Append(" ～ ").Append(state.LastStartedTurn).Append("　自軍行動ターン: ").AppendLine(state.OwnTurns.ToString());
        builder.Append("結果: ").AppendLine(result);
        builder.Append("自軍ターン: ").Append(interval.FirstOwnTurn).Append("～").AppendLine(state.OwnTurns.ToString());
        builder.Append("主要作戦: ").AppendLine(interval.After?.Strategy ?? state.LastSnapshot?.Strategy ?? "未記録");
        if (result != "INTERVAL")
            foreach (var usage in state.StrategyUse)
                builder.Append("作戦利用: ").Append(usage.Strategy).Append(" 脅威度帯 ").Append(usage.ThreatBand)
                    .Append(" / ").Append(usage.OwnTurns).AppendLine(" 自軍ターン");
        builder.Append("期間報酬: ").Append(interval.Reward.ToString("F2"))
            .Append("　戦闘累計報酬: ").AppendLine(state.BattleReward.ToString("F2"));
        builder.Append("内訳: 通常撃破 ").Append(interval.NormalKills).Append("　陣形撃破 ")
            .Append(interval.FormationKills).Append(" ×2　Artifact ").Append(interval.Artifacts)
            .Append("　経済安定・回復 ").Append(interval.StableTurns).Append("　反復・失敗減点 ")
            .Append(interval.PenaltyReward.ToString("F2")).Append("　勝敗報酬 ").AppendLine(interval.VictoryReward.ToString("F2"));
        builder.Append("行動数: ").Append(interval.Actions).Append("　戦略的進展: ").Append(interval.Successful)
            .Append("　失敗: ").Append(interval.Failed).Append("　無意味な反復: ").AppendLine(interval.Repeated.ToString());
        builder.Append("自軍損失: ").Append(interval.OwnLosses).Append("（累計 ").Append(state.TotalOwnLosses)
            .Append("）　敵撃破累計: ").Append(state.TotalKills).Append("　Artifact累計: ").AppendLine(state.TotalArtifacts.ToString());
        foreach (var failure in interval.Failures)
            builder.Append("失敗理由: ").Append(failure.Reason).Append(" ").AppendLine(failure.Count.ToString());
        AppendBreakdown(builder, interval.Breakdown);
        foreach (var success in interval.Successes)
            builder.Append("成功理由: ").Append(success.Reason).Append(" ").AppendLine(success.Count.ToString());
        AppendTraces(builder, interval.TraceExamples);
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
                    + "_Turn" + state.OwnTurns + ".txt";
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
    static void AppendBreakdown(StringBuilder builder, AIRewardBreakdown reward)
    {
        if (reward == null) return;
        builder.Append("報酬内訳: 戦闘 ").Append(reward.Combat.ToString("F2")).Append("　Artifact ").Append(reward.Artifact.ToString("F2"))
            .Append("　生存 ").Append(reward.Survival.ToString("F2")).Append("　位置 ").Append(reward.Position.ToString("F2"))
            .Append("　局所戦力 ").Append(reward.LocalPower.ToString("F2")).Append("　目標 ").Append(reward.Objective.ToString("F2"))
            .Append("　経済 ").Append(reward.Economy.ToString("F2")).Append("　情報 ").Append(reward.Information.ToString("F2"))
            .Append("　防衛 ").Append(reward.Defense.ToString("F2")).Append("　効率 ").Append(reward.Efficiency.ToString("F2"))
            .Append("　準備 ").Append(reward.Preparation.ToString("F2")).Append("　後続成果 ").AppendLine(reward.DelayedReward.ToString("F2"));
        builder.Append("減点内訳: 反復 ").Append(reward.RepeatPenalty.ToString("F2")).Append("　失敗 ").Append(reward.FailurePenalty.ToString("F2"))
            .Append("　軍の未活用 ").Append(reward.IdleArmyPenalty.ToString("F2")).Append("　過剰生産 ").Append(reward.OverproductionPenalty.ToString("F2"))
            .Append("　機会損失 ").AppendLine(reward.MissedOpportunityPenalty.ToString("F2"));
    }
    static void AppendTraces(StringBuilder builder, List<AIReflectionDecisionTrace> traces)
    {
        if (traces == null) return;
        foreach (var trace in traces)
        {
            if (trace == null) continue;
            builder.Append("主要判断: Turn ").Append(trace.Turn).Append(' ').Append(trace.ActorRole).Append(' ').AppendLine(trace.ActionType.ToString());
            builder.Append("  優先度 ").Append(trace.StrategicPriority < 0 ? "未記録" : "P" + trace.StrategicPriority)
                .Append("　通常評価 ").Append(trace.BaseScore.ToString("F2")).Append("　経験補正 ").Append(trace.LearnedModifier.ToString("F2"))
                .Append("　最終評価点 ").AppendLine(trace.FinalScore.ToString("F2"));
            builder.Append("  作戦: ").Append(trace.Strategy).Append("　目標: ").AppendLine(trace.Objective);
            builder.Append("  意図: ").AppendLine(trace.Intent);
            if (trace.DecisionFacts != null) foreach (var fact in trace.DecisionFacts) builder.Append("  選択時の観測: ").AppendLine(fact);
            builder.Append("  期待: ").AppendLine(trace.ExpectedOutcome);
            builder.Append("  結果: ").AppendLine(trace.ActualOutcome);
            builder.Append("  学習と次回の評価変更: ").AppendLine(trace.Lesson);
        }
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
        foreach (var reason in summary.Successes)
            builder.Append("  累計成功理由: ").Append(reason.Reason).Append(" ").AppendLine(reason.Count.ToString());
        AppendBreakdown(builder, summary.Breakdown);
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

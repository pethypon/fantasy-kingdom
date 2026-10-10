using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>Human-readable explanations projected only from a selected action and its observed record.</summary>
[Serializable]
public sealed class AIReflectionDecisionTrace
{
    public long ActionId;
    public int Turn, OriginalActionTurn, StrategicPriority = -1;
    public AIActionType ActionType;
    public string ActorRole, Strategy, Objective, Intent, ExpectedOutcome, ActualOutcome, Lesson;
    public float BaseScore, LearnedModifier, FinalScore, ImmediateReward, DelayedReward, FinalReward, LearningValueChange;
    public bool IsRewardCorrection;
    public List<string> DecisionFacts = new List<string>();

    public static AIReflectionDecisionTrace Build(AIAction action, AIActionRecord record)
    {
        if (record == null) return null;
        var trace = new AIReflectionDecisionTrace
        {
            ActionId = record.ActionId, Turn = record.Turn, OriginalActionTurn = record.Turn,
            ActionType = action != null ? action.ActionType : record.ActionType,
            StrategicPriority = action != null ? action.StrategicPriority : -1,
            ActorRole = record.Before?.ActorRole ?? record.ActorRole ?? "未記録",
            Strategy = record.Before?.Strategy ?? "未記録"
        };
        trace.Objective = record.Before?.HasObjective == true
            ? "既知の目標までの距離 " + record.Before.DistanceToObjective : "目標の記録なし";
        Templates(trace.ActionType, trace.ActorRole, out trace.Intent, out trace.ExpectedOutcome);
        trace.Refresh(record);
        return trace;
    }

    public static void UpdateOutcome(AIActionRecord record)
    {
        if (record == null) return;
        if (record.DecisionTrace == null) record.DecisionTrace = Build(null, record);
        else record.DecisionTrace.Refresh(record);
    }

    public static AIReflectionDecisionTrace BuildCorrection(AIActionRecord record, float rewardDelta, float learnedChange,
        int occurrenceTurn, AIRewardBreakdown breakdownDelta = null)
    {
        if (record == null) return null;
        var trace = (record.DecisionTrace ?? Build(null, record)).Copy();
        trace.Turn = occurrenceTurn; trace.OriginalActionTurn = record.Turn; trace.IsRewardCorrection = true;
        trace.ImmediateReward = 0; trace.DelayedReward = Safe(breakdownDelta?.DelayedReward ?? 0); trace.FinalReward = Safe(rewardDelta);
        trace.LearningValueChange = Safe(learnedChange);
        trace.ActualOutcome = "Turn " + occurrenceTurn + " に確定した評価補正: Turn " + record.Turn
            + " の行動への報酬補正 " + Signed(rewardDelta) + "。新しい行動としては数えない。";
        if (breakdownDelta != null)
            trace.ActualOutcome += " 内訳: 戦闘 " + Signed(breakdownDelta.Combat) + "、経済 " + Signed(breakdownDelta.Economy)
                + "、後続成果 " + Signed(breakdownDelta.DelayedReward) + "。";
        trace.Lesson = LessonText(trace.ActionType, learnedChange, rewardDelta);
        return trace;
    }

    public AIReflectionDecisionTrace Copy()
    {
        var result = (AIReflectionDecisionTrace)MemberwiseClone();
        result.DecisionFacts = DecisionFacts != null ? new List<string>(DecisionFacts) : new List<string>();
        return result;
    }

    void Refresh(AIActionRecord record)
    {
        BaseScore = Safe(record.BaseScore); LearnedModifier = Safe(record.LearnedModifier); FinalScore = Safe(record.FinalScore);
        ImmediateReward = Safe(record.ImmediateReward); DelayedReward = Safe(record.RewardBreakdown?.DelayedReward ?? 0);
        FinalReward = Safe(record.Reward); LearningValueChange = Safe(record.LearningValueChange);
        DecisionFacts = Facts(record.Before, LearnedModifier);
        ActualOutcome = OutcomeText(record);
        Lesson = record.Outcome == null ? "学習結果は未記録" : LessonText(ActionType, LearningValueChange, FinalReward);
    }

    static List<string> Facts(AIActionContextSnapshot before, float modifier)
    {
        var facts = new List<string>(12);
        if (before == null) { facts.Add("事前観測の記録なし"); return facts; }
        bool actorKnown = !string.IsNullOrEmpty(before.ActorRole) && before.ActorRole != "Builder" && before.ActorRole != "None";
        if (actorKnown)
        {
            facts.Add("局所戦力比 " + Number(before.LocalPowerRatio));
            facts.Add("自軍HP比率 " + Number(before.ActorHpRatio * 100) + "%");
            facts.Add("自軍の予想被害 " + Number(before.IncomingDamage));
            facts.Add("自軍の高さ " + before.ActorHeight);
        }
        facts.Add("残AP " + Number(before.ActorAp));
        facts.Add("Crystal危険 " + before.OwnCrystalThreatened);
        facts.Add("経済状態 " + before.EconomyState);
        facts.Add("視認した敵 " + before.VisibleEnemyCount);
        if (!string.IsNullOrEmpty(before.MatchPhase)) facts.Add("戦闘段階 " + before.MatchPhase);
        if (before.HasObjective) facts.Add("既知目標までの距離 " + before.DistanceToObjective);
        facts.Add("対象を観測済み " + before.TargetObserved);
        if (before.TargetObserved) facts.Add("観測した対象分類 " + before.TargetCategory);
        facts.Add("経験による評価補正 " + Signed(modifier));
        return facts;
    }

    static string OutcomeText(AIActionRecord record)
    {
        if (record.Outcome == null) return "実行結果は未記録";
        var outcome = record.Outcome;
        var text = new StringBuilder(256);
        text.Append("実行 ").Append(record.ExecutionSucceeded ? "成功" : "失敗")
            .Append("、敵撃破 ").Append(outcome.EnemyKills).Append("（陣形 ").Append(outcome.FormationKills)
            .Append("）、自軍損失 ").Append(outcome.OwnLosses).Append("、与ダメージ ").Append(outcome.DamageDealt)
            .Append("、新規視界 ").Append(outcome.NewTilesRevealed).Append("、Artifact ").Append(outcome.ArtifactsAcquired);
        if (outcome.EffectChanged) text.Append("、状態効果の変化を確認");
        if (record.Before != null && record.After != null)
            text.Append("、局所戦力比 ").Append(Number(record.Before.LocalPowerRatio)).Append(" → ").Append(Number(record.After.LocalPowerRatio))
                .Append("、自軍の予想被害 ").Append(Number(record.Before.IncomingDamage)).Append(" → ").Append(Number(record.After.IncomingDamage));
        text.Append("。即時評価 ").Append(Signed(record.ImmediateReward))
            .Append("、後続成果 ").Append(Signed(record.RewardBreakdown?.DelayedReward ?? 0))
            .Append("、最終評価 ").Append(Signed(record.Reward));
        return text.ToString();
    }

    static string LessonText(AIActionType action, float change, float reward)
    {
        change = Safe(change);
        if (Math.Abs(change) < .00001f) return "この記録による学習値の変更なし（評価 " + Signed(reward) + "）。";
        return "この状況での " + action + " の学習値を " + Signed(change)
            + (change > 0 ? " 上げた" : " 下げた") + "（実際の評価 " + Signed(reward) + "）。";
    }

    static void Templates(AIActionType action, string role, out string intent, out string expected)
    {
        switch (action)
        {
            case AIActionType.Attack:
                intent = "観測した敵の戦力を削減する"; expected = "観測した対象へダメージを与え、局所戦力差を改善することを期待する"; break;
            case AIActionType.Retreat:
                intent = "損失を避けて自軍の戦力を温存する"; expected = "予想被害を低下させ、生存できる位置へ移ることを期待する"; break;
            case AIActionType.DefenseRepos:
                intent = "Crystalの防衛位置を改善する"; expected = "観測した脅威へ対応し、防衛の危険を軽減することを期待する"; break;
            case AIActionType.Support:
                intent = "味方の行動を支援できる位置を確保する"; expected = "後続の味方行動に利用できる支援位置を確保することを期待する"; break;
            case AIActionType.Surround:
                intent = "観測した対象の周囲に陣形を準備する"; expected = "味方との陣形を整え、後続攻撃を支援することを期待する"; break;
            case AIActionType.Build:
            case AIActionType.Upgrade:
            case AIActionType.SubCrystal:
                intent = "経済または軍事の基盤を改善する"; expected = "建設または改良によって必要な生産や軍事機能を確保することを期待する"; break;
            case AIActionType.Summon:
                intent = "必要な自軍戦力を補強する"; expected = "新しい味方を確保し、作戦に利用することを期待する"; break;
            case AIActionType.SkillUse:
                intent = "観測した対象へスキルの効果を適用する"; expected = "記録したスキルによる攻撃・回復・状態変化を期待する"; break;
            case AIActionType.Rotate:
                intent = "駒の向きを変更する"; expected = "向きに依存する行動や視界を利用する準備を期待する"; break;
            case AIActionType.Wait:
                intent = "現在の位置とAPを維持する"; expected = "待機による防衛や温存の価値を、その後の観測結果で確認する"; break;
            default:
                bool scout = role == "Scout";
                intent = scout ? "未知領域と敵の情報を取得する" : "作戦に利用する位置へ移動する";
                expected = scout ? "未観測領域を観測し、既知の目標への進展を確認することを期待する"
                    : "予想被害と既知目標までの距離を踏まえ、位置の改善を確認することを期待する"; break;
        }
    }
    static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
    static string Number(float value) => Safe(value).ToString("F2", CultureInfo.InvariantCulture);
    static string Signed(float value) => Safe(value).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
}

using System;
using System.Globalization;
using System.Text;

/// <summary>Structured summaries from stored facts. The resulting prose is never a learning input.</summary>
public static class AIOperationDiary
{
    public static string Render(AIOperationBattleState state, AIOperationConfig config = null)
    {
        var builder = new StringBuilder(); Append(builder, state, config); return builder.ToString();
    }

    public static void Append(StringBuilder builder, AIOperationBattleState state, AIOperationConfig config = null)
    {
        if (builder == null || state == null) return;
        config = config != null ? config : AIOperationConfig.Active;
        if (config == null || !config.Enabled) return;
        builder.AppendLine().AppendLine("＝＝＝＝ 複数ターン作戦 ＝＝＝＝");
        if ((state.CompletedOperations == null || state.CompletedOperations.Count == 0)
            && (state.ActiveOperations == null || state.ActiveOperations.Count == 0))
        { builder.AppendLine("まだ作戦の記録はありません。"); return; }
        if (state.CompletedOperations != null)
        {
            int first = Math.Max(0, state.CompletedOperations.Count - config.CompletedLimit);
            for (int i = first; i < state.CompletedOperations.Count; i++)
                WritePlan(builder, state.CompletedOperations[i], config, false);
        }
        if (state.ActiveOperations != null)
        {
            int count = Math.Min(config.ConcurrentLimit, state.ActiveOperations.Count);
            for (int i = 0; i < count; i++) WritePlan(builder, state.ActiveOperations[i], config, true);
        }
    }

    static void WritePlan(StringBuilder builder, AIOperationPlan plan, AIOperationConfig config, bool active)
    {
        if (plan == null) return;
        var before = plan.StartContext;
        var after = plan.EndContext ?? plan.CurrentContext;
        builder.AppendLine().Append("【作戦 / Goal】#").Append(plan.OperationId).Append(' ')
            .AppendLine(GoalLabel(plan.PrimaryGoal));
        builder.Append("状態: ").Append(StatusLabel(plan.Status));
        if (plan.IsPrimary) builder.Append("（主作戦）");
        builder.AppendLine();
        builder.AppendLine("【作戦目的】");
        builder.AppendLine(string.IsNullOrWhiteSpace(plan.StrategicReason)
            ? GoalLabel(plan.PrimaryGoal) + "を、観測できる情報と実際のゲーム状態から判断する。" : plan.StrategicReason);
        builder.AppendLine("【開始時状況】");
        if (before == null) builder.AppendLine("開始時の観測記録なし。");
        else
        {
            builder.Append("自軍戦力 ").Append(Number(before.OwnMilitaryPower)).Append(" / 観測敵戦力 ")
                .Append(before.EnemyStrengthKnown ? Number(before.EnemyObservedPower) : "不明").AppendLine();
            builder.Append("経済 ").Append(EconomyLabel(before.EconomyState)).Append(" / 観測敵 ")
                .Append(before.VisibleEnemyUnits).Append("体 / 目標情報 ")
                .AppendLine(before.TargetObserved ? "現在視認" : before.ObjectiveKnown ? "既知・現在未視認" : "未確認");
        }
        builder.AppendLine("【計画 / Plan】");
        if (plan.Steps == null || plan.Steps.Count == 0) builder.AppendLine("手順の記録なし。");
        else
            for (int i = 0; i < Math.Min(plan.Steps.Count, config.StepLimit); i++)
            {
                var step = plan.Steps[i]; if (step == null) continue;
                builder.Append(i + 1).Append(". ").Append(StepLabel(step.Type));
                if (!string.IsNullOrWhiteSpace(step.Purpose)) builder.Append("：").Append(step.Purpose);
                builder.AppendLine(step.Completed ? "（完了）" : step.Failed ? "（未達）" : "（進行待ち）");
            }
        builder.AppendLine("【期待 / Expected】");
        builder.Append(Math.Max(1, plan.ExpectedEndTurn - plan.StartTurn + 1)).Append("自軍ターン以内に")
            .Append(GoalLabel(plan.PrimaryGoal)).Append("。AP予算 ").Append(Number(plan.ExpectedApBudget)).AppendLine("。");
        if (plan.PrimaryGoal == AIOperationGoal.ScoutRegion || plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce)
            builder.Append("探索目標 ").Append(plan.TargetNewTiles).AppendLine("マス。正式に観測した情報で達成を判定。");
        builder.AppendLine("【実際 / Actual】");
        int endTurn = plan.ActualEndTurn > 0 ? plan.ActualEndTurn : after?.Turn ?? plan.StartTurn;
        builder.Append(Math.Max(1, endTurn - plan.StartTurn + 1)).Append("自軍ターン / 主目標 ")
            .AppendLine(plan.PrimaryGoalAchieved ? "達成" : "未達成");
        if (after != null)
        {
            builder.Append("自軍戦力 ").Append(Number(before?.OwnMilitaryPower ?? 0)).Append(" → ")
                .Append(Number(after.OwnMilitaryPower)).Append(" / 撃破 ")
                .Append(Math.Max(0, after.EnemyKills - (before?.EnemyKills ?? 0)))
                .Append("体 / 自軍損失 ").Append(Math.Max(0, after.OwnLosses - (before?.OwnLosses ?? 0))).AppendLine("体");
            builder.Append("新規探索 ").Append(Math.Max(0, after.ExploredTiles - (before?.ExploredTiles ?? 0)))
                .Append("マス / 新規敵発見 ").Append(Math.Max(0, after.NewEnemyContacts - (before?.NewEnemyContacts ?? 0)))
                .Append(" / 割当軍の活用率 ").Append(Number(after.ArmyUtilization * 100)).AppendLine("%");
        }
        if (plan.StrategicAbort)
            builder.Append("合理的な中止: ").AppendLine(AbortLabel(plan.AbortReason));
        WriteRevisions(builder, plan, config);
        builder.AppendLine("【採点 / Score】");
        var score = active ? plan.ProvisionalScore : plan.Score;
        if (score == null) builder.AppendLine("採点待ち。");
        else
        {
            var weights = config.GetGoalWeights(plan.PrimaryGoal).Copy(); weights.Normalize();
            Dimension(builder, "主目標 / GoalAchievement", score.GoalAchievement, weights.GoalAchievement);
            Dimension(builder, "軍事成果 / MilitaryOutcome", score.MilitaryOutcome, weights.MilitaryOutcome);
            Dimension(builder, "生存 / Survival", score.Survival, weights.Survival);
            Dimension(builder, "効率 / Efficiency", score.Efficiency, weights.Efficiency);
            Dimension(builder, "位置 / Position", score.Position, weights.Position);
            Dimension(builder, "領土 / Territory", score.Territory, weights.Territory);
            Dimension(builder, "経済 / Economy", score.Economy, weights.Economy);
            Dimension(builder, "情報 / Information", score.Information, weights.Information);
            Dimension(builder, "防衛 / Defense", score.Defense, weights.Defense);
            Dimension(builder, "時間 / TimeEfficiency", score.TimeEfficiency, weights.TimeEfficiency);
            Dimension(builder, "軍活用 / ArmyUtilization", score.ArmyUtilization, weights.ArmyUtilization);
            Dimension(builder, "準備 / Preparation", score.PreparationQuality, weights.Preparation);
            Dimension(builder, "優位活用 / Exploitation", score.Exploitation, weights.Exploitation);
            Dimension(builder, "危険管理 / RiskControl", score.RiskControl, weights.RiskControl);
            builder.Append("減点 / Penalty: ").AppendLine(Number(score.Penalty));
            builder.Append(active ? "暫定評価: " : "最終評価: ").Append(Number(score.FinalScore)).Append(" / ")
                .Append(Number(config.PerfectScore)).Append(" — ")
                .AppendLine(new AIOperationScorer(config).GetRank(score.FinalScore).ToString());
        }
        if (active)
        {
            int completedSteps = 0;
            if (plan.Steps != null) foreach (var step in plan.Steps) if (step != null && step.Completed) completedSteps++;
            builder.Append("進行: ").Append(completedSteps).Append(" / ").Append(plan.Steps?.Count ?? 0).AppendLine("手順");
            builder.AppendLine("注意: 進行中の暫定評価です。完了時の採点と経験学習にはまだ使用していません。");
        }
        builder.AppendLine("【成功理由 / Success】");
        if (plan.SuccessReasons == null || plan.SuccessReasons.Count == 0) builder.AppendLine("確定した成功理由なし。");
        else foreach (var reason in plan.SuccessReasons) if (reason != AIOperationSuccessReason.None) builder.AppendLine(SuccessLabel(reason));
        builder.AppendLine("【失敗理由 / Failure】");
        if (plan.FailureReasons == null || plan.FailureReasons.Count == 0) builder.AppendLine("確定した失敗理由なし。");
        else foreach (var reason in plan.FailureReasons) if (reason != AIOperationFailureReason.None) builder.AppendLine(FailureLabel(reason));
        builder.AppendLine("【学習 / Lesson】");
        if (active) builder.AppendLine("作戦の完了前なので、作戦経験への学習は保留。");
        else
        {
            builder.Append("一般化状況: ").AppendLine(plan.ContextKey ?? "記録なし");
            builder.Append("作戦型: ").AppendLine(plan.PatternKey ?? "記録なし");
            builder.Append("学習用報酬 ").Append(Number(plan.LearningReward)).Append(" / 経験値 ")
                .Append(Number(plan.LearnedValueBefore)).Append(" → ").Append(Number(plan.LearnedValueAfter)).AppendLine();
            builder.AppendLine("この結果は類似状況の作戦選択を小さく補正する。行動の成果は別に評価し、日記文章は学習に使用しない。");
        }
    }

    static void WriteRevisions(StringBuilder builder, AIOperationPlan plan, AIOperationConfig config)
    {
        if (plan.Revisions == null || plan.Revisions.Count == 0) return;
        builder.AppendLine("【計画変更】");
        for (int i = 0; i < Math.Min(plan.Revisions.Count, config.RevisionLimit); i++)
        {
            var revision = plan.Revisions[i];
            if (revision == null) continue;
            builder.Append("自軍ターン ").Append(revision.Turn).Append("：").Append(GoalLabel(revision.OldGoal))
                .Append(" → ").Append(GoalLabel(revision.NewGoal)).Append(" / ").AppendLine(revision.Reason ?? "変更理由の記録なし");
        }
    }
    static void Dimension(StringBuilder builder, string title, float value, float weight)
    { builder.Append(title).Append(": ").Append(Number(value)).Append('/').AppendLine(Number(weight)); }
    static string Number(float value) => AIOperationLearningProfile.Finite(value).ToString("0.##", CultureInfo.InvariantCulture);

    public static string GoalLabel(AIOperationGoal goal)
    {
        switch (goal)
        {
            case AIOperationGoal.DestroyEnemyCrystal: return "敵メインクリスタルの破壊";
            case AIOperationGoal.DestroySubCrystal: return "敵サブクリスタルの破壊";
            case AIOperationGoal.DefendOwnCrystal: return "自軍メインクリスタルの防衛";
            case AIOperationGoal.DefendSubCrystal: return "自軍サブクリスタルの防衛";
            case AIOperationGoal.EliminateEnemyForce: return "観測した敵戦力の撃滅";
            case AIOperationGoal.WeakenEnemyForce: return "観測した敵戦力の削減";
            case AIOperationGoal.CaptureTerritory: return "領土の獲得";
            case AIOperationGoal.HoldTerritory: return "領土の維持";
            case AIOperationGoal.ObtainArtifact: return "Artifactの獲得";
            case AIOperationGoal.ClearDungeon: return "ダンジョンの攻略";
            case AIOperationGoal.BreakEnemyDefense: return "敵守備の突破";
            case AIOperationGoal.FlankEnemyForce: return "敵側面への迂回";
            case AIOperationGoal.SurroundEnemyForce: return "敵戦力の包囲";
            case AIOperationGoal.ScoutRegion: return "地域の探索";
            case AIOperationGoal.LocateEnemyMainForce: return "敵主力の発見";
            case AIOperationGoal.EconomicRecovery: return "経済の立て直し";
            case AIOperationGoal.SecureResourceArea: return "資源生産基盤の確保";
            case AIOperationGoal.DelayEnemy: return "敵進軍の遅延";
            case AIOperationGoal.ForceEnemyRetreat: return "敵の撤退誘導";
            case AIOperationGoal.CreateDiversion: return "陽動による敵守備の誘導";
            case AIOperationGoal.ExploitWeakFront: return "手薄な戦線の活用";
            default: return "作戦未設定";
        }
    }
    public static string StepLabel(AIOperationStepType type)
    {
        switch (type)
        {
            case AIOperationStepType.Scout: return "偵察";
            case AIOperationStepType.Approach: return "接近";
            case AIOperationStepType.Diversion: return "陽動";
            case AIOperationStepType.Attack: return "攻撃";
            case AIOperationStepType.Defense: return "防衛";
            case AIOperationStepType.Flank: return "側面への迂回";
            case AIOperationStepType.Surround: return "包囲";
            case AIOperationStepType.Support: return "支援";
            case AIOperationStepType.Retreat: return "撤退";
            case AIOperationStepType.Regroup: return "再編成";
            case AIOperationStepType.Hold: return "維持";
            case AIOperationStepType.DestroyObjective: return "目標破壊";
            case AIOperationStepType.AcquireArtifact: return "Artifact獲得";
            case AIOperationStepType.Exploit: return "優位の活用";
            default: return type.ToString();
        }
    }
    static string EconomyLabel(EconomicState state)
    {
        switch (state)
        { case EconomicState.Healthy: return "安定"; case EconomicState.Warning: return "不足の兆候";
            case EconomicState.Crisis: return "不足"; case EconomicState.Collapse: return "破綻"; default: return state.ToString(); }
    }
    static string StatusLabel(AIOperationStatus status)
    {
        switch (status)
        {
            case AIOperationStatus.Proposed: return "候補"; case AIOperationStatus.Preparing: return "準備中";
            case AIOperationStatus.Active: return "実行中"; case AIOperationStatus.Suspended: return "一時停止";
            case AIOperationStatus.Replanning: return "再計画中"; case AIOperationStatus.Aborted: return "中止";
            case AIOperationStatus.Success: return "成功"; case AIOperationStatus.PartialSuccess: return "部分成功";
            case AIOperationStatus.Failure: return "未達成"; default: return status.ToString();
        }
    }
    static string AbortLabel(AIOperationAbortReason reason)
    {
        switch (reason)
        {
            case AIOperationAbortReason.StrategicPriorityChanged: return "より優先する戦略へ変更";
            case AIOperationAbortReason.ObjectiveDestroyedByOther: return "別の行動により目標が消滅";
            case AIOperationAbortReason.ObjectiveUnavailable: return "目標へ到達できない";
            case AIOperationAbortReason.InsufficientForce: return "戦力不足";
            case AIOperationAbortReason.EconomicEmergency: return "経済への緊急対応";
            case AIOperationAbortReason.CrystalEmergency: return "クリスタルの緊急防衛";
            case AIOperationAbortReason.IntelligenceInvalidated: return "観測情報の変化";
            case AIOperationAbortReason.ExcessiveLoss: return "損失過多";
            case AIOperationAbortReason.Timeout: return "作戦期間超過";
            default: return reason.ToString();
        }
    }
    static string SuccessLabel(AIOperationSuccessReason reason)
    {
        switch (reason)
        {
            case AIOperationSuccessReason.PrimaryGoalCompleted: return "主目標を達成";
            case AIOperationSuccessReason.FastCompletion: return "予定より早く完了";
            case AIOperationSuccessReason.LowLosses: return "損失を抑えた";
            case AIOperationSuccessReason.GoodTrade: return "有利な戦力交換";
            case AIOperationSuccessReason.SuccessfulScout: return "偵察で新情報を獲得";
            case AIOperationSuccessReason.SuccessfulDiversion: return "観測した敵の陽動に成功";
            case AIOperationSuccessReason.SuccessfulFlank: return "側面への迂回に成功";
            case AIOperationSuccessReason.SuccessfulSurround: return "包囲に成功";
            case AIOperationSuccessReason.GoodArmyUtilization: return "割当軍を活用";
            case AIOperationSuccessReason.GoodRiskControl: return "危険を抑えた";
            case AIOperationSuccessReason.EconomicEfficiency: return "経済を効率よく改善";
            case AIOperationSuccessReason.StrongPreparation: return "準備が後の成果に寄与";
            case AIOperationSuccessReason.SuccessfulExploitation: return "獲得した優位を活用";
            default: return reason.ToString();
        }
    }
    static string FailureLabel(AIOperationFailureReason reason)
    {
        switch (reason)
        {
            case AIOperationFailureReason.PrimaryGoalFailed: return "主目標が未達成";
            case AIOperationFailureReason.PoorPreparation: return "準備不足";
            case AIOperationFailureReason.BadIntel: return "情報不足";
            case AIOperationFailureReason.InsufficientForce: return "戦力不足";
            case AIOperationFailureReason.ExcessiveLoss: return "損失過多";
            case AIOperationFailureReason.SlowExecution: return "作戦の進行が遅い";
            case AIOperationFailureReason.FailedDiversion: return "陽動の成果を確認できず";
            case AIOperationFailureReason.FailedFlank: return "迂回が未達成";
            case AIOperationFailureReason.FailedSurround: return "包囲が未達成";
            case AIOperationFailureReason.EconomicOverstretch: return "経済への負担が過大";
            case AIOperationFailureReason.ResourceWaste: return "成果に対して資源消費が過大";
            case AIOperationFailureReason.ArmyUnderutilization: return "割当軍の活用が不足";
            case AIOperationFailureReason.ObjectiveAbandoned: return "目標から離れた";
            case AIOperationFailureReason.MissedExploitation: return "優位を活用できず";
            case AIOperationFailureReason.CrystalExposed: return "クリスタルの危険が増加";
            case AIOperationFailureReason.Overcommitment: return "損失リスクのある作戦へ固執";
            case AIOperationFailureReason.RepeatedFailedPattern: return "失敗した作戦型を繰り返した";
            default: return reason.ToString();
        }
    }
}

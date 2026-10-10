using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Goal-aware, value-only operation evaluation. It never sums Reward or queries the live/hidden board.</summary>
public sealed class AIOperationScorer
{
    readonly AIOperationConfig config;
    readonly AIOperationFailureAnalyzer failureAnalyzer;
    readonly AIOperationSuccessAnalyzer successAnalyzer;
    public AIOperationScorer(AIOperationConfig config)
    {
        this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
        failureAnalyzer = new AIOperationFailureAnalyzer(config);
        successAnalyzer = new AIOperationSuccessAnalyzer(config);
    }

    public AIOperationEvaluationResult Evaluate(AIOperationPlan operation, AIOperationContext start,
        AIOperationContext end, IReadOnlyList<AIActionRecord> actions, bool provisional = false)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        start = start ?? operation.StartContext ?? new AIOperationContext();
        end = end ?? operation.CurrentContext ?? start;
        var facts = GatherFacts(operation, start, end, actions);
        bool achieved;
        float goal = EvaluateGoal(operation, start, end, facts, out achieved);
        var weights = config.GetGoalWeights(operation.PrimaryGoal);
        float scale = weights.Total > 0f ? 100f / weights.Total : 0f;
        float loss = LossRatio(start, end);
        float survival = Unit(1f - loss);
        float military = MilitaryQuality(start, end, facts, loss);
        float time = TimeQuality(operation, end, achieved);
        float defense = DefenseQuality(start, end);
        float information = InformationQuality(operation, start, end, facts);
        float utilization = Utilization(operation, end);
        float preparation = Unit(Mathf.Max(end.PreparationContribution, facts.PreparationQuality));
        float exploitation = Unit(end.ExploitationProgress);
        float risk = RiskQuality(start, end, loss);
        float economy = EconomyQuality(start, end);
        float efficiency = EfficiencyQuality(operation, start, end, facts, goal, loss);
        bool exposed = CrystalExposed(start, end, config);
        bool collapse = end.EconomyState == EconomicState.Collapse && start.EconomyState != EconomicState.Collapse;
        bool catastrophic = loss >= Unit(config.CatastrophicLossRatio) && loss > 0f;
        bool strategicAbort = operation.StrategicAbort && operation.Status == AIOperationStatus.Aborted
            && IsStrategicAbortReason(operation.AbortReason);
        var score = new AIOperationScoreBreakdown
        {
            GoalAchievement = Points(goal, weights.GoalAchievement, scale), MilitaryOutcome = Points(military, weights.MilitaryOutcome, scale),
            Survival = Points(survival, weights.Survival, scale), Efficiency = Points(efficiency, weights.Efficiency, scale),
            Position = Points(PositionQuality(start, end, achieved), weights.Position, scale),
            Territory = Points(TerritoryQuality(operation, start, end), weights.Territory, scale),
            Economy = Points(economy, weights.Economy, scale), Information = Points(information, weights.Information, scale),
            Defense = Points(defense, weights.Defense, scale), TimeEfficiency = Points(time, weights.TimeEfficiency, scale),
            ArmyUtilization = Points(utilization, weights.ArmyUtilization, scale), PreparationQuality = Points(preparation, weights.Preparation, scale),
            Exploitation = Points(exploitation, weights.Exploitation, scale), RiskControl = Points(risk, weights.RiskControl, scale)
        };
        float penalty = 0f;
        if (exposed) penalty += NonNegative(config.CrystalExposurePenalty);
        if (collapse) penalty += NonNegative(config.EconomicCollapsePenalty);
        if (catastrophic) penalty += NonNegative(config.CatastrophicLossPenalty);
        if (!strategicAbort && !achieved && end.Turn - operation.LastProgressTurn >= Math.Max(1, config.ReplanAfterStalledTurns)) penalty += NonNegative(config.StallPenalty);
        if (end.AssignedUnitCount > 0 && utilization < Unit(config.UnderutilizationThreshold)) penalty += NonNegative(config.ArmyIdlePenalty);
        if (facts.BadIntel) penalty += NonNegative(config.BadIntelPenalty);
        if (facts.ResourceWaste) penalty += NonNegative(config.ResourceWastePenalty);
        if (facts.Overcommitted) penalty += NonNegative(config.OvercommitmentPenalty);
        score.Penalty = -Mathf.Min(penalty, NonNegative(config.MaximumPenalty));
        float final = Mathf.Clamp(score.Total, 0f, Mathf.Clamp(Finite(config.PerfectScore, 100f), 1f, 100f));
        bool perfect = achieved && goal >= 1f && survival >= Unit(config.PerfectMinimumSurvival)
            && !exposed && end.EconomyState != EconomicState.Collapse && !catastrophic && !end.OwnCrystalDestroyed;
        if (!achieved) final = Mathf.Min(final, Mathf.Clamp(Finite(config.MaxScoreWithoutPrimaryGoal, 69f), 0f, 69f));
        if (!perfect) final = Mathf.Min(final, Mathf.Clamp(Finite(config.MaxScoreWithoutPerfectGate, 94f), 0f, 94f));
        // A rational recall protects survival and is not learned as an attack failure. Actual catastrophic outcomes remain failures.
        if (strategicAbort && !catastrophic && !collapse && !end.OwnCrystalDestroyed)
            final = Mathf.Clamp(Finite(config.StrategicAbortScore, 45f), 40f, 54f);
        if (end.OwnCrystalDestroyed) final = Mathf.Min(final, Mathf.Clamp(Finite(config.MaxScoreDestroyedOwnCrystal), 0f, 19f));
        if (provisional) final = Mathf.Min(final, Mathf.Clamp(Finite(config.MaxProvisionalScore, 94f), 0f, 99f));
        score.FinalScore = final;
        var result = new AIOperationEvaluationResult
        {
            PrimaryGoalAchieved = achieved, GoalProgress = goal, CanReachPerfect = perfect && !provisional,
            IsProvisional = provisional, IsStrategicAbort = strategicAbort, Breakdown = score,
            FinalScore = provisional ? 0f : final, ProvisionalScore = provisional ? final : 0f,
            Rank = GetRank(final), LearningReward = provisional ? 0f : ToLearningReward(final, achieved,
                strategicAbort && !catastrophic && !collapse && !end.OwnCrystalDestroyed)
        };
        result.FailureReasons = failureAnalyzer.Analyze(operation, start, end, score, achieved, provisional, facts.BadIntel, facts.ResourceWaste, facts.Overcommitted);
        result.SuccessReasons = successAnalyzer.Analyze(operation, start, end, score, achieved, provisional, facts.NewTiles, facts.NewContacts, facts.Kills, facts.PreparationQuality);
        return result;
    }

    public AIOperationRank GetRank(float score)
    {
        score = Mathf.Clamp(Finite(score), 0f, 100f);
        if (score >= Threshold(config.PerfectRankThreshold, 95f)) return AIOperationRank.Perfect;
        if (score >= Threshold(config.ExcellentRankThreshold, 85f)) return AIOperationRank.Excellent;
        if (score >= Threshold(config.SuccessRankThreshold, 70f)) return AIOperationRank.Success;
        if (score >= Threshold(config.PartialRankThreshold, 55f)) return AIOperationRank.PartialSuccess;
        if (score >= Threshold(config.NeutralRankThreshold, 40f)) return AIOperationRank.Neutral;
        if (score >= Threshold(config.FailureRankThreshold, 20f)) return AIOperationRank.Failure;
        return AIOperationRank.CriticalFailure;
    }

    public float ToLearningReward(float score, bool primaryGoalAchieved = true, bool strategicAbort = false)
    {
        float value;
        if (strategicAbort) value = Mathf.Clamp(Finite(config.StrategicAbortLearningReward), -.5f, .5f);
        else switch (GetRank(score))
        {
            case AIOperationRank.Perfect: value = config.PerfectLearningReward; break;
            case AIOperationRank.Excellent: value = config.ExcellentLearningReward; break;
            case AIOperationRank.Success: value = config.SuccessLearningReward; break;
            case AIOperationRank.PartialSuccess: value = config.PartialLearningReward; break;
            case AIOperationRank.Neutral: value = config.NeutralLearningReward; break;
            case AIOperationRank.Failure: value = config.FailureLearningReward; break;
            default: value = config.CriticalFailureLearningReward; break;
        }
        value = Mathf.Clamp(Finite(value), Mathf.Clamp(Finite(config.OperationLearningMin, -3f), -3f, 0f),
            Mathf.Clamp(Finite(config.OperationLearningMax, 3f), 0f, 3f));
        if (!primaryGoalAchieved) value = Mathf.Min(value, Mathf.Clamp(Finite(config.MaxLearningRewardWithoutPrimaryGoal, .5f), 0f, .5f));
        return value;
    }

    float EvaluateGoal(AIOperationPlan plan, AIOperationContext before, AIOperationContext after, Facts facts, out bool achieved)
    {
        achieved = false;
        if (plan.AbortReason == AIOperationAbortReason.ObjectiveDestroyedByOther) return 0f;
        float partial = 0f;
        switch (plan.PrimaryGoal)
        {
            case AIOperationGoal.DestroyEnemyCrystal:
            case AIOperationGoal.DestroySubCrystal:
                achieved = after.ConfirmedTargetDestroyed && SameTarget(plan, before, after);
                if (before.ObjectiveHpKnown && after.ObjectiveHpKnown && SameTarget(plan, before, after)
                    && before.ObjectiveHpRatio > 0f)
                    partial = Unit((before.ObjectiveHpRatio - after.ObjectiveHpRatio) / before.ObjectiveHpRatio);
                break;
            case AIOperationGoal.DefendOwnCrystal:
                achieved = before.OwnCrystalThreatened && after.EnemyThreatResolved && !after.OwnCrystalDestroyed
                    && after.OwnCrystalHpRatio >= Unit(config.CrystalDefenseMinimumHpRatio)
                    && after.OwnCrystalHpRatio + Unit(config.CrystalDamageTolerance) >= before.OwnCrystalHpRatio;
                partial = before.OwnCrystalThreatened && !after.OwnCrystalDestroyed ? Unit(after.OwnCrystalHpRatio) : 0f;
                break;
            case AIOperationGoal.DefendSubCrystal:
                achieved = before.ObjectiveThreatened && after.EnemyThreatResolved && !after.ConfirmedTargetDestroyed
                    && before.ObjectiveHpKnown && after.ObjectiveHpKnown && SameTarget(plan, before, after)
                    && after.ObjectiveHpRatio >= Unit(config.CrystalDefenseMinimumHpRatio)
                    && after.ObjectiveHpRatio + Unit(config.CrystalDamageTolerance) >= before.ObjectiveHpRatio;
                partial = before.ObjectiveThreatened && after.ObjectiveHpKnown ? Unit(after.ObjectiveHpRatio) : 0f;
                break;
            case AIOperationGoal.EliminateEnemyForce:
                achieved = after.TargetForceConfirmedEliminated;
                partial = before.EnemyStrengthKnown ? Unit(facts.EnemyLossPower / Math.Max(1f, before.EnemyObservedPower)) : 0f;
                break;
            case AIOperationGoal.WeakenEnemyForce:
                partial = before.EnemyStrengthKnown ? Unit(facts.EnemyLossPower / Math.Max(1f, before.EnemyObservedPower)
                    / Math.Max(.01f, Finite(plan.TargetPowerReduction, config.DefaultTargetPowerReduction))) : 0f;
                achieved = partial >= 1f && facts.Kills > 0;
                break;
            case AIOperationGoal.CaptureTerritory:
                partial = Unit((after.TerritoryCount - before.TerritoryCount) / (float)Math.Max(1, plan.TargetTerritoryGain));
                achieved = partial >= 1f && !after.OwnCrystalThreatened && !after.OwnCrystalDestroyed;
                break;
            case AIOperationGoal.HoldTerritory:
                achieved = before.TerritoryCount > 0 && after.TerritoryCount >= before.TerritoryCount
                    && after.Turn >= plan.ExpectedEndTurn && !after.OwnCrystalDestroyed;
                partial = before.TerritoryCount > 0 ? Unit(after.TerritoryCount / (float)before.TerritoryCount)
                    * Unit((after.Turn - plan.StartTurn + 1f) / Math.Max(1f, plan.ExpectedEndTurn - plan.StartTurn + 1f)) : 0f;
                break;
            case AIOperationGoal.ObtainArtifact:
                achieved = facts.Artifacts > 0; break;
            case AIOperationGoal.ClearDungeon:
                achieved = after.ConfirmedDungeonCleared; break;
            case AIOperationGoal.ScoutRegion:
                partial = Unit(facts.NewTiles / (float)Math.Max(1, plan.TargetNewTiles));
                achieved = partial >= 1f; break;
            case AIOperationGoal.LocateEnemyMainForce:
                achieved = facts.NewContacts > 0;
                partial = Unit(facts.NewTiles / (float)Math.Max(1, plan.TargetNewTiles)); break;
            case AIOperationGoal.EconomicRecovery:
                achieved = before.EconomyState != EconomicState.Healthy && after.EconomyState == EconomicState.Healthy;
                partial = Unit(((int)before.EconomyState - (int)after.EconomyState) / (float)Math.Max(1, (int)before.EconomyState));
                break;
            case AIOperationGoal.SecureResourceArea:
                achieved = after.ConfirmedResourceAreaSecured && after.EconomyProductionValue > before.EconomyProductionValue;
                partial = Unit((after.TerritoryCount - before.TerritoryCount) / (float)Math.Max(1, plan.TargetTerritoryGain)); break;
            case AIOperationGoal.BreakEnemyDefense:
                achieved = after.TargetForceConfirmedEliminated || after.RouteOpened && after.ConfirmedTargetDestroyed && SameTarget(plan, before, after);
                partial = Unit(after.ObjectiveProgress); break;
            case AIOperationGoal.FlankEnemyForce:
                achieved = after.FlankConfirmed; partial = Unit(after.ObjectiveProgress); break;
            case AIOperationGoal.SurroundEnemyForce:
                achieved = after.SurroundConfirmed; partial = Unit(after.ObjectiveProgress); break;
            case AIOperationGoal.CreateDiversion:
                achieved = after.DiversionConfirmed && after.RouteOpened;
                partial = after.DiversionConfirmed ? .5f : 0f; break;
            case AIOperationGoal.DelayEnemy:
                achieved = after.EnemyDelayConfirmed; break;
            case AIOperationGoal.ForceEnemyRetreat:
                achieved = after.EnemyRetreatConfirmed; break;
            case AIOperationGoal.ExploitWeakFront:
                partial = Unit(after.ExploitationProgress);
                achieved = partial >= Unit(config.SuccessfulExploitationThreshold)
                    && (facts.Kills > 0 || after.TerritoryCount > before.TerritoryCount || after.ConfirmedTargetDestroyed);
                break;
        }
        return achieved ? 1f : Mathf.Min(Unit(partial), Unit(config.PartialGoalProgressCap));
    }

    struct Facts
    {
        public int Kills, Artifacts, NewTiles, NewContacts;
        public float EnemyLossPower, PreparationQuality, ApSpent;
        public bool BadIntel, ResourceWaste, Overcommitted;
    }
    Facts GatherFacts(AIOperationPlan plan, AIOperationContext start, AIOperationContext end, IReadOnlyList<AIActionRecord> actions)
    {
        var facts = new Facts
        {
            Kills = Math.Max(0, end.EnemyKills - start.EnemyKills),
            Artifacts = Math.Max(0, end.ConfirmedArtifactAcquisitions - start.ConfirmedArtifactAcquisitions),
            NewTiles = Math.Max(0, end.ExploredTiles - start.ExploredTiles),
            NewContacts = Math.Max(0, end.NewEnemyContacts - start.NewEnemyContacts),
            EnemyLossPower = NonNegative(end.EnemyKilledPower - start.EnemyKilledPower),
            ApSpent = NonNegative(end.ApSpent - start.ApSpent)
        };
        int kills = 0, artifacts = 0, tiles = 0, preparation = 0, preparationUseful = 0;
        float ap = 0f;
        HashSet<string> contacts = null;
        if (actions != null) for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action == null || action.OperationId != plan.OperationId || action.ActionId <= plan.GoalStartedAfterActionId
                || !action.ExecutionSucceeded || action.Outcome == null) continue;
            kills = SaturatingAdd(kills, Math.Max(0, action.Outcome.EnemyKills));
            artifacts = SaturatingAdd(artifacts, Math.Max(0, action.Outcome.ArtifactsAcquired));
            tiles = SaturatingAdd(tiles, Math.Max(0, action.Outcome.NewTilesRevealed));
            ap += NonNegative(action.Before?.ActionApCost ?? 0);
            if (action.Outcome.NewEnemyLifeIds != null) foreach (string life in action.Outcome.NewEnemyLifeIds)
                if (!string.IsNullOrEmpty(life)) { if (contacts == null) contacts = new HashSet<string>(); contacts.Add(life); }
            if (action.PreparationCandidate)
            {
                preparation++;
                if (action.SuccessReasons != null && action.SuccessReasons.Contains(AISuccessReason.SuccessfulPreparation)) preparationUseful++;
            }
            facts.BadIntel |= action.FailureReason == AIFailureReason.InvalidTarget || action.FailureReason == AIFailureReason.TargetLost;
            facts.ResourceWaste |= action.FailureReason == AIFailureReason.ResourceWaste;
            facts.Overcommitted |= action.FailureReason == AIFailureReason.Overextended || action.FailureReason == AIFailureReason.UnsupportedAdvance;
        }
        facts.Kills = Math.Max(facts.Kills, kills); facts.Artifacts = Math.Max(facts.Artifacts, artifacts);
        facts.NewTiles = Math.Max(facts.NewTiles, tiles); facts.NewContacts = Math.Max(facts.NewContacts, contacts?.Count ?? 0);
        facts.ApSpent = Mathf.Max(facts.ApSpent, ap);
        if (facts.EnemyLossPower <= 0f && facts.Kills > 0) facts.EnemyLossPower = facts.Kills * NonNegative(config.EstimatedKillPower);
        facts.PreparationQuality = preparation > 0 ? preparationUseful / (float)preparation : 0f;
        return facts;
    }

    public static float LossRatio(AIOperationContext start, AIOperationContext end)
    {
        if (start == null || end == null) return 0f;
        float losses = NonNegative(end.OwnLossPower - start.OwnLossPower);
        int lostUnits = Math.Max(0, end.OwnLosses - start.OwnLosses);
        float fromPower = start.OwnMilitaryPower > 0f ? Unit(losses / start.OwnMilitaryPower) : 0f;
        float fromUnits = start.OwnUnits > 0 ? Unit(lostUnits / (float)start.OwnUnits) : 0f;
        return Mathf.Max(fromPower, fromUnits);
    }
    public static bool CrystalExposed(AIOperationContext start, AIOperationContext end, AIOperationConfig config)
        => start != null && end != null && (end.OwnCrystalDestroyed || end.OwnCrystalThreatened && !start.OwnCrystalThreatened
            || Finite(end.OwnCrystalHpRatio) + Unit(config.CrystalDamageTolerance) < Finite(start.OwnCrystalHpRatio));
    static bool SameTarget(AIOperationPlan plan, AIOperationContext start, AIOperationContext end)
    {
        string target = !string.IsNullOrEmpty(plan.TargetLifeId) ? plan.TargetLifeId : start.ObjectiveLifeId;
        return !string.IsNullOrEmpty(target) && (string.IsNullOrEmpty(start.ObjectiveLifeId) || target == start.ObjectiveLifeId)
            && target == end.ObjectiveLifeId;
    }
    float MilitaryQuality(AIOperationContext start, AIOperationContext end, Facts facts, float loss)
    {
        if (facts.Kills <= 0 && loss <= 0f) return .6f;
        float baseline = start.EnemyStrengthKnown ? NonNegative(start.EnemyObservedPower) : NonNegative(start.OwnMilitaryPower);
        float removed = Unit(facts.EnemyLossPower / Math.Max(1f, baseline));
        float ownLoss = NonNegative(end.OwnLossPower - start.OwnLossPower);
        float trade = facts.EnemyLossPower > 0f ? Unit(facts.EnemyLossPower / Math.Max(1f, facts.EnemyLossPower + ownLoss)) : 0f;
        return Unit((removed + trade) * .5f);
    }
    float TimeQuality(AIOperationPlan plan, AIOperationContext end, bool achieved)
    {
        float expected = Math.Max(1f, plan.ExpectedEndTurn - plan.StartTurn + 1f);
        float spent = Math.Max(1f, (plan.ActualEndTurn > 0 ? plan.ActualEndTurn : end.Turn) - plan.StartTurn + 1f);
        if (achieved && spent <= expected) return 1f;
        return Unit(expected / spent);
    }
    float DefenseQuality(AIOperationContext start, AIOperationContext end)
    {
        if (end.OwnCrystalDestroyed) return 0f;
        float retained = start.OwnCrystalHpRatio > 0f ? Unit(end.OwnCrystalHpRatio / start.OwnCrystalHpRatio) : Unit(end.OwnCrystalHpRatio);
        if (end.OwnCrystalThreatened) retained *= .5f;
        return retained;
    }
    float InformationQuality(AIOperationPlan plan, AIOperationContext start, AIOperationContext end, Facts facts)
    {
        float tiles = Unit(facts.NewTiles / (float)Math.Max(1, plan.TargetNewTiles));
        float contacts = Unit(facts.NewContacts / (float)Math.Max(1, config.FullInformationEnemyContacts));
        float uncertainty = start.BeliefUncertainty > 0f && (facts.NewTiles > 0 || facts.NewContacts > 0)
            ? Unit((start.BeliefUncertainty - end.BeliefUncertainty) / start.BeliefUncertainty) : 0f;
        float important = end.KnownImportantFacilities > start.KnownImportantFacilities ? 1f : 0f;
        return Mathf.Max(tiles, Mathf.Max(contacts, Mathf.Max(uncertainty, important)));
    }
    static float Utilization(AIOperationPlan plan, AIOperationContext end)
    {
        if (end.AssignedUnitCount > 0) return Unit(end.UtilizedAssignedUnitCount / (float)end.AssignedUnitCount);
        if (plan.OriginalAssignedUnitLifeIds != null && plan.OriginalAssignedUnitLifeIds.Count > 0)
            return Unit((plan.UtilizedUnitLifeIds?.Count ?? 0) / (float)plan.OriginalAssignedUnitLifeIds.Count);
        return Unit(end.ArmyUtilization);
    }
    static float PositionQuality(AIOperationContext start, AIOperationContext end, bool achieved)
    {
        if (achieved) return 1f;
        if (start.ObjectiveDistance < 0 || end.ObjectiveDistance < 0) return .5f;
        return Unit(.5f + (start.ObjectiveDistance - end.ObjectiveDistance) / (float)Math.Max(1, start.ObjectiveDistance) * .5f);
    }
    static float TerritoryQuality(AIOperationPlan plan, AIOperationContext start, AIOperationContext end)
        => end.TerritoryCount < start.TerritoryCount ? Unit(end.TerritoryCount / (float)Math.Max(1, start.TerritoryCount)) * .5f
            : Unit(.5f + (end.TerritoryCount - start.TerritoryCount) / (float)Math.Max(1, plan.TargetTerritoryGain) * .5f);
    static float EconomyQuality(AIOperationContext start, AIOperationContext end)
    {
        float state = 1f - (int)end.EconomyState / 3f;
        float recovery = (int)end.EconomyState < (int)start.EconomyState ? 1f : .5f;
        float production = end.EconomyProductionValue > start.EconomyProductionValue ? 1f : .5f;
        return Unit(state * .5f + recovery * .25f + production * .25f);
    }
    float EfficiencyQuality(AIOperationPlan plan, AIOperationContext start, AIOperationContext end, Facts facts, float goal, float loss)
    {
        float budget = Math.Max(1f, Finite(plan.ExpectedApBudget, config.DefaultExpectedApBudget));
        float apEfficiency = facts.ApSpent <= budget ? 1f : Unit(budget / facts.ApSpent);
        float actualSpend = NonNegative(end.ResourceSpendValue - start.ResourceSpendValue);
        float resourceEfficiency = end.ResourceInvestment > 0f && actualSpend > end.ResourceInvestment
            ? Unit(end.ResourceInvestment / actualSpend) : 1f;
        return Unit((apEfficiency + resourceEfficiency + 1f - loss) / 3f * Mathf.Max(.5f, goal));
    }
    static float RiskQuality(AIOperationContext start, AIOperationContext end, float loss)
    {
        float forecast = Unit(start.ForecastRisk);
        float unanticipated = Mathf.Max(0f, loss - forecast);
        return Unit(1f - unanticipated - (end.OwnCrystalThreatened ? .25f : 0f));
    }
    public static bool IsStrategicAbortReason(AIOperationAbortReason reason)
        => reason == AIOperationAbortReason.StrategicPriorityChanged || reason == AIOperationAbortReason.ObjectiveDestroyedByOther
            || reason == AIOperationAbortReason.ObjectiveUnavailable || reason == AIOperationAbortReason.EconomicEmergency
            || reason == AIOperationAbortReason.CrystalEmergency || reason == AIOperationAbortReason.IntelligenceInvalidated;
    static int SaturatingAdd(int a, int b) => a > int.MaxValue - b ? int.MaxValue : a + b;
    static float Points(float value, float weight, float scale) => Unit(value) * NonNegative(weight) * scale;
    static float Threshold(float value, float fallback) => Mathf.Clamp(Finite(value, fallback), 0f, 100f);
    static float Finite(float value, float fallback = 0f) => AIOperationConfig.Finite(value, fallback);
    static float Unit(float value) => AIOperationConfig.Unit(value);
    static float NonNegative(float value) => AIOperationConfig.NonNegative(value);
}

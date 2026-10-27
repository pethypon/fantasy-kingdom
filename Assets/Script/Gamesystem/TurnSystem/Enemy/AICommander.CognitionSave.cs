using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AICommander
{
    AICognitiveBattleState restoredCognitiveState;
    string futureCognitiveJson;
    const int MaximumCognitiveJson = 128 * 1024;
    string CaptureCognitiveState()
    {
        if (futureCognitiveJson != null) return futureCognitiveJson;
        if (cognitivePlanner == null && restoredCognitiveState == null) return null;
        var state = restoredCognitiveState ?? new AICognitiveBattleState {
            Owner = _actorTeam, BattleId = cognitiveBattleId, NextEventId = cognitiveEventId,
            ActivePlan = cognitivePlanner?.Current, LastReplanReason = cognitivePlanner?.ReplanReason,
            Hypotheses = cognitiveHypotheses?.Snapshot(cognitiveBattleId, _actorTeam) };
        string json = JsonUtility.ToJson(state);
        return json.Length <= MaximumCognitiveJson ? json : null;
    }
    void RestoreCognitiveState(string json, string expectedBattle, int ownTurn)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            if (json.Length > MaximumCognitiveJson) { Debug.LogWarning("[R4保存] 上限を超える計画を破棄しました。"); return; }
            var saved = JsonUtility.FromJson<AICognitiveBattleState>(json);
            if (saved == null || saved.Owner != _actorTeam || saved.BattleId != expectedBattle) return;
            if (saved.Version > 1)
            {
                // Keep the exact payload, including unknown future fields. It must never be executed by this version.
                futureCognitiveJson = json; cognitiveUnavailable = true;
                Debug.LogWarning("[R4保存] 新しい版の計画を保持し、通常AIで続行します。"); return;
            }
            if (!ValidCognitiveState(saved, _actorTeam, expectedBattle, ownTurn))
            { Debug.LogWarning("[R4保存] 不正な計画を破棄し、現在の観測から再計画します。"); return; }
            restoredCognitiveState = saved; cognitiveBattleId = saved.BattleId; cognitiveEventId = saved.NextEventId;
        }
        catch (Exception error) { Debug.LogWarning("[R4保存] 計画を読み込めません: " + error.GetType().Name); }
    }
    void ApplyRestoredCognition()
    {
        if (restoredCognitiveState == null) return;
        cognitivePlanner.RestoreIntent(restoredCognitiveState.ActivePlan);
        cognitiveHypotheses.Restore(restoredCognitiveState.Hypotheses, cognitiveBattleId, _actorTeam, _turnCount);
        restoredCognitiveState = null;
    }
    public static bool ValidCognitiveState(AICognitiveBattleState saved, Team owner, string battleId, int ownTurn)
    {
        if (saved == null || saved.Version != 1 || saved.Owner != owner || saved.BattleId != battleId
            || !Bounded(saved.BattleId, 256) || saved.NextEventId < 0 || !Bounded(saved.LastReplanReason, 256, true)) return false;
        var plan = saved.ActivePlan;
        if (plan != null)
        {
            if (plan.OwnerTeam != owner || plan.BattleId != battleId || !Enum.IsDefined(typeof(AIOperationGoal), plan.Goal)
                || !Enum.IsDefined(typeof(AICognitivePlanStatus), plan.Status) || !Bounded(plan.PlanId, 128)
                || !Bounded(plan.Signature, 4096) || !Bounded(plan.ObjectiveRef, 256, true)
                || !Bounded(plan.TargetObservedId, 128, true) || !Bounded(plan.PrimaryMethod, 64)
                || plan.CreatedTurn < 0 || plan.CreatedTurn > ownTurn || plan.LastValidatedTurn < 0 || plan.LastValidatedTurn > ownTurn
                || plan.BoardGenerationAtValidation < 0 || plan.ParentOperationId < 0 || plan.StrategicPriorityBand < 0 || plan.StrategicPriorityBand > 4
                || plan.KnowledgeIds == null || plan.KnowledgeVersions == null || plan.KnowledgeIds.Count > 12
                || plan.KnowledgeIds.Count != plan.KnowledgeVersions.Count || !plan.HasValidDependencies()
                || !FiniteCognitivePlan(plan) || !Bounded(plan.AbandonCondition, 512, true)
                || !Bounded(plan.ReplanTrigger, 512, true) || !Bounded(plan.RejectionReason, 256, true)
                || !ValidCognitiveStrings(plan.OriginProblemIds, 16, 256) || !ValidCognitiveStrings(plan.HypothesisIds, 32, 256)
                || !ValidCognitiveStrings(plan.ExplanationEvidenceIds, 64, 256)
                || plan.EnemyResponses == null || plan.EnemyResponses.Count > 3) return false;
            var knowledge = new HashSet<string>();
            for (int i = 0; i < plan.KnowledgeIds.Count; i++)
                if (!AICausalExperienceMemory.ValidKnowledgeId(plan.KnowledgeIds[i]) || plan.KnowledgeVersions[i] != 1
                    || !knowledge.Add(plan.KnowledgeIds[i])) return false;
            foreach (var step in plan.Steps)
            {
                if (!Bounded(step.OperatorId, 64) || !AIStrategyOperatorCatalog.TryGet(step.OperatorId, out _) || !Bounded(step.ActorLifeId, 128, true)
                    || !Bounded(step.TargetObservedId, 128, true) || !Bounded(step.BoundActionKey, 2048, true)
                    || !Enum.IsDefined(typeof(AIPlanOperatorStatus), step.Status) || !Enum.IsDefined(typeof(AISquadMissionRole), step.Mission)
                    || step.ExpectedCost < 0 || step.ExpectedCost > APSystem.MaximumAP || step.Dependencies == null || step.Dependencies.Count > 5
                    || !ValidCognitiveStrings(step.RequiredEvidenceIds, 64, 256) || !Bounded(step.ExpectedEffect, 512, true)
                    || !Bounded(step.RevalidationRule, 512, true) || !Bounded(step.FailureFallback, 512, true)) return false;
            }
            foreach (var response in plan.EnemyResponses)
                if (response == null || !Bounded(response.ResponseClass, 64) || !Bounded(response.EvidenceId, 256, true)
                    || !Enum.IsDefined(typeof(AICognitiveResponseLegality), response.Legality)
                    || !AIWorldFact.Finite(response.LossLowerBound) || !AIWorldFact.Finite(response.LossUpperBound)
                    || response.LossLowerBound < 0 || response.LossUpperBound < response.LossLowerBound) return false;
        }
        if (saved.Hypotheses != null && !new AIHypothesisManager().Restore(saved.Hypotheses, battleId, owner, ownTurn)) return false;
        return true;
    }
    static bool Bounded(string value, int length, bool optional = false) => value == null ? optional : value.Length <= length && (optional || value.Length > 0);
    static bool ValidCognitiveStrings(List<string> values, int count, int length)
    { if (values == null || values.Count > count) return false; foreach (string value in values) if (!Bounded(value, length)) return false; return true; }
    static bool FiniteCognitivePlan(AICognitivePlan p)
    {
        float[] values = { p.GoalProgress, p.EffectiveDamage, p.EffectiveSupport, p.PredictedIncomingDamage,
            p.WorstCaseLoss, p.APCost, p.Delay, p.Confidence, p.Uncertainty, p.Utility, p.ExistingActionScore };
        foreach (float value in values) if (!AIWorldFact.Finite(value)) return false;
        return p.APCost >= 0 && p.APCost <= APSystem.MaximumAP && p.EffectiveDamage >= 0 && p.EffectiveSupport >= 0
            && p.PredictedIncomingDamage >= 0 && p.WorstCaseLoss >= 0 && p.Delay >= 0
            && p.Confidence >= 0 && p.Confidence <= 1 && p.Uncertainty >= 0 && p.Uncertainty <= 1;
    }
}

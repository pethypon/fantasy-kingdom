using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AICommander
{
    AICognitiveBattleState restoredCognitiveState;
    string futureCognitiveJson;
    const int MaximumCognitiveJson = 128 * 1024;
    void ResetCognitionForLoad()
    {
        RestoreCognitivePromotion();
        cognitiveWorldBuilder?.Clear(); cognitivePlanner?.Clear(); cognitiveProblems?.Clear(); cognitiveHypotheses?.Clear();
        cognitiveWorld = null; cognitivePrepared = false; cognitiveMatches.Clear(); cognitiveAssessments.Clear(); cognitiveActionKnowledge.Clear();
        cognitiveAttributions.Clear(); currentCognitiveProblems = Array.Empty<AIProblem>(); currentCognitiveHypotheses = Array.Empty<AIHypothesis>();
        cognitivePending = cognitiveReleaseRequested = cognitiveUnavailable = cognitiveWarning = false;
        pendingCognitivePlan = null; pendingCognitiveAction = null; cognitiveFallbackRecord = null;
        cognitiveBattleId = null; cognitiveEventId = 0; cognitiveObservedActionId = -1;
        restoredCognitiveState = null; futureCognitiveJson = null; cognitiveTraces?.Clear();
    }
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
    void RestoreCognitiveState(string json, string expectedBattle, int ownTurn, int globalTurn)
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
            if (!ValidCognitiveState(saved, _actorTeam, expectedBattle, ownTurn, globalTurn))
            { Debug.LogWarning("[R4保存] 不正な計画を破棄し、現在の観測から再計画します。"); return; }
            // Unity serializes null inline classes as empty objects. Discard only that exact empty shape,
            // after scope/version validation; never adopt an unscoped object containing actual data.
            if (EmptyInlineCognitivePlan(saved.ActivePlan)) saved.ActivePlan = null;
            if (EmptyInlineHypotheses(saved.Hypotheses)) saved.Hypotheses = null;
            restoredCognitiveState = saved; cognitiveBattleId = saved.BattleId; cognitiveEventId = saved.NextEventId;
        }
        catch (Exception error) { Debug.LogWarning("[R4保存] 計画を読み込めません: " + error.GetType().Name); }
    }
    void ApplyRestoredCognition()
    {
        if (restoredCognitiveState == null) return;
        cognitivePlanner.RestoreIntent(restoredCognitiveState.ActivePlan);
        cognitiveHypotheses.Restore(restoredCognitiveState.Hypotheses, cognitiveBattleId, _actorTeam, _turnCount, _board?.TurnCount ?? int.MaxValue);
        restoredCognitiveState = null;
    }
    public static bool ValidCognitiveState(AICognitiveBattleState saved, Team owner, string battleId, int ownTurn, int globalTurn = int.MaxValue)
    {
        if (saved == null || saved.Version != 1 || saved.Owner != owner || saved.BattleId != battleId
            || !Bounded(saved.BattleId, 256) || saved.NextEventId < 0 || !Bounded(saved.LastReplanReason, 256, true)) return false;
        var plan = saved.ActivePlan;
        if (plan != null && !EmptyInlineCognitivePlan(plan))
        {
            bool intent = plan.Status == AICognitivePlanStatus.Suspended && plan.Steps != null && plan.Steps.Count == 0
                && plan.LastValidatedTurn == -1 && plan.BoardGenerationAtValidation == -1;
            if (plan.OwnerTeam != owner || plan.BattleId != battleId || !Enum.IsDefined(typeof(AIOperationGoal), plan.Goal)
                || plan.Goal == AIOperationGoal.None || !Enum.IsDefined(typeof(AICognitivePlanStatus), plan.Status) || !Bounded(plan.PlanId, 128, intent)
                || !Bounded(plan.Signature, 4096, intent) || !Bounded(plan.ObjectiveRef, 256)
                || !Bounded(plan.TargetObservedId, 128, true) || !Bounded(plan.PrimaryMethod, 64, intent)
                || plan.CreatedTurn < 0 || plan.CreatedTurn > ownTurn || !intent && (plan.LastValidatedTurn < 0 || plan.LastValidatedTurn > ownTurn)
                || !intent && plan.BoardGenerationAtValidation < 0 || plan.ParentOperationId < 0 || plan.StrategicPriorityBand < 0 || plan.StrategicPriorityBand > 4
                || plan.KnowledgeIds == null || plan.KnowledgeVersions == null || plan.KnowledgeIds.Count > 12
                || plan.KnowledgeIds.Count != plan.KnowledgeVersions.Count || !intent && !plan.HasValidDependencies()
                || !FiniteCognitivePlan(plan) || !Bounded(plan.SquadSequenceSignature, 2048, true) || !Bounded(plan.AbandonCondition, 512, true)
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
        if (saved.Hypotheses != null && !EmptyInlineHypotheses(saved.Hypotheses)
            && !new AIHypothesisManager().Restore(saved.Hypotheses, battleId, owner, ownTurn, globalTurn)) return false;
        return true;
    }
    static bool EmptyInlineHypotheses(AIHypothesisBattleState value)
        => value != null && value.Version == AIHypothesisBattleState.CurrentVersion
            && string.IsNullOrEmpty(value.BattleId) && value.ActorTeam == default(Team)
            && value.LastOwnTurn == -1 && value.LastGlobalTurn == -1
            && EmptyCognitiveList(value.Entries) && EmptyCognitiveList(value.ProcessedEventIds);

    static bool EmptyInlineCognitivePlan(AICognitivePlan value)
        => value != null && string.IsNullOrEmpty(value.PlanId) && string.IsNullOrEmpty(value.Signature)
            && string.IsNullOrEmpty(value.ObjectiveRef) && string.IsNullOrEmpty(value.TargetObservedId)
            && string.IsNullOrEmpty(value.PrimaryMethod) && string.IsNullOrEmpty(value.BattleId)
            && value.OwnerTeam == default(Team) && value.ParentOperationId == 0 && value.Goal == AIOperationGoal.None
            && value.CreatedTurn == 0 && value.LastValidatedTurn == 0 && value.BoardGenerationAtValidation == 0
            && value.StrategicPriorityBand == 0 && value.Status == AICognitivePlanStatus.Proposed
            && EmptyCognitiveList(value.Steps) && EmptyCognitiveList(value.KnowledgeIds) && EmptyCognitiveList(value.KnowledgeVersions)
            && EmptyCognitiveList(value.OriginProblemIds) && EmptyCognitiveList(value.HypothesisIds)
            && EmptyCognitiveList(value.ExplanationEvidenceIds) && EmptyCognitiveList(value.EnemyResponses)
            && value.GoalProgress == 0 && value.EffectiveDamage == 0 && value.EffectiveSupport == 0
            && value.PredictedIncomingDamage == 0 && value.WorstCaseLoss == 0 && value.APCost == 0 && value.Delay == 0
            && value.Confidence == 0 && value.Uncertainty == 0 && value.Utility == 0 && value.ExistingActionScore == 0
            && !value.ProvenVictory && !value.PreparationProven && !value.HasUnknownContinuationCost
            && ZeroCognitivePosition(value.StartActorPosition) && ZeroCognitivePosition(value.ExpectedActorPosition)
            && value.StartTargetHP == -1 && value.StartTargetBarrier == -1 && value.StartOwnAP == 0 && value.StartOwnUnitCount == 0
            && string.IsNullOrEmpty(value.ActualObservationReason) && value.LookaheadCompletedDepth == 0
            && value.LookaheadDelta == 0 && value.LookaheadUtilityAdjustment == 0 && string.IsNullOrEmpty(value.LookaheadSourceKind)
            && string.IsNullOrEmpty(value.SquadSequenceSignature) && string.IsNullOrEmpty(value.AbandonCondition)
            && string.IsNullOrEmpty(value.ReplanTrigger) && string.IsNullOrEmpty(value.RejectionReason);

    static bool EmptyCognitiveList<T>(List<T> values) => values != null && values.Count == 0;
    static bool ZeroCognitivePosition(Vector3 value) => value.x == 0 && value.y == 0 && value.z == 0;
    static bool Bounded(string value, int length, bool optional = false) => value == null ? optional : value.Length <= length && (optional || value.Length > 0);
    static bool ValidCognitiveStrings(List<string> values, int count, int length)
    { if (values == null || values.Count > count) return false; foreach (string value in values) if (!Bounded(value, length)) return false; return true; }
    static bool FiniteCognitivePlan(AICognitivePlan p)
    {
        float[] values = { p.GoalProgress, p.EffectiveDamage, p.EffectiveSupport, p.PredictedIncomingDamage,
            p.WorstCaseLoss, p.APCost, p.Delay, p.Confidence, p.Uncertainty, p.Utility, p.ExistingActionScore,
            p.LookaheadDelta, p.LookaheadUtilityAdjustment };
        foreach (float value in values) if (!AIWorldFact.Finite(value)) return false;
        return p.APCost >= 0 && p.APCost <= APSystem.MaximumAP && p.EffectiveDamage >= 0 && p.EffectiveSupport >= 0
            && p.PredictedIncomingDamage >= 0 && p.WorstCaseLoss >= 0 && p.Delay >= 0
            && p.Confidence >= 0 && p.Confidence <= 1 && p.Uncertainty >= 0 && p.Uncertainty <= 1
            && p.LookaheadCompletedDepth >= 0 && p.LookaheadCompletedDepth <= 20
            && Mathf.Abs(p.LookaheadUtilityAdjustment) <= .15f
            && (p.LookaheadCompletedDepth == 0 ? string.IsNullOrEmpty(p.LookaheadSourceKind) : p.LookaheadSourceKind == "minimax_completed")
            && FiniteCognitivePosition(p.StartActorPosition) && FiniteCognitivePosition(p.ExpectedActorPosition);
    }
    static bool FiniteCognitivePosition(Vector3 value)
        => AIWorldFact.Finite(value.x) && AIWorldFact.Finite(value.y) && AIWorldFact.Finite(value.z)
            && value.x >= 0 && value.x < 40 && value.z >= 0 && value.z < 40 && Mathf.Abs(value.y) <= 32768;
}

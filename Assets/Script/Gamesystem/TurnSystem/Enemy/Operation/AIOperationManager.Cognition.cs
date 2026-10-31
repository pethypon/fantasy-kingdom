using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>R4 proposals enter the existing 16-slot operation selection; they cannot execute an action or replace a primary.</summary>
public sealed partial class AIOperationManager
{
    const int CognitiveProposalLimit = 8;
    readonly List<AICognitiveOperationProposal> cognitiveProposals = new List<AICognitiveOperationProposal>(CognitiveProposalLimit);
    readonly Dictionary<string, long> cognitiveOperationIds = new Dictionary<string, long>(StringComparer.Ordinal);
    readonly List<string> cognitiveMappingOrder = new List<string>(64);
    string cognitiveBattle;
    int cognitiveProposalGeneration = -1;

    sealed class AICognitiveOperationProposal
    {
        public string PlanId, TargetId, Method;
        public AIOperationGoal Goal;
        public List<AIOperationStep> Steps;
    }

    public bool TryMapCognitivePlan(string planId, out long operationId)
    { if (planId != null) return cognitiveOperationIds.TryGetValue(planId, out operationId); operationId = 0; return false; }

    public void SubmitCognitivePlans(IReadOnlyList<AICognitivePlan> plans, AIBoardState board)
    {
        cognitiveProposals.Clear(); cognitiveProposalGeneration = -1;
        if (board == null || state.Ended || !config.Enabled || !AICognitiveAIConfig.Active.MayInfluenceActions ||
            board.Governor?.Mode == StrategicMode.EmergencyDefense || board.Governor?.Mode == StrategicMode.EconomicRecovery ||
            plans == null || state.Faction != board.ActorTeam) return;
        if (cognitiveBattle != state.BattleId)
        { cognitiveBattle = state.BattleId; cognitiveOperationIds.Clear(); cognitiveMappingOrder.Clear(); }
        for (int i = 0; i < plans.Count && cognitiveProposals.Count < CognitiveProposalLimit; i++)
        {
            var plan = plans[i];
            if (plan == null || plan.OwnerTeam != board.ActorTeam || plan.BattleId != state.BattleId ||
                plan.BoardGenerationAtValidation != board.Generation || plan.Goal == AIOperationGoal.None ||
                plan.Status != AICognitivePlanStatus.Active && plan.Status != AICognitivePlanStatus.Validated ||
                !plan.HasValidDependencies() || string.IsNullOrEmpty(plan.NextActionKey) ||
                string.IsNullOrEmpty(plan.PlanId) || plan.PlanId.Length > 64 || plan.StrategicPriorityBand < 2) continue;
            var steps = TranslateCognitiveSteps(plan.Steps, Mathf.Max(1, state.LastStartedOwnTurn));
            if (steps == null || steps.Count == 0) continue;
            cognitiveProposals.Add(new AICognitiveOperationProposal { PlanId = plan.PlanId, Goal = plan.Goal,
                TargetId = plan.TargetObservedId ?? string.Empty, Method = plan.PrimaryMethod, Steps = steps });
            LinkExistingCognitiveOperation(plan);
        }
        if (cognitiveProposals.Count == 0) return;
        cognitiveProposalGeneration = board.Generation;
        // Re-enter the existing current-turn selection once. Its priority/promote/suspend/primary contracts remain authoritative.
        refreshedGeneration = -1;
        Refresh(board, strategy, legacyPlan);
        foreach (var plan in plans)
            if (plan != null && TryFindEquivalentOperation(plan.Goal, plan.TargetObservedId, out long id))
            { RememberCognitiveMapping(plan.PlanId, id); plan.ParentOperationId = id; }
    }

    void MergeCognitiveCandidates(AIBoardState board, int turn)
    {
        if (board == null || cognitiveProposalGeneration != board.Generation || !AICognitiveAIConfig.Active.MayInfluenceActions ||
            board.Governor?.Mode == StrategicMode.EmergencyDefense || board.Governor?.Mode == StrategicMode.EconomicRecovery) return;
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in cognitiveProposals)
        {
            if (TryFindEquivalentOperation(proposal.Goal, proposal.TargetId, out long id))
            { RememberCognitiveMapping(proposal.PlanId, id); continue; }
            string signature = ((int)proposal.Goal) + "|" + proposal.TargetId + "|" + proposal.Method;
            if (!methods.Add(signature)) continue;
            AIOperationCandidate source = null;
            foreach (var candidate in candidates)
                if (candidate.Plan.PrimaryGoal == proposal.Goal && (candidate.Plan.TargetLifeId ?? string.Empty) == proposal.TargetId)
                { source = candidate; break; }
            // Knowledge cannot manufacture an unsupported objective, target or new score. It refines an existing feasible proposal.
            if (source == null || source.Priority <= 2) continue;
            if (!string.IsNullOrEmpty(proposal.TargetId) && !features.TryObserved(proposal.TargetId, out _) &&
                !features.TryOwn(proposal.TargetId, out _) && !source.Plan.HasTargetPosition) continue;

            string pattern = Pattern(proposal.Steps);
            bool duplicate = false;
            foreach (var candidate in candidates)
                if (candidate.Plan.PrimaryGoal == proposal.Goal && (candidate.Plan.TargetLifeId ?? string.Empty) == proposal.TargetId &&
                    candidate.Plan.PatternKey == pattern) { duplicate = true; break; }
            if (duplicate) continue;
            if (candidates.Count >= 16 && !RemoveDuplicateOrdinaryCandidate()) continue;

            Status observedTarget = null;
            if (!string.IsNullOrEmpty(proposal.TargetId)) features.TryObserved(proposal.TargetId, out observedTarget);
            var added = AddCandidate(board, turn, source.Plan.PrimaryGoal, source.Priority, source.BaseScore,
                observedTarget, personality, playerModel);
            if (added == null) continue;
            CopyCognitiveTarget(source.Plan, added.Plan);
            added.Plan.Steps = AIOperationRevision.CopySteps(proposal.Steps); added.Plan.PatternKey = pattern;
            added.Plan.StrategicReason = source.Plan.StrategicReason + " / R4:" + proposal.PlanId;
            RefreshCandidateContext(added, board, turn);
        }
    }

    bool RemoveDuplicateOrdinaryCandidate()
    {
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var candidate = candidates[i]; if (candidate.Priority <= 2) continue;
            for (int j = 0; j < i; j++)
                if (candidates[j].Plan.PrimaryGoal == candidate.Plan.PrimaryGoal &&
                    candidates[j].Plan.TargetLifeId == candidate.Plan.TargetLifeId &&
                    candidates[j].Plan.PatternKey == candidate.Plan.PatternKey)
                { candidates.RemoveAt(i); return true; }
        }
        return false;
    }

    bool TryFindEquivalentOperation(AIOperationGoal goal, string targetId, out long id)
    {
        foreach (var operation in state.ActiveOperations)
            if (Running(operation) && operation.PrimaryGoal == goal && (operation.TargetLifeId ?? string.Empty) == (targetId ?? string.Empty))
            { id = operation.OperationId; return true; }
        id = 0; return false;
    }
    void LinkExistingCognitiveOperation(AICognitivePlan plan)
    {
        if (TryFindEquivalentOperation(plan.Goal, plan.TargetObservedId, out long id))
        { RememberCognitiveMapping(plan.PlanId, id); plan.ParentOperationId = id; }
    }
    void RememberCognitiveMapping(string planId, long id)
    {
        if (string.IsNullOrEmpty(planId) || id <= 0) return;
        if (!cognitiveOperationIds.ContainsKey(planId))
        {
            if (cognitiveMappingOrder.Count >= 64)
            { cognitiveOperationIds.Remove(cognitiveMappingOrder[0]); cognitiveMappingOrder.RemoveAt(0); }
            cognitiveMappingOrder.Add(planId);
        }
        cognitiveOperationIds[planId] = id;
    }

    static List<AIOperationStep> TranslateCognitiveSteps(IReadOnlyList<AIPlanOperator> operators, int turn)
    {
        if (operators == null || operators.Count == 0 || operators.Count > 6) return null;
        var result = new List<AIOperationStep>(operators.Count);
        foreach (var step in operators)
        {
            if (step == null || !TryTranslateCognitiveOperator(step.OperatorId, out var type)) return null;
            result.Add(new AIOperationStep { StepId = step.StepId, Type = type, Purpose = step.OperatorId,
                Required = true, StartTurn = turn, Completed = false, Failed = false });
        }
        return result;
    }

    public static bool TryTranslateCognitiveOperator(string id, out AIOperationStepType type)
    {
        switch (id)
        {
            case "SCOUT_FOR_ANSWER": type = AIOperationStepType.Scout; return true;
            case "IMPROVE_POSITION": case "REPOSITION": case "OPEN_LINE": type = AIOperationStepType.Approach; return true;
            case "ASSEMBLE": case "CONCENTRATE": case "SYNCHRONIZE": type = AIOperationStepType.Regroup; return true;
            case "ATTACK": case "FOCUS_FIRE": case "DEBUFF_THEN_ATTACK": case "BUDGET_COUNTER": case "TRADE_EVALUATE": type = AIOperationStepType.Attack; return true;
            case "FORM_SUPPORT": type = AIOperationStepType.Support; return true;
            case "FORM_DEFENSE": case "DEFEND": type = AIOperationStepType.Defense; return true;
            case "FLANK": type = AIOperationStepType.Flank; return true;
            case "SURROUND": type = AIOperationStepType.Surround; return true;
            case "DIVERSION": type = AIOperationStepType.Diversion; return true;
            case "DISENGAGE": case "ROTATE_FRONTLINE": type = AIOperationStepType.Retreat; return true;
            case "HOLD": type = AIOperationStepType.Hold; return true;
            case "FINISH_SEQUENCE": type = AIOperationStepType.DestroyObjective; return true;
            // Existing operation DTO has no production/forecast step: keep that demand with the Governor, never fake an Attack.
            default: type = default; return false;
        }
    }

    static void CopyCognitiveTarget(AIOperationPlan source, AIOperationPlan target)
    {
        target.TargetLifeId = source.TargetLifeId; target.TargetCategory = source.TargetCategory;
        target.HasTargetPosition = source.HasTargetPosition; target.TargetX = source.TargetX; target.TargetZ = source.TargetZ;
        target.IsBaseAssault = source.IsBaseAssault; target.BaseAssaultPattern = source.BaseAssaultPattern;
        target.BaseTargetType = source.BaseTargetType; target.RequiredAssaultForce = source.RequiredAssaultForce;
        target.BaseIntelConfidence = source.BaseIntelConfidence; target.EstimatedBaseDefense = source.EstimatedBaseDefense;
        target.BaseDefenseKnown = source.BaseDefenseKnown; target.BaseRouteConfirmed = source.BaseRouteConfirmed;
        target.HasBaseApproachPoint = source.HasBaseApproachPoint; target.BaseApproachX = source.BaseApproachX; target.BaseApproachZ = source.BaseApproachZ;
        target.RallyX = source.RallyX; target.RallyZ = source.RallyZ;
    }
}

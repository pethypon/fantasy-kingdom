using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded HTN/GOAP proposal layer. It binds only a freshly generated formal first action;
/// subsequent dependency nodes await the actual result and are never sent to the executor.
/// </summary>
public sealed partial class AICognitivePlanner
{
    const int MaximumFormalCandidates = 64;
    readonly List<AICognitivePlan> candidates = new List<AICognitivePlan>(8);
    readonly List<AICognitivePlan> beam = new List<AICognitivePlan>(6);
    readonly HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, AICognitivePlan> bestByMethod = new Dictionary<string, AICognitivePlan>(StringComparer.Ordinal);
    readonly AICognitiveReplanner replanner = new AICognitiveReplanner();
    const int MaximumRejectionRecords = 32;
    readonly List<string> rejectionReasons = new List<string>(MaximumRejectionRecords);
    string battleId;
    Team team;
    bool initialized;
    int validatedGeneration = -1;
    public AICognitivePlan Current { get; private set; }
    public IReadOnlyList<AICognitivePlan> Candidates => candidates.AsReadOnly();
    public string ReplanReason { get; private set; } = "not_started";
    public bool CandidateScanTruncated { get; private set; }
    /// <summary>Stable value-only diagnostics for the current planning generation. Total can exceed the bounded retained records.</summary>
    public IReadOnlyList<string> RejectedReasons => rejectionReasons.AsReadOnly();
    public int RejectedCount => rejectionReasons.Count;
    public int RejectedTotalCount { get; private set; }
    public bool DiagnosticsEnabled { get; set; } = true;

    /// <summary>Source-fixture convenience only. Runtime uses RefreshSteps under the existing AISlicedWork budget.</summary>
    public void Refresh(AIObservedWorldModel world, IReadOnlyList<AIAction> formalActions,
        IReadOnlyList<AIKnowledgeMatch> matches, AICognitivePlannerLimits limits,
        bool emergency = false, bool economyRecovery = false,
        Func<AIAction, AICognitiveActionAssessment> assessment = null, Func<AICognitivePlan, float> calibration = null,
        Func<AIAction, IReadOnlyList<AIKnowledgeMatch>> actionKnowledge = null)
    {
        var work = RefreshSteps(world, formalActions, matches, limits, emergency, economyRecovery, assessment, calibration, actionKnowledge);
        try { while (work.MoveNext()) { } }
        finally { (work as IDisposable)?.Dispose(); }
    }

    public IEnumerator RefreshSteps(AIObservedWorldModel world, IReadOnlyList<AIAction> formalActions,
        IReadOnlyList<AIKnowledgeMatch> matches, AICognitivePlannerLimits limits,
        bool emergency = false, bool economyRecovery = false,
        Func<AIAction, AICognitiveActionAssessment> assessment = null, Func<AICognitivePlan, float> calibration = null,
        Func<AIAction, IReadOnlyList<AIKnowledgeMatch>> actionKnowledge = null)
    {
        candidates.Clear(); beam.Clear(); signatures.Clear(); bestByMethod.Clear(); rejectionReasons.Clear(); RejectedTotalCount = 0;
        CandidateScanTruncated = false; validatedGeneration = -1;
        bool finalized = false;
        try
        {
            if (world == null || formalActions == null || limits.MaxPlansTotal <= 0)
            { Current = null; ReplanReason = "missing_world_or_candidates"; RejectCandidate(ReplanReason); finalized = true; yield break; }
            if (!initialized || battleId != world.BattleId || team != world.ActorTeam)
            { replanner.Reset(); Current = null; battleId = world.BattleId; team = world.ActorTeam; initialized = true; }
            if (emergency || economyRecovery)
            {
                if (Current != null) Current.Status = AICognitivePlanStatus.Suspended;
                ReplanReason = emergency ? "protected_emergency_defense" : "protected_economic_recovery";
                RejectCandidate(ReplanReason); finalized = true; yield break;
            }
            if (!AIObjectiveReasoner.TryChoose(world, Current, false, false, out var objective))
            { Current = null; ReplanReason = "no_observed_or_remembered_objective"; RejectCandidate(ReplanReason); finalized = true; yield break; }
            if (AITurnBudget.Expired) { Suspend("budget_exhausted"); RejectCandidate("budget_exhausted"); finalized = true; yield break; }

            ulong observedFingerprint = AICognitiveReplanner.Fingerprint(world);
            int count = Math.Min(MaximumFormalCandidates, formalActions.Count);
            CandidateScanTruncated = formalActions.Count > count;
            if (CandidateScanTruncated) RejectCandidate("candidate_scan_truncated");
            for (int index = 0; index < count; index++)
            {
                // One formal-risk/Sim assessment is the largest indivisible unit. Checkpoint every candidate.
                yield return null;
                if (AITurnBudget.Expired) { RejectCandidate("budget_exhausted"); break; }
                var action = formalActions[index];
                if (!BasicLegal(action, world)) { RejectCandidate(BasicRejectionReason(action, world)); continue; }
                AICognitiveActionAssessment assessed = assessment != null ? assessment(action) : ConservativeAssessment(action, world);
                if (!assessed.Legal) { RejectCandidate("illegal_action", action); continue; }
                if (!assessed.HardSafe) { RejectCandidate("dangerous_immediate_loss", action); continue; }
                if (!assessed.IsFinite) { RejectCandidate("non_finite_prediction", action); continue; }
                string method = MethodFor(action, world, objective, assessed);
                if (method == null) { RejectCandidate("unsupported_operator_or_effect_unproven", action); continue; }
                // A per-action proof cannot be replaced by evidence collected for another action.
                var localMatches = actionKnowledge == null ? matches : actionKnowledge(action) ?? Array.Empty<AIKnowledgeMatch>();
                var plan = BuildPlan(action, method, world, objective, localMatches, limits, assessed);
                if (plan == null) { RejectCandidate("knowledge_not_proven_for_action", action); continue; }
                if (!replanner.CanRetry(plan.Signature, observedFingerprint))
                { RejectCandidate("repeated_failure_unchanged_observation", action, plan); continue; }
                if (!AICognitivePlanEvaluator.Evaluate(plan, world, limits))
                { RejectCandidate(plan.RejectionReason ?? "plan_validation_failed", action, plan); continue; }
                if (calibration != null)
                {
                    float adjustment = calibration(plan);
                    if (AIWorldFact.Finite(adjustment)) plan.Utility += Mathf.Clamp(adjustment, -.1f, .1f);
                }
                // Keep genuinely different methods, not six copies of the same move with different cells.
                if (!bestByMethod.TryGetValue(method, out var previous) || AICognitivePlanEvaluator.Compare(plan, previous) < 0)
                {
                    if (previous != null) RejectCandidate("same_method_dominated", previous);
                    bestByMethod[method] = plan;
                }
                else RejectCandidate("same_method_dominated", plan);
            }
            // Dictionary traversal never decides order: a total stable comparator is applied before truncation.
            foreach (var entry in bestByMethod) beam.Add(entry.Value);
            beam.Sort(AICognitivePlanEvaluator.Compare);
            while (beam.Count > limits.BeamWidth)
            { RejectCandidate("beam_width_limit", beam[beam.Count - 1]); beam.RemoveAt(beam.Count - 1); }
            foreach (var plan in beam)
            {
                if (candidates.Count >= Math.Min(limits.MaxPlansTotal, limits.MaxPlansPerGoal))
                { RejectCandidate("plan_count_limit", plan); continue; }
                if (signatures.Add(plan.Signature)) candidates.Add(plan);
                else RejectCandidate("duplicate_plan", plan);
            }
            if (candidates.Count == 0)
            { Suspend(AITurnBudget.Expired ? "budget_exhausted" : "no_evidence_supported_legal_plan"); finalized = true; yield break; }

            AICognitivePlan retained = null;
            if (Current != null)
                foreach (var plan in candidates) if (plan.Signature == Current.Signature) { retained = plan; break; }
            var best = candidates[0];
            Current = retained != null && !replanner.ShouldSwitch(retained, best, limits.SwitchMargin) ? retained : best;
            Current.Status = AICognitivePlanStatus.Active;
            ReplanReason = retained == null ? "first_step_rebound_to_current_formal_candidates" : replanner.Reason;
            validatedGeneration = world.BoardGeneration;
            finalized = true;
        }
        finally
        {
            // A coroutine may be disposed between candidates or after a callback throws.
            // Keep the objective intention, but never expose an unfinished generation as ready.
            if (!finalized)
            {
                validatedGeneration = -1; candidates.Clear(); beam.Clear(); signatures.Clear(); bestByMethod.Clear();
                Suspend("planning_cancelled_before_finalization"); RejectCandidate("planning_cancelled_before_finalization");
            }
        }
    }

    public bool BindPreferred(int generation, IReadOnlyList<AIAction> formalActions, out AIAction action)
    {
        action = null;
        if (Current == null || Current.Status != AICognitivePlanStatus.Active || validatedGeneration != generation ||
            Current.BoardGenerationAtValidation != generation || AITurnBudget.Expired || formalActions == null) return false;
        for (int i = 0; i < formalActions.Count && i < MaximumFormalCandidates; i++)
        {
            var candidate = formalActions[i];
            if (candidate != null && candidate.StrategicPriority >= 2 && string.IsNullOrEmpty(candidate.StrategicRejectReason) &&
                candidate.APCost == Current.Steps[0].ExpectedCost && AICognitiveActionKey.For(candidate) == Current.NextActionKey)
            { action = candidate; return true; }
        }
        ReplanReason = "bound_action_no_longer_in_formal_candidates";
        return false;
    }

    public void ObserveExecution(string actionKey, bool success, AIObservedWorldModel after, bool meaningfulProgress = true)
    {
        validatedGeneration = -1;
        if (Current == null || string.IsNullOrEmpty(actionKey) || Current.NextActionKey != actionKey) return;
        if (!success)
        { replanner.Fail(Current, after, "formal_execution_failed"); ReplanReason = replanner.Reason; return; }
        if (!meaningfulProgress)
        { replanner.Fail(Current, after, "formal_action_no_observed_progress"); ReplanReason = replanner.Reason; return; }
        bool changed = false;
        var first = Current.Steps[0];
        if (after != null && after.BattleId == Current.BattleId && after.ActorTeam == Current.OwnerTeam)
        {
            if (Find(after.OwnUnits, first.ActorLifeId, out var actor))
                changed = (actor.Position - Current.ExpectedActorPosition).sqrMagnitude < .0001f &&
                    (Current.StartActorPosition - Current.ExpectedActorPosition).sqrMagnitude > .0001f;
            if (Find(after.ObservedOpponents, first.TargetObservedId, out var target) && Current.StartTargetHP >= 0)
                changed |= target.HP < Current.StartTargetHP || target.Barrier < Current.StartTargetBarrier;
            changed |= after.OwnUnits.Count > Current.StartOwnUnitCount;
        }
        first.Status = changed ? AIPlanOperatorStatus.Completed : AIPlanOperatorStatus.AwaitingObservation;
        Current.ActualObservationReason = changed ? "observed_position_protection_or_own_force_delta" : "execution_succeeded_effect_not_yet_confirmed";
        // Action success is not objective success. A new observation must bind every continuation.
        for (int i = 1; i < Current.Steps.Count; i++) Current.Steps[i].Status = AIPlanOperatorStatus.AwaitingObservation;
        Current.Status = AICognitivePlanStatus.Proposed;
        ReplanReason = "execution_observed_revalidate_dependencies";
    }

    public void Reset()
    {
        Current = null; initialized = false; battleId = null; validatedGeneration = -1;
        candidates.Clear(); beam.Clear(); signatures.Clear(); bestByMethod.Clear(); replanner.Reset();
        rejectionReasons.Clear(); RejectedTotalCount = 0;
        ReplanReason = "reset";
    }
    public void Clear() => Reset();
    /// <summary>Save/load keeps an objective intention only. Old orders, actor references and effect reservations are discarded.</summary>
    public void RestoreIntent(AICognitivePlan saved)
    {
        Reset();
        if (saved == null || string.IsNullOrEmpty(saved.BattleId) || string.IsNullOrEmpty(saved.ObjectiveRef) ||
            saved.Goal == AIOperationGoal.None || !Enum.IsDefined(typeof(AIOperationGoal), saved.Goal)) return;
        battleId = saved.BattleId; team = saved.OwnerTeam; initialized = true;
        Current = new AICognitivePlan { BattleId = battleId, OwnerTeam = team, Goal = saved.Goal,
            ObjectiveRef = saved.ObjectiveRef, ParentOperationId = saved.ParentOperationId,
            TargetObservedId = saved.TargetObservedId, Status = AICognitivePlanStatus.Suspended,
            CreatedTurn = saved.CreatedTurn, LastValidatedTurn = -1, BoardGenerationAtValidation = -1 };
        ReplanReason = "restored_objective_requires_fresh_formal_binding";
    }
    void Suspend(string reason)
    { if (Current != null) Current.Status = AICognitivePlanStatus.Suspended; ReplanReason = reason; }

    void RejectCandidate(string reason, AIAction action = null, AICognitivePlan plan = null)
    {
        if (RejectedTotalCount < int.MaxValue) RejectedTotalCount++;
        if (!DiagnosticsEnabled || rejectionReasons.Count >= MaximumRejectionRecords) return;
        string value = reason ?? "unspecified_rejection";
        if (plan != null) value += "|plan=" + plan.PlanId;
        // Only callers that have already accepted current observation/identity legality pass an action.
        string key = action != null ? AICognitiveActionKey.For(action) : plan?.NextActionKey;
        if (!string.IsNullOrEmpty(key)) value += "|action=" + key;
        rejectionReasons.Add(value.Length <= 2048 ? value : value.Substring(0, 2048));
    }
    void RejectCandidate(string reason, AICognitivePlan plan) => RejectCandidate(reason, null, plan);
    static string BasicRejectionReason(AIAction action, AIObservedWorldModel world)
    {
        if (action == null) return "missing_action";
        if (action.APCost < 0) return "invalid_AP_cost";
        if (action.APCost > world.OwnAP) return "insufficient_AP";
        if (action.StrategicPriority < 2) return "protected_safety_band";
        if (!string.IsNullOrEmpty(action.StrategicRejectReason)) return "formal_governor_rejected";
        if (action.ActionType == AIActionType.Wait || action.ActionType == AIActionType.Rotate) return "unsupported_planner_action";
        return "illegal_or_stale_action";
    }

    static bool BasicLegal(AIAction a, AIObservedWorldModel world)
    {
        if (a == null || a.ActionType == AIActionType.Wait || a.ActionType == AIActionType.Rotate || a.APCost < 0 ||
            a.APCost > world.OwnAP || a.StrategicPriority < 2 || !string.IsNullOrEmpty(a.StrategicRejectReason) ||
            !AIWorldFact.Finite(a.Score) || !AIWorldFact.Finite(a.TargetPos.x) || !AIWorldFact.Finite(a.TargetPos.y) ||
            !AIWorldFact.Finite(a.TargetPos.z)) return false;
        // R4 consumes the existing fixed board; this guard changes no map or official movement rule.
        if (world.Width == 40 && world.Depth == 40 &&
            (a.TargetPos.x < 0 || a.TargetPos.x > 39 || a.TargetPos.z < 0 || a.TargetPos.z > 39)) return false;
        if (a.Unit != null && !Find(world.OwnUnits, a.Unit.ReflectionLifeId, out _)) return false;
        if (a.TargetUnit != null && !Find(world.OwnUnits, a.TargetUnit.ReflectionLifeId, out _) &&
            !Find(world.ObservedOpponents, a.TargetUnit.ReflectionLifeId, out _)) return false;
        return true;
    }

    static AICognitiveActionAssessment ConservativeAssessment(AIAction action, AIObservedWorldModel world)
    {
        // A formal candidate is necessary but not sufficient for a preparation promotion.
        return new AICognitiveActionAssessment { Legal = true, HardSafe = action.StrategicPriority >= 2,
            Confidence = action.Unit != null ? 1f : .75f };
    }

    static string MethodFor(AIAction action, AIObservedWorldModel world, AIWorldObjectiveValue objective, AICognitiveActionAssessment assessed)
    {
        switch (action.ActionType)
        {
            case AIActionType.Attack: return "ATTACK";
            case AIActionType.SkillUse:
                if (assessed.EffectiveSupport > 0) return "FORM_SUPPORT";
                if (assessed.EffectiveDamage > 0) return "ATTACK";
                return null; // Utility skills require a formal effect witness; their name alone is insufficient.
            case AIActionType.Retreat: return "DISENGAGE";
            case AIActionType.DefenseRepos: return "FORM_DEFENSE";
            case AIActionType.Support: return "FORM_SUPPORT";
            case AIActionType.Surround: return "SURROUND";
            case AIActionType.Summon: return "REINFORCE";
            case AIActionType.Build: case AIActionType.Upgrade: return "PLAN_PRODUCTION";
            case AIActionType.Move:
                if (assessed.OpenedAlliedFireLine) return "OPEN_LINE";
                if (assessed.ExpectedInformationGain > 0) return "SCOUT_FOR_ANSWER";
                if (action.Unit == null || !Find(world.OwnUnits, action.Unit.ReflectionLifeId, out var actor)) return null;
                if (objective.HasPosition && Distance(actor.Position, objective.Position) > Distance(action.TargetPos, objective.Position))
                    return "IMPROVE_POSITION";
                return null;
            default: return null;
        }
    }

    static AICognitivePlan BuildPlan(AIAction action, string method, AIObservedWorldModel world,
        AIWorldObjectiveValue objective, IReadOnlyList<AIKnowledgeMatch> matches, AICognitivePlannerLimits limits,
        AICognitiveActionAssessment assessed)
    {
        var plan = new AICognitivePlan { BattleId = world.BattleId, OwnerTeam = world.ActorTeam, Goal = objective.Goal,
            ObjectiveRef = objective.Id, ParentOperationId = objective.OperationId, TargetObservedId = objective.TargetLifeId,
            PrimaryMethod = method, CreatedTurn = world.OwnTurn, LastValidatedTurn = world.OwnTurn,
            BoardGenerationAtValidation = world.BoardGeneration, StrategicPriorityBand = action.StrategicPriority,
            Confidence = Mathf.Min(objective.Confidence, assessed.Confidence), APCost = action.APCost,
            EffectiveDamage = assessed.EffectiveDamage, EffectiveSupport = assessed.EffectiveSupport,
            PredictedIncomingDamage = assessed.PredictedIncomingDamage, ProvenVictory = assessed.ProvenVictory,
            PreparationProven = assessed.PreparationProven, ExistingActionScore = action.Score,
            AbandonCondition = "formal_goal_unavailable_or_protected_safety_or_economy",
            ReplanTrigger = "new_observation_or_step_failure_or_changed_formal_candidates" };
        plan.StartOwnAP = world.OwnAP; plan.StartOwnUnitCount = world.OwnUnits.Count;
        plan.ExpectedActorPosition = action.TargetPos;
        if (action.Unit != null && Find(world.OwnUnits, action.Unit.ReflectionLifeId, out var startActor))
            plan.StartActorPosition = startActor.Position;
        if (action.TargetUnit != null && Find(world.ObservedOpponents, action.TargetUnit.ReflectionLifeId, out var startTarget))
        { plan.StartTargetHP = startTarget.HP; plan.StartTargetBarrier = startTarget.Barrier; }

        var effectGroups = new HashSet<string>(StringComparer.Ordinal);
        var continuations = new List<string>(6);
        for (int i = 0; matches != null && i < matches.Count && i < limits.MaxKnowledgeMatches; i++)
        {
            var match = matches[i];
            if (match == null || match.Principle == null || match.State != AITruthValue.True ||
                !MatchesGoal(match.Principle, objective.Goal) || !SupportsMethod(match.Principle, method) ||
                !effectGroups.Add(match.Principle.EffectGroup)) continue;
            if (plan.KnowledgeIds.Count >= 4) break;
            plan.KnowledgeIds.Add(match.Principle.Id); plan.KnowledgeVersions.Add(match.Principle.Version);
            plan.Confidence = Mathf.Min(plan.Confidence, match.Confidence);
            foreach (string evidence in match.EvidenceIds) AddUnique(plan.ExplanationEvidenceIds, evidence, 32);
            foreach (string op in match.Principle.OperatorIds) AddUnique(continuations, op, 6);
        }
        if (plan.KnowledgeIds.Count == 0) return null;
        string actorId = action.Unit != null ? action.Unit.ReflectionLifeId : string.Empty;
        string targetId = action.TargetUnit != null ? action.TargetUnit.ReflectionLifeId : objective.TargetLifeId;
        plan.Steps.Add(Node(0, method, actorId, targetId, AICognitiveActionKey.For(action), action.APCost, assessed.PreparationProven));
        foreach (string evidence in plan.ExplanationEvidenceIds) plan.Steps[0].RequiredEvidenceIds.Add(evidence);
        string last = method;
        // Back-chain the objective through compatible effects. Unproven future nodes stay explicitly unbound.
        foreach (string next in continuations)
        {
            if (plan.Steps.Count >= limits.MaxOperatorsPerPlan) break;
            if (next == method || !CanFollow(last, next)) continue;
            var node = Node(plan.Steps.Count, next, string.Empty, objective.TargetLifeId, null, 0, false);
            node.Dependencies.Add(node.StepId - 1); plan.Steps.Add(node); plan.HasUnknownContinuationCost = true; last = next;
        }
        if (method == "OPEN_LINE" && plan.Steps.Count < limits.MaxOperatorsPerPlan && last != "ATTACK")
        {
            var attack = Node(plan.Steps.Count, "ATTACK", string.Empty, objective.TargetLifeId, null, 0, false);
            attack.Dependencies.Add(attack.StepId - 1); plan.Steps.Add(attack); plan.HasUnknownContinuationCost = true;
        }
        plan.GoalProgress = Mathf.Clamp01(assessed.ExpectedGoalProgress);
        if (plan.GoalProgress == 0 && action.Unit != null && objective.HasPosition && Find(world.OwnUnits, actorId, out var own))
        {
            float before = Distance(own.Position, objective.Position), after = Distance(action.TargetPos, objective.Position);
            // Only the current, already legal move's actual distance improvement is estimated. This is not a route/LoS proof.
            if (action.ActionType == AIActionType.Move && before > after) plan.GoalProgress = (before - after) / Mathf.Max(1, before);
        }
        plan.Signature = world.BattleId + "|" + (int)world.ActorTeam + "|" + (int)plan.Goal + "|" + objective.Id + "|" + method;
        foreach (var step in plan.Steps) plan.Signature += "|" + step.OperatorId;
        foreach (string id in plan.KnowledgeIds) plan.Signature += "|" + id;
        // Actor/cell differences are not different methods, but the selected first binding remains recorded independently.
        plan.PlanId = AICognitivePlan.StableId(plan.Signature);
        return plan;
    }

    static AIPlanOperator Node(int id, string op, string actor, string target, string key, int cost, bool proven)
        => new AIPlanOperator { StepId = id, OperatorId = op, ActorLifeId = actor, TargetObservedId = target, BoundActionKey = key,
            ExpectedCost = cost, CostKnown = !string.IsNullOrEmpty(key), PreparationProven = proven,
            Status = string.IsNullOrEmpty(key) ? AIPlanOperatorStatus.AwaitingObservation : AIPlanOperatorStatus.Ready,
            ExpectedEffect = Effect(op), RevalidationRule = "fresh_formal_candidates_AP_vision_height_counter_safety",
            FailureFallback = "invalidate_dependants_and_replan_same_objective", Mission = Mission(op) };
    static string Effect(string op)
    {
        switch (op)
        {
            case "ATTACK": case "FOCUS_FIRE": return "observed_target_HP_or_barrier_delta";
            case "OPEN_LINE": return "actor_arrived_then_formal_allied_attack_candidate_exists";
            case "REINFORCE": return "official_purchase_and_living_new_unit";
            case "PLAN_PRODUCTION": return "official_build_or_upgrade_and_forecast_delta";
            case "SCOUT_FOR_ANSWER": return "new_observed_decision_relevant_fact";
            default: return "formal_position_or_support_delta_then_revalidate_objective";
        }
    }
    static AISquadMissionRole Mission(string op)
    {
        switch (op)
        {
            case "OPEN_LINE": case "ATTACK": return AISquadMissionRole.Breakthrough;
            case "SURROUND": case "FLANK": return AISquadMissionRole.Flank;
            case "SCOUT_FOR_ANSWER": return AISquadMissionRole.ScoutRoute;
            case "DISENGAGE": return AISquadMissionRole.RetreatCover;
            case "FORM_DEFENSE": return AISquadMissionRole.Guard;
            default: return AISquadMissionRole.None;
        }
    }
    static bool SupportsMethod(AIStrategyPrinciple p, string method)
    {
        foreach (string op in p.OperatorIds)
            if (op == method || method == "ATTACK" && (op == "FOCUS_FIRE" || op == "FINISH_SEQUENCE" || op == "TRADE_EVALUATE" || op == "BUDGET_COUNTER") ||
                method == "IMPROVE_POSITION" && (op == "CONCENTRATE" || op == "REPOSITION" || op == "PRESS_ADVANTAGE" || op == "FIND_FIRING_POSITION") ||
                method == "REINFORCE" && op == "RECRUIT_MISSING_ROLE" ||
                method == "PLAN_PRODUCTION" && (op == "REBUILD" || op == "REINFORCE")) return true;
        return false;
    }
    static bool CanFollow(string before, string after)
    {
        if (before == "ATTACK" || before == "FOCUS_FIRE" || before == "FINISH_SEQUENCE") return after == "DISENGAGE" || after == "HOLD";
        if (before == "OPEN_LINE" || before == "IMPROVE_POSITION" || before == "FLANK" || before == "SURROUND")
            return after == "ATTACK" || after == "FOCUS_FIRE" || after == "BUDGET_COUNTER" || after == "DEBUFF_THEN_ATTACK";
        if (before == "REINFORCE" || before == "PLAN_PRODUCTION") return after == "ASSEMBLE" || after == "CONCENTRATE" || after == "SYNCHRONIZE";
        if (before == "ASSEMBLE" || before == "CONCENTRATE" || before == "SYNCHRONIZE") return after == "IMPROVE_POSITION" || after == "ATTACK" || after == "FOCUS_FIRE";
        if (before == "DEBUFF_THEN_ATTACK" || before == "BUDGET_COUNTER" || before == "TRADE_EVALUATE") return after == "ATTACK" || after == "FOCUS_FIRE";
        if (before == "DISENGAGE") return after == "FORM_SUPPORT" || after == "ASSEMBLE" || after == "REBUILD";
        if (before == "FORM_SUPPORT") return after == "ATTACK" || after == "ASSEMBLE";
        return false;
    }
    static bool MatchesGoal(AIStrategyPrinciple principle, AIOperationGoal goal)
    { foreach (var allowed in principle.GoalTags) if (allowed == goal) return true; return false; }
    static void AddUnique(List<string> values, string value, int cap)
    { if (!string.IsNullOrEmpty(value) && values.Count < cap && !values.Contains(value)) values.Add(value); }
    static bool Find(IReadOnlyList<AIObservedUnitValue> units, string id, out AIObservedUnitValue value)
    { foreach (var unit in units) if (unit.LifeId == id) { value = unit; return true; } value = default; return false; }
    static float Distance(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));
}

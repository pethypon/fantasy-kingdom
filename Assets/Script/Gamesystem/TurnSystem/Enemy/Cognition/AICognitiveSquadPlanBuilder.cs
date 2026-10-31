using System;
using System.Collections.Generic;

/// <summary>
/// Adds an observation-bound dependency graph from the existing coordinator. It never executes,
/// reserves AP, applies predicted damage/debuffs, or changes permanent squad roles.
/// </summary>
public static class AICognitiveSquadPlanBuilder
{
    const int MaximumSequence = 5;
    const int MaximumFormalScan = 64;

    public static bool Enrich(AICognitivePlan plan, AIObservedWorldModel world, AIBoardState board,
        IReadOnlyList<AIAction> formalActions, AICognitiveAIConfig config)
    {
        if (config == null || !config.Enabled || !config.EnableWorldModel || !config.EnableAutonomousPlanning
            || !config.EnableMultiStepSquadPlanning || plan == null || world == null || board == null
            || board.Operations == null || formalActions == null || !world.IsCurrent(board) || AITurnBudget.Expired
            || board.Governor == null || board.Governor.Mode == StrategicMode.EmergencyDefense
            || board.Governor.Mode == StrategicMode.EconomicRecovery) return false;

        var source = board.Operations.CoordinationSteps;
        int maximum = Math.Min(MaximumSequence, Math.Min(6, config.MaxOperatorsPerPlan));
        if (source == null || source.Count < 2 || source.Count > maximum) return false;
        var witnesses = new List<AICognitiveSquadActionProof>(source.Count);
        var actions = new List<AIAction>(source.Count);
        long operationId = 0;
        Status target = null;
        for (int i = 0; i < source.Count; i++)
        {
            if (AITurnBudget.Expired || !TryCurrentAction(source[i], formalActions, board, out var action)) return false;
            if (i == 0)
            {
                target = action.TargetUnit;
                if (AICognitiveActionKey.For(action) != plan.NextActionKey || !board.IsSpecialActorObserved(target)
                    || !target.IsAlive || !string.IsNullOrEmpty(target.Special?.guardianPartnerId)) return false;
                if (!board.Operations.TryGetSquad(action.Unit.ReflectionLifeId, out var headSquad)) return false;
                operationId = headSquad.OperationId;
                if (operationId <= 0 || plan.ParentOperationId > 0 && plan.ParentOperationId != operationId
                    || !HasCurrentOperation(board.Operations, operationId, plan.Goal, target.ReflectionLifeId)) return false;
            }
            else if (action.TargetUnit != target) return false;

            if (!board.Operations.TryGetSquad(action.Unit.ReflectionLifeId, out var squad) || squad.OperationId != operationId) return false;
            var assessment = board.Governor.AssessCognitiveAction(action, board);
            if (!assessment.IsFinite || !assessment.Legal || !assessment.HardSafe) return false;
            actions.Add(action);
        }

        // Recalculate the coordinator's current-target loss using one finite protection budget.
        // Guaranteed control is not applied to that budget before its real execution.
        var protection = AIObservedDamageBudget.Capture(target);
        int remainingHp = target.HP;
        for (int i = 0; i < actions.Count; i++)
        {
            if (AITurnBudget.Expired || remainingHp <= 0) return false;
            var action = actions[i];
            int expectedLoss = 0;
            if (action.ActionType == AIActionType.Attack)
            {
                int raw = BarrierSystem.NormalAttackDamage(action.Unit.kind, protection.Barrier,
                    AIActionEvaluator.EstimateSpecialAwareAttack(action.Unit, target, board));
                expectedLoss = Math.Min(remainingHp, AIActionEvaluator.EstimateObservedHitLoss(target, board,
                    raw, HpChangeKind.NormalDamage, ref protection));
                remainingHp -= expectedLoss;
            }
            bool control = action.ActionType == AIActionType.SkillUse && IsGuaranteedControl(action);
            witnesses.Add(new AICognitiveSquadActionProof(AICognitiveActionKey.For(action),
                action.Unit.ReflectionLifeId, target.ReflectionLifeId, action.ActionType, action.APCost,
                expectedLoss, board.Generation, true, true, control));
        }
        int minimumStepMs = Math.Max(1, Math.Min(250, AIOperationSquadConfig.Active.MinimumChainStepBudgetMs));
        if (!TryComposeObservedChain(plan, world, source, witnesses, maximum,
            board.RemainingPlannedActionSlots, AITurnBudget.RemainingMs, minimumStepMs, out var nodes)) return false;

        // Commit only after every check has passed. Preserve the first-action evaluation: a future
        // tentative kill cannot inflate utility, proven victory, actual damage, or paid AP.
        plan.Steps = nodes;
        plan.ParentOperationId = operationId;
        plan.HasUnknownContinuationCost = true;
        plan.ReplanTrigger = "squad_step_execution_then_fresh_AP_visibility_protection_and_target_validation";
        string suffix = "|squad-sequence";
        foreach (var node in nodes) suffix += "|" + node.ActorLifeId + ":" + node.OperatorId;
        // Keep the canonical means signature stable: changing members must not evade failed-plan suppression.
        plan.SquadSequenceSignature = suffix;
        return true;
    }

    static bool HasCurrentOperation(AIOperationManager operations, long operationId, AIOperationGoal goal, string targetId)
    {
        foreach (var operation in operations.ActiveOperations)
            if (operation != null && operation.OperationId == operationId && operation.PrimaryGoal == goal
                && (operation.Status == AIOperationStatus.Active || operation.Status == AIOperationStatus.Preparing)
                && (string.IsNullOrEmpty(operation.TargetLifeId) || operation.TargetLifeId == targetId)) return true;
        return false;
    }

    static bool TryCurrentAction(AIOperationCoordinationStep step, IReadOnlyList<AIAction> formal,
        AIBoardState board, out AIAction found)
    {
        found = null;
        if (step == null || step.APCost < 0 || step.ExpectedHpLoss < 0
            || string.IsNullOrEmpty(step.ActorLifeId) || string.IsNullOrEmpty(step.TargetLifeId)
            || step.ActionType != AIActionType.Attack && step.ActionType != AIActionType.SkillUse) return false;
        string foundKey = null;
        for (int i = 0; i < formal.Count && i < MaximumFormalScan; i++)
        {
            var action = formal[i];
            if (action == null || action.ActionType != step.ActionType || action.APCost != step.APCost
                || action.SpecialCommand != null || !string.IsNullOrEmpty(action.StrategicRejectReason)
                || !board.IsSpecialActorObserved(action.Unit) || !board.IsSpecialActorObserved(action.TargetUnit)
                || action.Unit.team != board.ActorTeam || !action.Unit.IsAlive || !action.TargetUnit.IsAlive
                || MatchObjectiveRules.IsCommander(action.Unit) || action.Unit.ReflectionLifeId != step.ActorLifeId
                || action.TargetUnit.ReflectionLifeId != step.TargetLifeId || StatusEffectSystem.IsStunned(action.Unit)) continue;
            if (action.ActionType == AIActionType.Attack)
            {
                // A remaining-AP damage curve needs a step-by-step AP simulation. The
                // coordinator's current-board estimate cannot certify that later damage.
                var definition = SpecialSkillRuntime.Definition(action.Unit);
                if (definition != null && definition.enabledForPlay && definition.ability == UnitSpecialSkill.NavigatorPull
                    && definition.HasCurve(definition.navigatorAPDamageCurve)) continue;
                if (board.CalcAttackCost(action.Unit) != action.APCost || !board.GetAttackTargets(action.Unit).Contains(action.TargetUnit)) continue;
            }
            else if (!IsGuaranteedControl(action) || board.CalcSkillCost(action.Unit, action.Skill) != action.APCost
                || !board.GetSkillTargets(action.Unit, action.Skill).Contains(action.TargetUnit)) continue;
            string key = AICognitiveActionKey.For(action);
            // Coordination steps do not store skill IDs. Multiple distinct legal skills with
            // the same descriptor are ambiguous, so do not guess which one was coordinated.
            if (found != null && (foundKey != key || !ReferenceEquals(found.Skill, action.Skill))) return false;
            found = action; foundKey = key;
        }
        return found != null;
    }
    static bool IsGuaranteedControl(AIAction action) => action.Skill != null && action.SpecialCommand == null
        && action.Skill.InflictDebuff != StatusEffectType.None && AIWorldFact.Finite(action.Skill.DebuffChance)
        && action.Skill.DebuffChance >= 1;

    /// <summary>Pure value contract seam for fixtures. Witnesses must come from the official validation above.</summary>
    internal static bool TryComposeObservedChain(AICognitivePlan plan, AIObservedWorldModel world,
        IReadOnlyList<AIOperationCoordinationStep> source, IReadOnlyList<AICognitiveSquadActionProof> witnesses,
        int maximumSteps, int remainingSlots, double remainingMilliseconds, int minimumStepMilliseconds,
        out List<AIPlanOperator> result)
    {
        result = null;
        if (plan == null || world == null || source == null || witnesses == null || source.Count < 2
            || source.Count > MaximumSequence || source.Count > maximumSteps || source.Count > remainingSlots
            || witnesses.Count != source.Count || double.IsNaN(remainingMilliseconds) || double.IsInfinity(remainingMilliseconds)
            || remainingMilliseconds < source.Count * (long)Math.Max(1, minimumStepMilliseconds)
            || plan.BattleId != world.BattleId || plan.OwnerTeam != world.ActorTeam
            || plan.BoardGenerationAtValidation != world.BoardGeneration || !plan.HasValidDependencies() || string.IsNullOrEmpty(plan.Signature)
            || plan.Status != AICognitivePlanStatus.Active && plan.Status != AICognitivePlanStatus.Validated) return false;
        if (source[0] == null || !CurrentTarget(world, source[0].TargetLifeId, out var target)) return false;
        if (!string.IsNullOrEmpty(plan.TargetObservedId) && plan.TargetObservedId != target.LifeId) return false;
        var actors = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<AIPlanOperator>(source.Count);
        long spent = 0, hpLoss = 0;
        for (int i = 0; i < source.Count; i++)
        {
            var step = source[i]; var witness = witnesses[i];
            if (step == null || string.IsNullOrEmpty(step.ActorLifeId) || !actors.Add(step.ActorLifeId)
                || step.TargetLifeId != target.LifeId || !CurrentOwn(world, step.ActorLifeId)
                || !witness.Legal || !witness.Safe || witness.Generation != world.BoardGeneration
                || witness.ActorLifeId != step.ActorLifeId || witness.TargetLifeId != step.TargetLifeId
                || witness.ActionType != step.ActionType || witness.APCost != step.APCost || witness.APCost < 0
                || witness.ExpectedHpLoss != step.ExpectedHpLoss || witness.ExpectedHpLoss < 0
                || string.IsNullOrEmpty(witness.ActionKey) || hpLoss >= target.HP) return false;
            if (step.ActionType != AIActionType.Attack && (step.ActionType != AIActionType.SkillUse
                || !witness.GuaranteedControl || witness.ExpectedHpLoss != 0)) return false;
            spent += witness.APCost; hpLoss += witness.ExpectedHpLoss;
            if (spent > world.OwnAP || hpLoss > target.HP) return false;
            if (i == 0 && witness.ActionKey != plan.NextActionKey) return false;
            var node = i == 0 ? plan.Steps[0].Copy() : new AIPlanOperator();
            node.StepId = i; node.ActorLifeId = witness.ActorLifeId; node.TargetObservedId = witness.TargetLifeId;
            node.OperatorId = step.ActionType == AIActionType.SkillUse ? "DEBUFF_THEN_ATTACK" : "FOCUS_FIRE";
            node.ExpectedCost = witness.APCost; node.CostKnown = i == 0;
            node.Status = i == 0 ? AIPlanOperatorStatus.Ready : AIPlanOperatorStatus.AwaitingObservation;
            node.BoundActionKey = i == 0 ? witness.ActionKey : null;
            node.Dependencies = i == 0 ? new List<int>() : new List<int> { i - 1 };
            node.Mission = step.ActionType == AIActionType.SkillUse ? AISquadMissionRole.Suppress : AISquadMissionRole.Breakthrough;
            node.ExpectedEffect = step.ActionType == AIActionType.SkillUse ? "actual_control_effect_then_regenerate_attack" : "actual_target_HP_or_barrier_delta";
            node.RevalidationRule = "fresh_formal_candidate_same_observed_target_current_AP_fatigue_height_and_counter";
            node.FailureFallback = "invalidate_remaining_sequence_and_replan_without_predicted_effects";
            nodes.Add(node);
        }
        if (hpLoss != target.HP) return false; // This bridge admits only the existing coordinator's bounded kill proposals.
        result = nodes; return true;
    }
    static bool CurrentOwn(AIObservedWorldModel world, string id)
    {
        foreach (var value in world.OwnUnits)
            if (value.LifeId == id && value.Team == world.ActorTeam && value.HP > 0 && !value.IsCommander && !value.IsBuilding && !value.IsCrystal
                && value.Source == AIWorldFactSource.CurrentObservation && value.ObservedTurn == world.GlobalTurn) return true;
        return false;
    }
    static bool CurrentTarget(AIObservedWorldModel world, string id, out AIObservedUnitValue target)
    {
        foreach (var value in world.ObservedOpponents)
            if (value.LifeId == id && value.Team != world.ActorTeam && value.HP > 0
                && value.Source == AIWorldFactSource.CurrentObservation && value.ObservedTurn == world.GlobalTurn)
            { target = value; return true; }
        target = default; return false;
    }
}

/// <summary>Current-generation proof copied from a formal candidate; contains no Unity references.</summary>
internal readonly struct AICognitiveSquadActionProof
{
    public readonly string ActionKey, ActorLifeId, TargetLifeId;
    public readonly AIActionType ActionType;
    public readonly int APCost, ExpectedHpLoss, Generation;
    public readonly bool Legal, Safe, GuaranteedControl;
    public AICognitiveSquadActionProof(string key, string actor, string target, AIActionType type, int cost,
        int hpLoss, int generation, bool legal, bool safe, bool control = false)
    { ActionKey = key; ActorLifeId = actor; TargetLifeId = target; ActionType = type; APCost = cost;
        ExpectedHpLoss = hpLoss; Generation = generation; Legal = legal; Safe = safe; GuaranteedControl = control; }
}

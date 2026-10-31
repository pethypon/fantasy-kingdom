using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded observed focus fire and preparation. The normal executor remains the only execution path.</summary>
public sealed partial class AIOperationManager
{
    readonly List<AIAction> chainCandidates = new List<AIAction>(64);
    readonly List<AIAction> currentChain = new List<AIAction>(5);
    readonly List<Status> chainTargets = new List<Status>(8);
    readonly HashSet<string> chainActors = new HashSet<string>(StringComparer.Ordinal);
    readonly List<AIOperationCoordinationStep> coordinationSteps = new List<AIOperationCoordinationStep>(5);
    readonly List<AIAction> coordinatedActions = new List<AIAction>(5);
    readonly List<Vector3> coordinationOrigins = new List<Vector3>(5);
    readonly List<Direction> coordinationFacings = new List<Direction>(5);
    AIAction coordinationHead;
    Status coordinationTarget;
    Vector3 coordinationTargetPosition;
    int coordinationAP, coordinationHP, coordinationBarrier;
    bool coordinationWinsObjective, coordinationHasSafeAlternative;
    AIBoardState coordinationBoard;
    int coordinationGeneration = -1;
    public IReadOnlyList<AIOperationCoordinationStep> CoordinationSteps => coordinationSteps;

    public bool IsCoordinatedKillHead(AIAction action, AIBoardState board)
    {
        if (action == null || action != coordinationHead || action.ActionType != AIActionType.Attack
            || board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel)
            || !CurrentCoordination(board) || coordinationSteps.Count < 2) return false;
        long hpLoss = 0;
        foreach (var step in coordinationSteps) hpLoss += Mathf.Max(0, step.ExpectedHpLoss);
        if (hpLoss < coordinationTarget.HP) return false;
        foreach (var candidate in coordinatedActions)
            if (!SafeCoordinationActor(candidate.Unit, board)) return false;
        return true;
    }

    public void UpdateTacticalCoordination(List<AIAction> actions, AIBoardState board)
    {
        ClearCoordination();
        if (actions == null || board == null || state.Ended || !AIOperationSquadConfig.Active.Enabled) return;
        if (squadBoard != board || squadGeneration != board.Generation) return;
        var policy = AIOperationSquadConfig.Active;
        coordinationBoard = board; coordinationGeneration = board.Generation;
        int targetLimit = Mathf.Clamp(policy.MaximumChainTargets, 1, 8);
        foreach (var action in actions)
        {
            if (action == null) continue;
            var target = action.TargetUnit;
            if (action.ActionType != AIActionType.Attack || action.Unit == null || target == null || !board.IsSpecialActorObserved(target)
                || !target.IsAlive || !squadByActor.ContainsKey(action.Unit.ReflectionLifeId)
                || action.APCost < 0 || action.APCost > board.EnemyAP || !LegalSquadTargets(action.Unit, board).Contains(target)) continue;
            if (!chainTargets.Contains(target))
            {
                if (chainTargets.Count < targetLimit) chainTargets.Add(target);
                else if (IsVictoryTarget(target))
                    for (int i = chainTargets.Count - 1; i >= 0; i--)
                        if (!IsVictoryTarget(chainTargets[i])) { chainTargets[i] = target; break; }
            }
        }
        chainTargets.Sort((a, b) =>
        {
            int objective = IsVictoryTarget(b).CompareTo(IsVictoryTarget(a));
            return objective != 0 ? objective : string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId);
        });
        float best = float.NegativeInfinity;
        foreach (var target in chainTargets)
        {
            if (AITurnBudget.Expired) break;
            // A linked recipient can lose its own HP while this target remains alive.
            // Conservatively omit compound-kill claims until the link has actually ended.
            if (!string.IsNullOrEmpty(target.Special?.guardianPartnerId)) continue;
            chainCandidates.Clear();
            foreach (var action in actions)
                if (action != null && action.ActionType == AIActionType.Attack && action.TargetUnit == target && action.Unit != null && action.Unit.IsAlive
                    && action.Unit.team == board.ActorTeam && !MatchObjectiveRules.IsCommander(action.Unit)
                    && !StatusEffectSystem.IsStunned(action.Unit) && action.APCost >= 0 && action.APCost <= board.EnemyAP
                    && squadByActor.ContainsKey(action.Unit.ReflectionLifeId) && board.CalcAttackCost(action.Unit) == action.APCost
                    && !HasAPDependentAttack(action.Unit) && LegalSquadTargets(action.Unit, board).Contains(target))
                { if (chainCandidates.Count < 64) chainCandidates.Add(action); }
            chainCandidates.Sort((a, b) =>
            {
                // Order uses current observed protection, then the sequence below spends a shared finite budget.
                float va = AIActionEvaluator.EstimateEffectiveSpecialImpact(a.Unit, target, board, out _) / (float)Mathf.Max(1, a.APCost);
                float vb = AIActionEvaluator.EstimateEffectiveSpecialImpact(b.Unit, target, board, out _) / (float)Mathf.Max(1, b.APCost);
                int efficient = vb.CompareTo(va);
                return efficient != 0 ? efficient : string.CompareOrdinal(a.Unit.ReflectionLifeId, b.Unit.ReflectionLifeId);
            });
            var protection = AIObservedDamageBudget.Capture(target);
            currentChain.Clear(); chainActors.Clear(); int spent = 0, damage = 0; float loss = 0;
            long operation = chainCandidates.Count > 0 ? squadByActor[chainCandidates[0].Unit.ReflectionLifeId].OperationId : 0;
            foreach (var attack in chainCandidates)
            {
                int chainLimit = board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel) ? 2 : Mathf.Clamp(policy.MaximumChainActions, 2, 5);
                if (currentChain.Count >= chainLimit || damage >= target.HP) break;
                if (spent + (long)attack.APCost > board.EnemyAP
                    || squadByActor[attack.Unit.ReflectionLifeId].OperationId != operation || !chainActors.Add(attack.Unit.ReflectionLifeId)) continue;
                int raw = BarrierSystem.NormalAttackDamage(attack.Unit.kind, protection.Barrier,
                    AIActionEvaluator.EstimateSpecialAwareAttack(attack.Unit, target, board));
                int hpLoss = Mathf.Min(target.HP - damage,
                    AIActionEvaluator.EstimateObservedHitLoss(target, board, raw, HpChangeKind.NormalDamage, ref protection));
                currentChain.Add(attack); spent += attack.APCost; damage += hpLoss;
                loss += board.Governor?.IncomingDamage(board, attack.Unit, attack.Unit.transform.position, null) ?? 0;
            }
            bool victory = IsVictoryTarget(target) && damage >= target.HP;
            bool kill = damage >= target.HP;
            AIAction preparation = null;
            // Never delay an already affordable kill with a redundant setup. A new,
            // guaranteed effective damage debuff may lead an otherwise incomplete chain.
            if (!kill && board.ReconThreatLevel >= Mathf.Max(1, config.MinimumThreatLevel))
                foreach (var candidate in actions)
                {
                    if (currentChain.Count >= Mathf.Clamp(policy.MaximumChainActions, 2, 5)
                        || !UsefulCoordinationDebuff(candidate, target) || candidate.Unit == null
                        || chainActors.Contains(candidate.Unit.ReflectionLifeId)
                        || !TryGetSquad(candidate.Unit.ReflectionLifeId, out var owner) || owner.OperationId != operation
                        || (long)spent + candidate.APCost > board.EnemyAP || !SafeCoordinationActor(candidate.Unit, board)
                        || board.CalcSkillCost(candidate.Unit, candidate.Skill) != candidate.APCost
                        || !board.GetSkillTargets(candidate.Unit, candidate.Skill).Contains(target)) continue;
                    if (preparation == null || candidate.APCost < preparation.APCost
                        || candidate.APCost == preparation.APCost && string.CompareOrdinal(candidate.Unit.ReflectionLifeId, preparation.Unit.ReflectionLifeId) < 0)
                        preparation = candidate;
                }
            if (currentChain.Count < 2 || !kill && preparation == null) continue;
            bool safeAlternative = false;
            foreach (var candidate in chainCandidates)
                if (candidate.Unit != currentChain[0].Unit && AIActionEvaluator.CanFinishSpecialTarget(candidate.Unit, target, board)
                    && (board.Governor?.IncomingDamage(board, candidate.Unit, candidate.Unit.transform.position, null) ?? 0) < candidate.Unit.HP)
                    safeAlternative = true;
            float value = (victory ? 100 : kill ? 20 : 0) + Mathf.Min(target.HP, damage) / (float)Mathf.Max(1, spent + (preparation?.APCost ?? 0)) - loss * .02f;
            if (value <= best) continue;
            best = value; coordinationHead = preparation ?? currentChain[0]; coordinationTarget = target;
            coordinationWinsObjective = victory; coordinationHasSafeAlternative = safeAlternative;
            coordinationSteps.Clear(); coordinatedActions.Clear(); coordinationOrigins.Clear(); coordinationFacings.Clear();
            if (preparation != null)
            {
                coordinatedActions.Add(preparation);
                coordinationOrigins.Add(preparation.Unit.transform.position); coordinationFacings.Add(preparation.Unit.direction);
                coordinationSteps.Add(new AIOperationCoordinationStep { ActorLifeId = preparation.Unit.ReflectionLifeId,
                    TargetLifeId = target.ReflectionLifeId, ActionType = preparation.ActionType, APCost = preparation.APCost });
            }
            protection = AIObservedDamageBudget.Capture(target); int remainingHp = target.HP;
            foreach (var step in currentChain)
            {
                coordinatedActions.Add(step);
                coordinationOrigins.Add(step.Unit.transform.position); coordinationFacings.Add(step.Unit.direction);
                int raw = BarrierSystem.NormalAttackDamage(step.Unit.kind, protection.Barrier,
                    AIActionEvaluator.EstimateSpecialAwareAttack(step.Unit, target, board));
                int actual = Mathf.Min(remainingHp,
                    AIActionEvaluator.EstimateObservedHitLoss(target, board, raw, HpChangeKind.NormalDamage, ref protection));
                remainingHp -= actual;
                coordinationSteps.Add(new AIOperationCoordinationStep { ActorLifeId = step.Unit.ReflectionLifeId, TargetLifeId = target.ReflectionLifeId,
                    ActionType = step.ActionType, APCost = step.APCost, ExpectedHpLoss = actual });
            }
        }
        if (coordinationHead == null) SelectTacticalPreparation(actions, board);
        coordinationAP = board.EnemyAP;
        coordinationHP = coordinationTarget != null ? coordinationTarget.HP : 0;
        coordinationBarrier = coordinationTarget != null ? coordinationTarget.Barrier : 0;
        coordinationTargetPosition = coordinationTarget != null ? coordinationTarget.transform.position : Vector3.zero;
    }

    static bool HasAPDependentAttack(Status actor)
    {
        var special = SpecialSkillRuntime.Definition(actor);
        return special?.enabledForPlay == true && special.ability == UnitSpecialSkill.NavigatorPull;
    }
    static bool SafeCoordinationActor(Status actor, AIBoardState board)
        => actor != null && actor.IsAlive && actor.team == board.ActorTeam && !MatchObjectiveRules.IsCommander(actor)
            && !StatusEffectSystem.IsStunned(actor)
            && (board.Governor?.IncomingDamage(board, actor, actor.transform.position, null)
                ?? board.EstimateCounterDamageAt(actor.transform.position, actor)) < actor.HP;

    internal static bool UsefulCoordinationDebuff(AIAction action, Status target)
    {
        if (action?.ActionType != AIActionType.SkillUse || action.Skill == null || action.SpecialCommand != null
            || action.TargetUnit != target || target == null || !target.IsAlive || action.APCost < 0
            || float.IsNaN(action.Skill.DebuffChance) || float.IsInfinity(action.Skill.DebuffChance) || action.Skill.DebuffChance < 1
            || StatusEffectSystem.IsImmune(target, action.Skill.InflictDebuff)
            || StatusEffectSystem.HasDebuff(target, action.Skill.InflictDebuff)
            || target.specialAbility == SpecialAbility.IndomitableWill && !target.DebuffNullifiedThisTurn) return false;
        // R2 has no ordinary DEF. ArmorBreak must not manufacture a synergy there.
        return action.Skill.InflictDebuff == StatusEffectType.Mark && GameConstants.MarkIncomingDamageIncrease > 0
            || action.Skill.InflictDebuff == StatusEffectType.Freeze && GameConstants.FreezeIncomingDamageIncrease > 0
            || action.Skill.InflictDebuff == StatusEffectType.ArmorBreak && DamageCalculator.UsesLegacyDefence && target.DEF > 0;
    }

    /// <summary>Seven conservative gates: only a currently observed, affordable multi-actor winning objective sequence may spend a non-commander.</summary>
    public bool AllowsObjectiveSacrifice(AIAction action, AIBoardState board)
    {
        if (action == null || action != coordinationHead || !CurrentCoordination(board)
            || !coordinationWinsObjective || coordinationHasSafeAlternative || action.Unit == null || MatchObjectiveRules.IsCommander(action.Unit)
            || action.TargetUnit == null || !IsVictoryTarget(action.TargetUnit) || !board.IsSpecialActorObserved(action.TargetUnit)
            || !squadByActor.TryGetValue(action.Unit.ReflectionLifeId, out var squad) || FindActive(squad.OperationId)?.TargetLifeId != action.TargetUnit.ReflectionLifeId
            || coordinationSteps.Count < 2 || board.Governor?.Mode == StrategicMode.EmergencyDefense
            || coordinationSteps.Count > board.RemainingPlannedActionSlots
            || AITurnBudget.RemainingMs < coordinationSteps.Count * Mathf.Clamp(AIOperationSquadConfig.Active.MinimumChainStepBudgetMs, 1, 250)) return false;
        long ap = 0; var surviving = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in coordinationSteps) { ap += step.APCost; if (step.ActorLifeId != action.Unit.ReflectionLifeId) surviving.Add(step.ActorLifeId); }
        return ap <= board.EnemyAP && surviving.Count > 0;
    }
    static bool IsVictoryTarget(Status target) => target != null && (target.kind == Kind.Crystal || MatchObjectiveRules.IsCommander(target));
    void ClearCoordination()
    {
        coordinationHead = null; coordinationTarget = null; coordinationWinsObjective = coordinationHasSafeAlternative = false;
        coordinationBoard = null; coordinationGeneration = -1;
        chainCandidates.Clear(); chainTargets.Clear(); currentChain.Clear(); chainActors.Clear(); coordinationSteps.Clear(); coordinatedActions.Clear();
        coordinationOrigins.Clear(); coordinationFacings.Clear();
        tacticalTargets.Clear(); tacticalTargetBoard = null; tacticalTargetGeneration = -1;
    }
}

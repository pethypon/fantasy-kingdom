using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded present-position focus-fire proposals. The normal executor remains the only execution path.</summary>
public sealed partial class AIOperationManager
{
    readonly List<AIAction> chainCandidates = new List<AIAction>(64);
    readonly List<AIAction> currentChain = new List<AIAction>(5);
    readonly List<Status> chainTargets = new List<Status>(8);
    readonly HashSet<string> chainActors = new HashSet<string>(StringComparer.Ordinal);
    readonly List<AIOperationCoordinationStep> coordinationSteps = new List<AIOperationCoordinationStep>(5);
    AIAction coordinationHead;
    bool coordinationWinsObjective, coordinationHasSafeAlternative;
    AIBoardState coordinationBoard;
    int coordinationGeneration = -1;
    public IReadOnlyList<AIOperationCoordinationStep> CoordinationSteps => coordinationSteps;

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
            var target = action.TargetUnit;
            if (action.ActionType != AIActionType.Attack || action.Unit == null || target == null || !board.IsSpecialActorObserved(target)
                || !target.IsAlive || !squadByActor.ContainsKey(action.Unit.ReflectionLifeId)
                || action.APCost < 0 || action.APCost > board.EnemyAP || !board.GetAttackTargets(action.Unit).Contains(target)) continue;
            if (!chainTargets.Contains(target) && chainTargets.Count < targetLimit) chainTargets.Add(target);
        }
        chainTargets.Sort((a, b) =>
        {
            int objective = IsVictoryTarget(b).CompareTo(IsVictoryTarget(a));
            return objective != 0 ? objective : string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId);
        });
        float best = float.NegativeInfinity;
        foreach (var target in chainTargets)
        {
            // A linked recipient can lose its own HP while this target remains alive.
            // Conservatively omit compound-kill claims until the link has actually ended.
            if (!string.IsNullOrEmpty(target.Special?.guardianPartnerId)) continue;
            chainCandidates.Clear();
            foreach (var action in actions)
                if (action.ActionType == AIActionType.Attack && action.TargetUnit == target && action.Unit != null && action.Unit.IsAlive
                    && action.Unit.team == board.ActorTeam && !MatchObjectiveRules.IsCommander(action.Unit)
                    && !StatusEffectSystem.IsStunned(action.Unit) && action.APCost >= 0 && action.APCost <= board.EnemyAP
                    && squadByActor.ContainsKey(action.Unit.ReflectionLifeId) && board.GetAttackTargets(action.Unit).Contains(target))
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
            foreach (var attack in chainCandidates)
            {
                int chainLimit = board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel) ? 2 : Mathf.Clamp(policy.MaximumChainActions, 2, 5);
                if (currentChain.Count >= chainLimit || damage >= target.HP) break;
                if (spent + (long)attack.APCost > board.EnemyAP || !chainActors.Add(attack.Unit.ReflectionLifeId)) continue;
                int raw = BarrierSystem.NormalAttackDamage(attack.Unit.kind, protection.Barrier,
                    AIActionEvaluator.EstimateSpecialAwareAttack(attack.Unit, target, board));
                int hpLoss = Mathf.Min(target.HP - damage,
                    AIActionEvaluator.EstimateObservedHitLoss(target, board, raw, HpChangeKind.NormalDamage, ref protection));
                currentChain.Add(attack); spent += attack.APCost; damage += hpLoss;
                loss += board.Governor?.IncomingDamage(board, attack.Unit, attack.Unit.transform.position, null) ?? 0;
            }
            bool victory = IsVictoryTarget(target) && damage >= target.HP;
            if (currentChain.Count < 2 || !victory && damage < target.HP) continue;
            bool safeAlternative = false;
            foreach (var candidate in chainCandidates)
                if (candidate.Unit != currentChain[0].Unit && AIActionEvaluator.CanFinishSpecialTarget(candidate.Unit, target, board)
                    && (board.Governor?.IncomingDamage(board, candidate.Unit, candidate.Unit.transform.position, null) ?? 0) < candidate.Unit.HP)
                    safeAlternative = true;
            float value = (victory ? 100 : 20) + Mathf.Min(target.HP, damage) / (float)Mathf.Max(1, spent) - loss * .02f;
            if (value <= best) continue;
            best = value; coordinationHead = currentChain[0]; coordinationWinsObjective = victory; coordinationHasSafeAlternative = safeAlternative;
            coordinationSteps.Clear(); protection = AIObservedDamageBudget.Capture(target); int remainingHp = target.HP;
            foreach (var step in currentChain)
            {
                int raw = BarrierSystem.NormalAttackDamage(step.Unit.kind, protection.Barrier,
                    AIActionEvaluator.EstimateSpecialAwareAttack(step.Unit, target, board));
                int actual = Mathf.Min(remainingHp,
                    AIActionEvaluator.EstimateObservedHitLoss(target, board, raw, HpChangeKind.NormalDamage, ref protection));
                remainingHp -= actual;
                coordinationSteps.Add(new AIOperationCoordinationStep { ActorLifeId = step.Unit.ReflectionLifeId, TargetLifeId = target.ReflectionLifeId,
                    ActionType = step.ActionType, APCost = step.APCost, ExpectedHpLoss = actual });
            }
        }
        // Guaranteed, currently legal control effects may open the already proposed attacks.
        // This is a hint only: no unexecuted debuff is applied to the damage estimate.
        if (coordinationHead != null)
            foreach (var action in actions)
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.InflictDebuff != StatusEffectType.None
                    && action.Skill.DebuffChance >= 1 && action.TargetUnit == coordinationHead.TargetUnit && action.Unit != null
                    && action.Unit != coordinationHead.Unit && action.Unit.team == board.ActorTeam && action.APCost >= 0
                    && squadByActor.ContainsKey(action.Unit.ReflectionLifeId) && (long)action.APCost + coordinationHead.APCost <= board.EnemyAP
                    && action.SpecialCommand == null && board.GetSkillTargets(action.Unit, action.Skill).Contains(action.TargetUnit))
                {
                    // Adding preparation must still leave the entire observed kill sequence affordable.
                    if (coordinationSteps.Count >= Mathf.Clamp(policy.MaximumChainActions, 2, 5)
                        || board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel)) continue;
                    long needed = action.APCost; foreach (var step in coordinationSteps) needed += step.APCost;
                    if (needed > board.EnemyAP) continue;
                    coordinationHead = action; coordinationWinsObjective = false;
                    coordinationSteps.Insert(0, new AIOperationCoordinationStep { ActorLifeId = action.Unit.ReflectionLifeId,
                        TargetLifeId = action.TargetUnit.ReflectionLifeId, ActionType = action.ActionType, APCost = action.APCost });
                    break;
                }
    }

    /// <summary>Seven conservative gates: only a currently observed, affordable multi-actor winning objective sequence may spend a non-commander.</summary>
    public bool AllowsObjectiveSacrifice(AIAction action, AIBoardState board)
    {
        if (action == null || action != coordinationHead || coordinationBoard != board || coordinationGeneration != board.Generation
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
        coordinationHead = null; coordinationWinsObjective = coordinationHasSafeAlternative = false;
        coordinationBoard = null; coordinationGeneration = -1;
        chainCandidates.Clear(); chainTargets.Clear(); currentChain.Clear(); chainActors.Clear(); coordinationSteps.Clear();
    }
}

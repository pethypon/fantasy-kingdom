using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIOperationManager
{
    readonly List<Status> tacticalTargets = new List<Status>(16);
    AIBoardState tacticalTargetBoard;
    int tacticalTargetGeneration = -1;
    void PrepareTacticalTargets(AIBoardState board)
    {
        if (tacticalTargetBoard == board && tacticalTargetGeneration == board.Generation) return;
        tacticalTargetBoard = board; tacticalTargetGeneration = board.Generation; tacticalTargets.Clear();
        int inspected = 0;
        foreach (var target in board.SpecialObservedActors)
        {
            if (inspected++ >= 256) break;
            if (target == null || target.team == board.ActorTeam || !board.IsSpecialActorObserved(target)) continue;
            if (tacticalTargets.Count < 16) tacticalTargets.Add(target);
            else if (IsVictoryTarget(target))
                for (int i = tacticalTargets.Count - 1; i >= 0; i--)
                    if (!IsVictoryTarget(tacticalTargets[i])) { tacticalTargets[i] = target; break; }
        }
        tacticalTargets.Sort((a, b) =>
        {
            int order = IsVictoryTarget(b).CompareTo(IsVictoryTarget(a));
            return order != 0 ? order : string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId);
        });
    }

    /// <summary>Only existing formal candidates can become a setup head. No predicted
    /// attack is inserted into the executor or credited with unobserved damage.</summary>
    void SelectTacticalPreparation(List<AIAction> actions, AIBoardState board)
    {
        if (board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel) || board.ReconMap == null
            || board.Governor?.Mode == StrategicMode.EmergencyDefense || board.Governor?.Mode == StrategicMode.EconomicRecovery) return;
        PrepareTacticalTargets(board);
        // A real opportunity against a victory target is worth more than another setup.
        foreach (var action in actions)
            if (action?.ActionType == AIActionType.Attack && action.TargetUnit != null && IsVictoryTarget(action.TargetUnit)
                && board.IsSpecialActorObserved(action.TargetUnit) && SafeCoordinationActor(action.Unit, board)
                && action.APCost >= 0 && action.APCost <= board.EnemyAP
                && LegalSquadTargets(action.Unit, board).Contains(action.TargetUnit)
                && AIActionEvaluator.EstimateEffectiveSpecialImpact(action.Unit, action.TargetUnit, board, out _) > 0) return;
        float best = float.NegativeInfinity;
        int inspected = 0;
        foreach (var action in actions)
        {
            if (AITurnBudget.Expired || inspected++ >= 1024) break;
            if (action?.Unit == null || !TryGetSquad(action.Unit.ReflectionLifeId, out var squad)
                || !squadMembers.TryGetValue(action.Unit.ReflectionLifeId, out var member)
                || !SafeCoordinationActor(action.Unit, board) || member.Recovering) continue;
            var plan = FindActive(squad.OperationId);
            if (plan == null || !Running(plan)) continue;
            bool move = action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Surround;
            bool rotate = action.ActionType == AIActionType.Rotate && AIOrientation.IsObservedAttackPreparation(action, board);
            if (!move && !rotate || action.Unit.HasMovedThisTurn && move || action.APCost < 0 || action.APCost > board.EnemyAP) continue;
            bool assignedSlot = member.HasSlot && action.TargetPos.x == member.SlotX && action.TargetPos.z == member.SlotZ;
            if (move && (!assignedSlot && action.ActionType != AIActionType.Surround
                || !assignedSlot && squadSlots.Contains(GridHelper.ToGridXZ(action.TargetPos))
                || !board.IsTerrainKnown(action.TargetPos) || board.CalcMoveCost(action.Unit, action.TargetPos) != action.APCost
                || !board.GetValidMoves(action.Unit).Contains(action.TargetPos))) continue;
            foreach (var target in tacticalTargets)
            {
                if (target == null || !target.IsAlive || !NeutralFactionSystem.AreHostile(action.Unit, target)
                    || !board.IsSpecialActorObserved(target)
                    || target.ReflectionLifeId != plan.TargetLifeId
                        && AIOperationFeatureExtractor.Distance(target.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ)) > DefenseRadius
                    || LegalSquadTargets(action.Unit, board).Contains(target)) continue;
                var destination = move ? action.TargetPos : action.Unit.transform.position;
                var facing = rotate ? action.TargetDirection : action.Unit.direction;
                if (!ProspectiveStrike(action.Unit, destination, facing, target, board)
                    || (long)action.APCost + board.CoordinationAttackCost(action.Unit, move) > board.EnemyAP) continue;
                float risk = board.Governor?.IncomingDamage(board, action.Unit, destination, null)
                    ?? board.EstimateCounterDamageAt(destination, action.Unit);
                long remainingAP = board.EnemyAP - (long)action.APCost - board.CoordinationAttackCost(action.Unit, move);
                if (risk >= action.Unit.HP * .5f || move && !HasKnownReturnStep(action.Unit, destination, board, remainingAP)) continue;
                if (move && member.Role == AIOperationSquadRole.Ranged && AdjacentObservedHostile(destination, board)) continue;
                bool flank = move && HasOppositeSupportingActor(action.Unit, destination, target, squad, board);
                float progress = GridHelper.ChebyshevDistance(action.Unit.transform.position, target.transform.position)
                    - GridHelper.ChebyshevDistance(destination, target.transform.position);
                if (move && progress <= 0 && !flank) continue;
                float value = (IsVictoryTarget(target) ? 4 : 0) + (flank ? 2 : 0) + Mathf.Clamp(progress, 0, 2)
                    - risk / Mathf.Max(1, action.Unit.HP) * 4 - action.APCost * .05f;
                if (value <= best) continue;
                best = value; coordinationHead = action; coordinationTarget = target;
                coordinatedActions.Clear(); coordinatedActions.Add(action); coordinationSteps.Clear();
                coordinationOrigins.Clear(); coordinationOrigins.Add(action.Unit.transform.position);
                coordinationFacings.Clear(); coordinationFacings.Add(action.Unit.direction);
                coordinationSteps.Add(new AIOperationCoordinationStep { ActorLifeId = action.Unit.ReflectionLifeId,
                    TargetLifeId = target.ReflectionLifeId, ActionType = action.ActionType, APCost = action.APCost });
            }
        }
    }

    internal static bool ProspectiveStrike(Status actor, Vector3 position, Direction facing, Status target, AIBoardState board)
    {
        if (actor == null || target == null || board?.ReconMap == null || !board.IsSpecialActorObserved(target)
            || !SpecialSkillRuntime.CanNormalAttackTarget(actor, target) || AISpecialSkillKnowledge.SniperRestrictsTargets(actor)
            || AIActionEvaluator.IsObservedCompleteProtection(board, target)
            || !AttackPatterns.CanAttack(actor, facing, target.transform.position.x - position.x, target.transform.position.z - position.z)) return false;
        var map = board.ReconMap;
        return KnownTacticalLine(board, position, target.transform.position)
            && map.Environment.HasClearShots(position, target.transform.position)
            && (MapCreate.IsArcingAttack(actor.kind, actor.facilityKind) && GridHelper.ChebyshevDistance(position, target.transform.position) >= 2
                || map.HasClearTerrainLine(position, target.transform.position));
    }

    static bool HasKnownReturnStep(Status actor, Vector3 destination, AIBoardState board, long remainingAP)
    {
        // A geometric escape candidate, not a reserved/paid future move. The actual
        // return still goes through fresh AP, occupancy and vision validation.
        var origin = actor.transform.position;
        return board.CoordinationReturnCost(actor, destination, origin) <= remainingAP
            && board.IsTerrainKnown(origin) && MovePatterns.CanMove(actor, actor.direction, origin.x - destination.x, origin.z - destination.z)
            && KnownTacticalLine(board, destination, origin)
            && board.ReconMap.CanTraverse(destination, origin)
            && (board.Governor?.IncomingDamage(board, actor, origin, null) ?? board.EstimateCounterDamageAt(origin, actor)) < actor.HP;
    }
    static bool KnownTacticalLine(AIBoardState board, Vector3 from, Vector3 to)
    {
        int distance = AIOperationFeatureExtractor.Distance(from, to);
        if (distance > 16) return false;
        for (int step = 0; step <= distance; step++)
            if (!board.IsTerrainKnown(Vector3.Lerp(from, to, distance == 0 ? 0 : (float)step / distance))) return false;
        return true;
    }
    bool HasOppositeSupportingActor(Status actor, Vector3 destination, Status target, AIOperationSquad squad, AIBoardState board)
    {
        var side = destination - target.transform.position; side.y = 0;
        foreach (var member in squad.Members)
            if (member.ActorLifeId != actor.ReflectionLifeId && features.TryOwn(member.ActorLifeId, out var ally)
                && SafeCoordinationActor(ally, board) && LegalSquadTargets(ally, board).Contains(target))
            {
                var held = ally.transform.position - target.transform.position; held.y = 0;
                if (Vector3.Dot(side, held) < 0) return true;
            }
        return false;
    }

    /// <summary>Operation-targeted flanks, replacing fixed world offsets. Every returned
    /// cell is already a formal legal move, has an actual firing mask, friendly support,
    /// and enough shared AP for attack plus a known reverse escape candidate.</summary>
    public void AppendSurroundCandidates(Status actor, AIBoardState board, List<AIAction> output)
    {
        if (actor == null || output == null || board == null || actor.HasMovedThisTurn || AITurnBudget.Expired
            || !TryGetSquad(actor.ReflectionLifeId, out var squad) || !SafeCoordinationActor(actor, board)
            || !squadMembers.TryGetValue(actor.ReflectionLifeId, out var member) || member.Recovering) return;
        var plan = FindActive(squad.OperationId);
        if (plan == null || !Running(plan)) return;
        PrepareTacticalTargets(board);
        Status objective = null; float value = float.NegativeInfinity;
        foreach (var target in tacticalTargets)
        {
            if (target == null || !target.IsAlive || !board.IsSpecialActorObserved(target)
                || !NeutralFactionSystem.AreHostile(actor, target)) continue;
            int distance = AIOperationFeatureExtractor.Distance(target.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ));
            if (target.ReflectionLifeId != plan.TargetLifeId && distance > DefenseRadius) continue;
            float score = (target.ReflectionLifeId == plan.TargetLifeId ? 4 : 0) + (IsVictoryTarget(target) ? 4 : 0) - distance;
            if (score > value || score == value && objective != null && string.CompareOrdinal(target.ReflectionLifeId, objective.ReflectionLifeId) < 0)
            { objective = target; value = score; }
        }
        if (objective == null) return;
        int count = 0;
        foreach (var cell in board.GetValidMoves(actor))
        {
            if (AITurnBudget.Expired || count >= 2) break;
            bool assigned = member.HasSlot && cell.x == member.SlotX && cell.z == member.SlotZ;
            int cost = board.CalcMoveCost(actor, cell);
            if (!board.IsTerrainKnown(cell) || !assigned && squadSlots.Contains(GridHelper.ToGridXZ(cell))
                || cost < 0 || (long)cost + board.CoordinationAttackCost(actor, true) > board.EnemyAP
                || !ProspectiveStrike(actor, cell, actor.direction, objective, board)
                || member.Role == AIOperationSquadRole.Ranged && AdjacentObservedHostile(cell, board)
                || !HasKnownReturnStep(actor, cell, board, board.EnemyAP - (long)cost - board.CoordinationAttackCost(actor, true))
                || !HasOppositeSupportingActor(actor, cell, objective, squad, board)) continue;
            float risk = board.Governor?.IncomingDamage(board, actor, cell, null) ?? board.EstimateCounterDamageAt(cell, actor);
            if (risk >= actor.HP * .5f) continue;
            output.Add(new AIAction { ActionType = AIActionType.Surround, Unit = actor, TargetUnit = objective,
                TargetPos = cell, APCost = cost }); count++;
        }
    }

    bool CurrentCoordination(AIBoardState board)
    {
        if (coordinationHead == null || coordinationBoard != board || board == null || coordinationGeneration != board.Generation
            || board.EnemyAP != coordinationAP || coordinationTarget == null || !coordinationTarget.IsAlive
            || !board.IsSpecialActorObserved(coordinationTarget) || coordinationTarget.HP != coordinationHP
            || coordinationTarget.Barrier != coordinationBarrier || coordinationTarget.transform.position != coordinationTargetPosition || coordinatedActions.Count == 0
            || coordinatedActions.Count != coordinationSteps.Count || coordinatedActions.Count != coordinationOrigins.Count
            || coordinatedActions.Count != coordinationFacings.Count || coordinatedActions.Count > board.RemainingPlannedActionSlots) return false;
        long spent = 0; int index = 0; chainActors.Clear();
        foreach (var action in coordinatedActions)
        {
            if (action?.Unit == null || !action.Unit.IsAlive || action.Unit.team != board.ActorTeam
                || MatchObjectiveRules.IsCommander(action.Unit) || StatusEffectSystem.IsStunned(action.Unit)
                || !chainActors.Add(action.Unit.ReflectionLifeId) || !TryGetSquad(action.Unit.ReflectionLifeId, out _)
                || action.APCost < 0 || !string.IsNullOrEmpty(action.StrategicRejectReason)
                || action.Unit.transform.position != coordinationOrigins[index] || action.Unit.direction != coordinationFacings[index]) return false;
            index++;
            spent += action.APCost;
            if (spent > board.EnemyAP) return false;
            if (action.ActionType == AIActionType.Attack)
            { if (board.CalcAttackCost(action.Unit) != action.APCost || !LegalSquadTargets(action.Unit, board).Contains(coordinationTarget)) return false; }
            else if (action.ActionType == AIActionType.SkillUse)
            { if (!UsefulCoordinationDebuff(action, coordinationTarget) || board.CalcSkillCost(action.Unit, action.Skill) != action.APCost
                || !board.GetSkillTargets(action.Unit, action.Skill).Contains(coordinationTarget)) return false; }
            else if (action.ActionType == AIActionType.Rotate)
            { if (!AIOrientation.IsObservedAttackPreparation(action, board)) return false; }
            else if (!board.GetValidMoves(action.Unit).Contains(action.TargetPos) || board.CalcMoveCost(action.Unit, action.TargetPos) != action.APCost) return false;
        }
        return true;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class AIActionEvaluator
{
    // Evaluation scratch only: no transforms, HP, state stores, or turn clocks are mutated.
    // Three charge steps and eight linked bodies bound the work independently of map size.
    const int MaximumProjectedPushBodies = 8;
    static readonly Dictionary<Status, Vector3> projectedPushPositions = new Dictionary<Status, Vector3>();
    static readonly List<Status> projectedPushVictims = new List<Status>(MaximumProjectedPushBodies);
    static float projectedPushEntryValue;

    static bool IsPositionalAction(AIActionType type) => type == AIActionType.Move || type == AIActionType.Retreat
        || type == AIActionType.Support || type == AIActionType.Surround || type == AIActionType.DefenseRepos;

    public static float CalcNaturalRecoveryAdjustment(AIAction action, AIBoardState board)
    {
        var actor = action?.Unit; var recovery = actor?.NaturalRecovery;
        if (board == null || actor == null || actor.type != Type.Unit || actor.HP >= actor.MaxHP
            || recovery == null || !recovery.HasTurnBaseline || recovery.HpLostSinceLastOwnTurn
            || recovery.ConsecutiveSafeTurns < 3 || StatusEffectSystem.HasDebuff(actor, StatusEffectType.Poison)) return 0;
        bool moving = IsPositionalAction(action.ActionType);
        if (!moving && action.ActionType != AIActionType.Wait) return 0;
        Vector3 from = actor.transform.position, to = moving ? action.TargetPos : from;
        if (SpecialSkillRuntime.HasAssassinPassive(actor)
            && !board.HasOtherAllyVisionAt(actor, to)) return 0;
        int danger = ThreatAt(board, actor, to);
        if (danger > 0) return 0; // Imminent real HP loss breaks this plan; a null hit does not.
        int heal = Mathf.Min(actor.MaxHP - actor.HP, NaturalRecoverySystem.HealAmount(actor.MaxHP));
        int remaining = Mathf.Max(1, NaturalRecoverySystem.SafeTurnThreshold - recovery.ConsecutiveSafeTurns);
        float reward = SpecialValue(actor) * heal / Mathf.Max(1f, actor.MaxHP)
            * AISpecialSkillKnowledge.FutureWeight(board, remaining) / remaining;
        if (moving && ThreatAt(board, actor, from) > 0) reward += HpCost(actor, ThreatAt(board, actor, from)) * .25f;
        // A survival benefit participates in the existing ranking; it never forces a wait.
        action.PreserveSpecialEvaluation = true;
        return reward;
    }

    public static float CalcBarrierRecoveryAdjustment(AIAction action, AIBoardState board)
    {
        var actor = action?.Unit; var recovery = actor?.BarrierRecovery;
        var settings = BarrierCombatSettings.Active;
        if (board == null || actor == null || !BarrierSystem.UsesBarrier(actor) || actor.Barrier >= actor.MaxBarrier
            || recovery == null || recovery.RuleRevision != BarrierSystem.RecoveryRuleRevision || !recovery.FullHPObserved || recovery.SafeTurns < 2
            || !BarrierSystem.CanRegenerate(actor.HP, actor.MaxHP, 3)
            || settings.recoveryAtOwnerTurnStart && !recovery.HasOwnTurnBaseline
            || !settings.recoveryAtOwnerTurnStart && recovery.LastProcessedRound == board.TurnCount
            || StatusEffectSystem.HasDebuff(actor, StatusEffectType.Poison)) return 0;
        int safeTurns = recovery.SafeTurns; bool damaged = recovery.DamageSinceOwnTurn, hasRecovered = recovery.HasRecovered,
            fullHPObserved = recovery.FullHPObserved;
        int amount = Mathf.Min(actor.MaxBarrier-actor.Barrier,BarrierSystem.AdvanceRecovery(ref safeTurns,
            ref damaged,ref hasRecovered,ref fullHPObserved,actor.HP,actor.MaxHP,actor.MaxBarrier));
        if (amount <= 0) return 0;
        bool moving = IsPositionalAction(action.ActionType);
        if (!moving && action.ActionType != AIActionType.Wait) return 0;
        Vector3 destination = moving ? action.TargetPos : actor.transform.position;
        if (SpecialSkillRuntime.HasAssassinPassive(actor) && !board.HasOtherAllyVisionAt(actor, destination)) return 0;
        if (ThreatAt(board, actor, destination) > 0 || board.EstimateSpecialHazardAt(actor, destination) > 0
            || board.KnownEnvironmentDamage(actor, destination, false) > 0
            || moving && board.KnownEnvironmentDamage(actor, destination, true) > 0) return 0;
        // Only predicted HP loss breaks the full-HP condition. Barrier-only hits preserve its clock.
        foreach (var opponent in board.SpecialObservedActors)
        {
            if (opponent.team == actor.team || !NeutralFactionSystem.AreHostile(opponent, actor)) continue;
            var state = opponent.Special;
            if (state != null && (state.ritualActive && state.ritualTargetId == actor.ReflectionLifeId
                || state.sniperActive && state.sniperTargetId == actor.ReflectionLifeId
                || !string.IsNullOrEmpty(state.bombCasterId) && state.bombDueRound <= board.TurnCount + 1
                    && GridHelper.ChebyshevDistance(destination, opponent.transform.position) <= state.bombRadius)) return 0;
            if (StatusEffectSystem.IsStunned(opponent) || SpecialSkillRuntime.AttackBlocked(opponent)
                || !SpecialSkillRuntime.CanNormalAttackTarget(opponent, actor)) continue;
            float range = AIBoardQuery.EstimateAttackRange(opponent) + 1.5f;
            if ((destination - opponent.transform.position).sqrMagnitude <= range * range
                && SpecialSkillRuntime.PreviewHpDamage(actor, EstimateSpecialAwareAttack(opponent, actor, board)) > 0) return 0;
        }
        action.PreserveSpecialEvaluation = true;
        return SpecialValue(actor) * amount / Mathf.Max(1f, actor.MaxHP + (float)actor.MaxBarrier)
            * AISpecialSkillKnowledge.FutureWeight(board, 1);
    }

    static float PassivePositionValue(AIBoardState board, Status actor, Vector3 point)
    {
        var definition = SpecialSkillRuntime.Definition(actor);
        if (definition == null || !definition.enabledForPlay) return 0;
        float value = 0;
        if (definition.ability == UnitSpecialSkill.PriestPurification)
            foreach (var other in board.SpecialObservedActors)
            {
                if (!board.IsSpecialActorObserved(other) || GridHelper.ChebyshevDistance(point, other.transform.position) > 1) continue;
                int heal = Mathf.Min(other.MaxHP - other.HP, SpecialSkillRuntime.FractionOfMaximum(other, .05f));
                float benefit = SpecialValue(other) * Mathf.Max(0, heal) / Mathf.Max(1f, other.MaxHP);
                value += other.team == actor.team ? benefit : -benefit;
            }
        // Scout aura affects both sides. Do not infer state or free actions for unseen units.
        if (definition.ability == UnitSpecialSkill.ScoutVisionGuard
            && (definition.scoutFreeMoveScope != AbilityResetScope.Unconfigured || definition.scoutFreeAttackScope != AbilityResetScope.Unconfigured))
            foreach (var other in board.SpecialObservedActors)
            {
                if (other == actor || !board.IsSpecialActorObserved(other)) continue;
                var offset = GridHelper.ToGridXZ(other.transform.position - point);
                bool seen = false;
                foreach (var cell in VisionGenerator.BaseVisionOffsets(actor))
                    if (cell.x == offset.x && cell.z == offset.z) { seen = true; break; }
                if (!seen) continue;
                float usable = definition.scoutFreeAttackScope != AbilityResetScope.Unconfigured
                    && !(other.Special?.scoutFreeAttackUsed ?? false) ? APSystem.BaseAttackCost : 0;
                if (definition.scoutFreeMoveScope != AbilityResetScope.Unconfigured && !(other.Special?.scoutFreeMoveUsed ?? false)) usable += 1;
                value += (other.team == actor.team ? 1 : -1) * usable;
            }
        return value;
    }

    static bool ObservedCouldAttack(AIBoardState board, Status source, Vector3 destination, Status target)
    {
        if (source == null || target == null || !board.IsSpecialActorObserved(source)
            || !SpecialSkillRuntime.CanNormalAttackTarget(source, target) || StatusEffectSystem.IsStunned(source)
            || BoardActionProfile.For(source)?.CanAttack == false) return false;
        Vector3 difference = destination - source.transform.position;
        if (AttackPatterns.CanAttack(source, source.direction, difference.x, difference.z)) return true;
        return !SpecialSkillRuntime.MovementBlocked(source) && BoardActionProfile.For(source)?.CanMove != false
            && difference.sqrMagnitude <= Mathf.Pow(AIBoardQuery.EstimateAttackRange(source) + 1.5f, 2);
    }

    static float BestReachableAttackValue(AIBoardState board, Status actor)
    {
        float best = 0;
        var offsets = MovePatterns.Offsets(actor);
        int direction = MovePatterns.IsDirectionIndependent(actor) ? 1 : MovePatterns.DirZ(actor.direction);
        // Only one movement layer, with a deterministic cap rather than a nested minimax.
        int samples = Mathf.Min(16, offsets.Count);
        for (int i = 0; i < samples; i++)
        {
            Vector3 point = actor.transform.position + new Vector3(offsets[i].x, 0, offsets[i].y * direction);
            if (!board.IsTerrainKnown(point) || board.ReconMap == null
                || !board.ReconMap.CanTraverse(actor.transform.position, point)) continue;
            best = Mathf.Max(best, AttackValueAt(board, actor, point));
        }
        return best;
    }

    static bool SegmentsCrossXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float Cross(Vector3 x, Vector3 y, Vector3 z) => (y.x - x.x) * (z.z - x.z) - (y.z - x.z) * (z.x - x.x);
        float abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        if (abC == 0 && abD == 0)
            return Mathf.Max(Mathf.Min(a.x,b.x),Mathf.Min(c.x,d.x)) <= Mathf.Min(Mathf.Max(a.x,b.x),Mathf.Max(c.x,d.x))
                && Mathf.Max(Mathf.Min(a.z,b.z),Mathf.Min(c.z,d.z)) <= Mathf.Min(Mathf.Max(a.z,b.z),Mathf.Max(c.z,d.z));
        return abC * abD <= 0 && cdA * cdB <= 0;
    }

    static float FutureLineCrossingShare(AIBoardState board, Status actor, Vector3 a, Vector3 b)
    {
        if (actor.type != Type.Unit || BoardActionProfile.For(actor)?.CanMove == false
            || SpecialSkillRuntime.MovementBlocked(actor) || StatusEffectSystem.IsMovementBlocked(actor)) return 0;
        var offsets = MovePatterns.Offsets(actor);
        int direction = MovePatterns.IsDirectionIndependent(actor) ? 1 : MovePatterns.DirZ(actor.direction);
        int legal = 0, crossings = 0, cap = Mathf.Min(16, offsets.Count);
        for (int i = 0; i < cap; i++)
        {
            Vector3 next = actor.transform.position + new Vector3(offsets[i].x, 0, offsets[i].y * direction);
            if (board.ReconMap == null || !board.IsTerrainKnown(next)
                || !board.ReconMap.CanTraverse(actor.transform.position, next)
                || board.KnownEnvironmentMoveCost(actor.transform.position, next) >= GameConstants.MaxAP) continue;
            legal++;
            if (SegmentsCrossXZ(a, b, actor.transform.position, next)) crossings++;
        }
        return legal == 0 ? 0 : crossings / (float)legal;
    }

    static float KnownLineRouteValue(AIBoardState board, Vector3 a, Vector3 b)
    {
        int steps = Mathf.Min(64, Mathf.CeilToInt(GridHelper.ChebyshevDistance(a, b)));
        float value = 0;
        var previous = new Vector3Int(int.MinValue,0,int.MinValue);
        for (int i = 0; i <= steps; i++)
        {
            var point = Vector3.Lerp(a, b, steps == 0 ? 0 : i / (float)steps);
            var cell = GridHelper.ToGridXZ(point);
            if (cell == previous) continue;
            previous = cell;
            var known = board.KnownEnvironmentDefinition(point);
            if (known == null) continue;
            if (known.kind == TerrainEnvironmentKind.Road) value += 2;
            else if (known.kind == TerrainEnvironmentKind.Cave) value += 3;
        }
        return Mathf.Min(16, value); // Route leverage cannot dominate kills/survival or grow with map size.
    }

    // Shared by charge, pull, and swap: adding a new authored hazard needs no unit-specific combo rule.
    public static float KnownForcedEntryValue(AIBoardState board, Status actor, Vector3 from, Vector3 to)
    {
        if (board == null || actor == null || !board.IsSpecialActorObserved(actor)) return 0;
        var budget = AIObservedDamageBudget.Capture(actor);
        int hazardHp = board.EstimateSpecialHazardAt(actor,to,ref budget);
        int actual = (int)Math.Min(int.MaxValue,(long)hazardHp+EstimateObservedHitLoss(actor,board,
            board.KnownEnvironmentDamage(actor,to,true),HpChangeKind.IgnoreDefenceDamage,ref budget));
        float benefit = HpCost(actor, actual);
        // A staying hazard is a second packet, discounted because the actor can leave.
        int standing = EstimateObservedHitLoss(actor,board,board.KnownEnvironmentDamage(actor,to,false),HpChangeKind.IgnoreDefenceDamage,ref budget);
        benefit += HpCost(actor, standing) * .5f;
        benefit -= HpCost(actor, EstimateObservedHitLoss(actor,board,board.KnownEnvironmentDamage(actor,from,false),HpChangeKind.IgnoreDefenceDamage)) * .5f;
        int heal = board.KnownEnvironmentHeal(actor, to);
        benefit -= SpecialValue(actor) * Mathf.Min(Mathf.Max(0, actor.MaxHP - actor.HP), heal) / Mathf.Max(1f, actor.MaxHP);
        var known = board.KnownEnvironmentDefinition(to);
        if (known != null && (known.blocksVision || known.concealsOccupants))
            benefit += Mathf.Min(20, actor.VisionCell?.Count ?? 0) * .25f;
        // Positive means good for our side, including negative friendly fire.
        return actor.team == board.ActorTeam ? -benefit : benefit;
    }

    static Vector3 ProjectedPosition(Status actor)
        => projectedPushPositions.TryGetValue(actor, out var value) ? value : actor.transform.position;
    static Status ProjectedOccupant(AIBoardState board, Vector3 point, Status except)
    {
        var cell = GridHelper.ToGridXZ(point);
        foreach (var actor in board.SpecialObservedActors)
            if (actor != except && actor.IsAlive && GridHelper.ToGridXZ(ProjectedPosition(actor)) == cell) return actor;
        return null;
    }
    static bool ProjectPush(AIBoardState board, Status actor, Vector3 direction, int depth)
    {
        if (actor == null || actor.type != Type.Unit || actor.kind == Kind.Crystal || depth >= MaximumProjectedPushBodies) return false;
        Vector3 from = ProjectedPosition(actor), next = from + direction;
        if (board.ReconMap == null || !board.ReconMap.TryGetHeight(Mathf.RoundToInt(next.x), Mathf.RoundToInt(next.z), out float height)) return false;
        next.y = height;
        if (!board.ReconMap.CanTraverse(from, next)) return false;
        var blocker = ProjectedOccupant(board, next, actor);
        if (blocker != null && !ProjectPush(board, blocker, direction, depth + 1)) return false;
        projectedPushPositions[actor] = next;
        if (!projectedPushVictims.Contains(actor)) projectedPushVictims.Add(actor);
        projectedPushEntryValue += KnownForcedEntryValue(board, actor, from, next);
        return true;
    }
    static float KnightProjectionValue(AIBoardState board, Status knight, out Vector3 finalPosition)
    {
        projectedPushPositions.Clear(); projectedPushVictims.Clear(); projectedPushEntryValue = 0;
        finalPosition = knight.transform.position;
        try
        {
            Vector3 direction = new Vector3(0, 0, MovePatterns.DirZ(knight.direction));
            float score = 0;
            for (int step = 0; step < 3; step++)
            {
                Vector3 front = finalPosition + direction;
                if (board.ReconMap == null || !board.ReconMap.TryGetHeight(Mathf.RoundToInt(front.x), Mathf.RoundToInt(front.z), out float y)) break;
                front.y = y;
                if (!board.ReconMap.CanTraverse(finalPosition, front)) break;
                var victim = ProjectedOccupant(board, front, knight);
                // Runtime will revalidate all physical occupancy. Unseen actors cannot be scored here.
                if (victim != null && (victim.team == knight.team || !ProjectPush(board, victim, direction, 0))) break;
                score += KnownForcedEntryValue(board, knight, finalPosition, front);
                finalPosition = front;
            }
            foreach (var actor in projectedPushVictims)
            {
                Vector3 destination = ProjectedPosition(actor);
                if (actor.team != knight.team)
                {
                    // Charge pushes; it does not also execute an unpriced ordinary attack.
                    score += Mathf.Max(0, GridHelper.ChebyshevDistance(destination, board.EnemyCrystalPos)
                        - GridHelper.ChebyshevDistance(actor.transform.position, board.EnemyCrystalPos));
                }
            }
            return score + projectedPushEntryValue;
        }
        finally { projectedPushPositions.Clear(); projectedPushVictims.Clear(); projectedPushEntryValue = 0; }
    }
}

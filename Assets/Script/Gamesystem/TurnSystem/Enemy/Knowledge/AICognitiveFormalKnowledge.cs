using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Same-action proof refinement of the bounded retrieval result. Aggregate
/// generation facts never certify a different actor's plan. All later actions
/// remain provisional and must be regenerated after the first action executes.
/// </summary>
public static class AICognitiveFormalKnowledge
{
    static readonly AIStrategyPredicate[] predicates = (AIStrategyPredicate[])Enum.GetValues(typeof(AIStrategyPredicate));
    public static void MatchesForAction(AIObservedWorldModel world, AIAction action, AIBoardState board,
        AICognitiveActionAssessment proof, IReadOnlyList<AIKnowledgeMatch> retrieved, IList<AIKnowledgeMatch> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();
        if (world == null || board == null || action == null || retrieved == null || !world.IsCurrent(board)
            || !proof.IsFinite || !proof.Legal || !proof.HardSafe || proof.Confidence <= 0
            || action.APCost < 0 || action.APCost > board.EnemyAP || AITurnBudget.Expired) return;
        var context = AIKnowledgeContextBuilder.Build(world, new[] { action }, board.ReconThreatLevel, _ => proof);
        // The world may contain proofs made for another actor/target. Retain only
        // faction-global facts; relational claims are established again below.
        foreach (var predicate in predicates)
            if (!IsFactionGlobal(predicate)) context.Set(predicate, AITruthValue.Unknown, null);
        string evidence = "same-action:" + AICognitiveActionKey.For(action);
        var actor = action.Unit;
        bool ownActor = actor != null && actor.IsAlive && actor.team == board.ActorTeam && actor.gameObject.activeInHierarchy
            && HasCurrentValue(world.OwnUnits, actor.ReflectionLifeId, world.GlobalTurn);
        bool move = ownActor && IsMove(action.ActionType) && board.CalcMoveCost(actor, action.TargetPos) == action.APCost
            && board.GetValidMoves(actor).Contains(action.TargetPos);
        if (move)
        {
            Set(context, AIStrategyPredicate.LegalMoveAvailable, evidence, proof);
            RefineFiringPosition(context, world, action, board, proof, evidence);
            RefineRecovery(context, world, action, board, proof, evidence);
            if (actor.kind == Kind.Scout && proof.ExpectedInformationGain > 0)
            {
                // This assessment is supplied only for a concrete unresolved
                // route hypothesis by Commander, after AP/loss/opportunity cost.
                Set(context, AIStrategyPredicate.DecisionRelevantUnknown, evidence, proof);
                Set(context, AIStrategyPredicate.ScoutInformationGainPositive, evidence, proof);
            }
            if (proof.OpenedAlliedFireLine && proof.PreparationProven)
            {
                Set(context, AIStrategyPredicate.FiringLineOpened, evidence, proof);
                Set(context, AIStrategyPredicate.SharedAPAffordable, evidence, proof);
            }
        }
        if (ownActor && action.ActionType == AIActionType.Attack && action.TargetUnit != null
            && board.IsSpecialActorObserved(action.TargetUnit) && board.GetAttackTargets(actor).Contains(action.TargetUnit)
            && board.CalcAttackCost(actor) == action.APCost && proof.EffectiveDamage > 0)
        {
            Set(context, AIStrategyPredicate.LegalAttackAvailable, evidence, proof);
            Set(context, AIStrategyPredicate.ObservedTargetAvailable, evidence, proof);
            if (action.TargetUnit.kind == Kind.Crystal && proof.PredictedIncomingDamage < actor.HP)
            {
                // Governor's actual finite damage budget includes the existing
                // attacker-turn-end crystal counter. This is not a claim that
                // unobserved enemy responses are known or absent.
                Set(context, AIStrategyPredicate.CounterResponseKnown, evidence, proof);
                Set(context, AIStrategyPredicate.CounterLossSurvivable, evidence, proof);
            }
            if (proof.ProvenVictory)
            {
                Set(context, AIStrategyPredicate.WinningLegalSequence, evidence, proof);
                Set(context, AIStrategyPredicate.SharedAPAffordable, evidence, proof);
                Set(context, AIStrategyPredicate.CounterResponseKnown, evidence, proof);
            }
        }
        if (action.ActionType == AIActionType.Summon && board.Governor != null
            && board.Governor.RecruitmentBlockReason(action, board) == null)
        {
            var definition = action.SummonDefinition ?? board.ResolveUnitDefinition(action.SummonKind);
            if (definition != null && definition.costCitizen <= world.OwnEconomy.Citizen
                && context.Get(AIStrategyPredicate.EconomyForecastAvailable).State == AITruthValue.True)
            {
                // The existing official forecast/transaction consumes current
                // citizens. No house is treated as instant population.
                Set(context, AIStrategyPredicate.CitizenGrowthRespected, evidence, proof);
                string role = AIStrategicGovernor.RecruitmentRole(definition, action.SummonKind);
                if (role != null && CurrentTrue(world, "missing_role:" + role))
                {
                    Set(context, AIStrategyPredicate.OperationRoleMissing, evidence, proof);
                    Set(context, AIStrategyPredicate.AffordableRoleRecruitAvailable, evidence, proof);
                }
            }
        }
        var matcher = new AIStrategyKnowledgeMatcher();
        var effects = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < retrieved.Count && i < 12 && !AITurnBudget.Expired; i++)
        {
            var principle = retrieved[i]?.Principle; if (principle == null) continue;
            var match = matcher.Match(principle, context);
            if (match.State == AITruthValue.True && effects.Add(principle.EffectGroup)) result.Add(match);
        }
    }

    static void RefineFiringPosition(AIKnowledgeContext context, AIObservedWorldModel world, AIAction action,
        AIBoardState board, AICognitiveActionAssessment proof, string evidence)
    {
        var actor = action.Unit;
        if (actor == null || !board.IsTerrainKnown(action.TargetPos) || !TryAttackCostAfterMove(actor, board, out int attackCost)
            || (long)action.APCost + attackCost > board.EnemyAP) return;
        int checkedTargets = 0;
        foreach (var snapshot in world.ObservedOpponents)
        {
            if (++checkedTargets > 8 || AITurnBudget.Expired) break;
            var target = board.FindObservedSpecialActor(snapshot.LifeId);
            if (target == null || !board.IsSpecialActorObserved(target)
                || snapshot.Source != AIWorldFactSource.CurrentObservation || snapshot.ObservedTurn != world.GlobalTurn
                || !CanHitKnown(board, actor, action.TargetPos, target)
                || CanHitKnown(board, actor, actor.transform.position, target)) continue;
            Set(context, AIStrategyPredicate.ActivityImproves, evidence, proof);
            Set(context, AIStrategyPredicate.LegalFiringPositionAvailable, evidence, proof);
            Set(context, AIStrategyPredicate.HeightAndLineOfSightVerified, evidence, proof);
            // Same observed target, legal firing positions and an AP reserve for
            // both participants establish actual mutual attack support.
            int allies = 0;
            foreach (var ally in board.AliveEnemyUnits)
            {
                if (++allies > 8 || AITurnBudget.Expired) break;
                if (ally == null || ally == actor || !ally.IsAlive || ally.team != board.ActorTeam
                    || !HasCurrentValue(world.OwnUnits, ally.ReflectionLifeId, world.GlobalTurn)
                    || (long)action.APCost + attackCost + board.CalcAttackCost(ally) > board.EnemyAP
                    || !CanHitKnown(board, ally, ally.transform.position, target)) continue;
                context.Set(AIStrategyPredicate.IsolationRisk, AITruthValue.False, evidence, proof.Confidence);
                Set(context, AIStrategyPredicate.MutualSupportAvailable, evidence, proof);
                break;
            }
            return;
        }
    }

    static void RefineRecovery(AIKnowledgeContext context, AIObservedWorldModel world, AIAction action,
        AIBoardState board, AICognitiveActionAssessment proof, string evidence)
    {
        if (action.ActionType != AIActionType.Retreat || board.Governor == null || MatchObjectiveRules.IsCommander(action.Unit)
            || board.Governor.Mode == StrategicMode.EmergencyDefense || !AIUnitPreservationConfig.Active.Enabled) return;
        var preservation = board.Governor.AssessPreservation(action.Unit, board);
        if (!preservation.HasLegalSaferMove || !preservation.HealingPossible
            || !GridHelper.MatchXZ(action.TargetPos, GridHelper.ToGridXZ(preservation.Refuge))
            || proof.PredictedIncomingDamage >= preservation.IncomingCurrent
            || !ExistingRearSafe(world, board)) return;
        if (board.Operations == null || !board.Operations.TryGetSquad(action.Unit.ReflectionLifeId, out var squad)) return;
        bool operationRetained = false;
        foreach (var operation in world.OngoingOperations)
            if (operation.OperationId == squad.OperationId && operation.HasPosition
                && (operation.Status == AIOperationStatus.Active || operation.Status == AIOperationStatus.Preparing))
            { operationRetained = true; break; }
        if (!operationRetained) return;
        Set(context, AIStrategyPredicate.LegalDisengageAvailable, evidence, proof);
        Set(context, AIStrategyPredicate.RearDefenseSafe, evidence, proof);
        // Existing squad recovery retains the assignment and rejoins at the
        // existing configured ratio; R4 adds no new healing or return rule.
        Set(context, AIStrategyPredicate.ResumeConditionDefined, evidence + "/existing-squad-rejoin-ratio", proof);
    }
    static bool ExistingRearSafe(AIObservedWorldModel world, AIBoardState board)
    {
        bool checkedObjective = false;
        foreach (var value in world.OwnUnits)
        {
            if (!value.IsCommander && !value.IsCrystal) continue;
            var actor = board.FindObservedSpecialActor(value.LifeId);
            if (actor == null || actor.team != board.ActorTeam || !actor.IsAlive) return false;
            float risk = board.Governor.IncomingDamage(board, actor, actor.transform.position, null);
            if (!AIWorldFact.Finite(risk) || risk >= actor.HP) return false;
            checkedObjective = true;
        }
        return checkedObjective;
    }
    // Uses the existing official attack-cost query and the exact formal Move
    // fatigue increment. Dynamic HP/attack-count cost overrides are excluded.
    // This is a conservative planning upper bound, not a paid future action.
    static bool TryAttackCostAfterMove(Status actor, AIBoardState board, out int cost)
    {
        cost = 0;
        var definition = SpecialSkillRuntime.Definition(actor);
        if (definition != null && definition.enabledForPlay && (definition.ability == UnitSpecialSkill.BerserkerRage
            || definition.ability == UnitSpecialSkill.GuardianLifeLink)) return false;
        int current = board.CalcAttackCost(actor);
        if (current < 0) return false;
        long increment = Math.Max(0L, 1L + GameConstants.GetExtraFatiguePerAction(actor.kind));
        cost = current == 0 ? 0 : (int)Math.Min(int.MaxValue, current + increment);
        return true;
    }
    static bool CanHitKnown(AIBoardState board, Status actor, Vector3 origin, Status target)
    {
        if (actor == null || target == null || !board.IsSpecialActorObserved(target)
            || !SpecialSkillRuntime.CanNormalAttackTarget(actor, target)) return false;
        var destination = target.transform.position;
        var map = board.ReconMap;
        if (map == null || !KnownCorridor(board, origin, destination)
            || !AttackPatterns.CanAttack(actor, actor.direction, destination.x - origin.x, destination.z - origin.z)) return false;
        // Same terrain/environment calls and arc rule as MapCreate's official
        // CanAttackAcrossTerrain, with a value origin instead of moving a Transform.
        return map.Environment.HasClearShots(origin, destination)
            && (MapCreate.IsArcingAttack(actor.kind, actor.facilityKind) && GridHelper.ChebyshevDistance(origin, destination) >= 2
                || map.HasClearTerrainLine(origin, destination));
    }
    static bool KnownCorridor(AIBoardState board, Vector3 from, Vector3 to)
    {
        int x = Mathf.RoundToInt(from.x), z = Mathf.RoundToInt(from.z), tx = Mathf.RoundToInt(to.x), tz = Mathf.RoundToInt(to.z);
        int dx = Math.Abs(tx - x), dz = Math.Abs(tz - z), sx = Math.Sign(tx - x), sz = Math.Sign(tz - z), ix = 0, iz = 0;
        if (dx > 39 || dz > 39 || !board.IsTerrainKnown(new Vector3(x, 0, z))) return false;
        while (ix < dx || iz < dz)
        {
            int decision = (1 + 2 * ix) * dz - (1 + 2 * iz) * dx;
            if (decision == 0)
            {
                if (!board.IsTerrainKnown(new Vector3(x + sx, 0, z)) || !board.IsTerrainKnown(new Vector3(x, 0, z + sz))) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (!board.IsTerrainKnown(new Vector3(x, 0, z))) return false;
        }
        return true;
    }
    static bool HasCurrentValue(IReadOnlyList<AIObservedUnitValue> values, string id, int turn)
    { foreach (var value in values) if (value.LifeId == id && value.Source == AIWorldFactSource.CurrentObservation && value.ObservedTurn == turn) return true; return false; }
    static bool CurrentTrue(AIObservedWorldModel world, string id)
        => world.TryFact(id, out var fact) && fact.Source == AIWorldFactSource.CurrentObservation && fact.ObservedTurn == world.GlobalTurn && fact.Truth == AITruthValue.True;
    static bool IsMove(AIActionType type) => type == AIActionType.Move || type == AIActionType.Retreat
        || type == AIActionType.Support || type == AIActionType.Surround || type == AIActionType.DefenseRepos;
    static bool IsFactionGlobal(AIStrategyPredicate predicate)
        => predicate == AIStrategyPredicate.EconomyForecastAvailable || predicate == AIStrategyPredicate.SupplyForecastAvailable
            || predicate == AIStrategyPredicate.OperationRoleMissing || predicate == AIStrategyPredicate.ProductionMismatch
            || predicate == AIStrategyPredicate.StableObjectiveAvailable || predicate == AIStrategyPredicate.ObservedTargetAvailable;
    static void Set(AIKnowledgeContext context, AIStrategyPredicate predicate, string evidence, AICognitiveActionAssessment proof)
        => context.Set(predicate, AITruthValue.True, evidence, proof.Confidence);
}

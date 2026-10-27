using System;
using System.Collections.Generic;

/// <summary>Formal current evidence only; unsupported tactical predicates remain Unknown.</summary>
public static class AIKnowledgeContextBuilder
{
    public static AIKnowledgeContext Build(AIObservedWorldModel world, IReadOnlyList<AIAction> actions,
        int threatLevel, Func<AIAction, AICognitiveActionAssessment> assess = null,
        AIOperationGoal goal = AIOperationGoal.None, int maxActions = 64)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        var result = new AIKnowledgeContext(world.BattleId, world.ActorTeam, world.OwnTurn,
            world.BoardGeneration, goal, threatLevel);
        // Exact named proofs can be supplied by formal bounded queries. Broad
        // summaries (e.g. uncertainty) do not prove a specific tactical claim.
        foreach (var fact in world.Facts)
            if (fact.Source == AIWorldFactSource.CurrentObservation && fact.ObservedTurn == world.GlobalTurn
                && Enum.TryParse(fact.Id, false, out AIStrategyPredicate predicate)
                && Enum.IsDefined(typeof(AIStrategyPredicate), predicate))
                result.Set(predicate, fact.Truth, fact.Id, fact.Confidence);
        bool visibleTarget = false;
        foreach (var target in world.ObservedOpponents)
            if (target.Source == AIWorldFactSource.CurrentObservation && target.ObservedTurn == world.GlobalTurn
                && target.HP > 0 && target.Team != world.ActorTeam) { visibleTarget = true; break; }
        if (visibleTarget)
            Prove(result, AIStrategyPredicate.ObservedTargetAvailable, world, "visible_opponents");
        else if (!world.Truncated)
            Refute(result, AIStrategyPredicate.ObservedTargetAvailable, world, "visible_opponents");
        if (world.Objectives.Count > 0)
            Prove(result, AIStrategyPredicate.StableObjectiveAvailable, world, "formal-objective");
        Import(result, world, "role_shortage", AIStrategyPredicate.OperationRoleMissing);
        if (ValidForecast(world.OwnEconomy))
        {
            Prove(result, AIStrategyPredicate.EconomyForecastAvailable, world, "own-official-forecast");
            Prove(result, AIStrategyPredicate.SupplyForecastAvailable, world, "own-official-forecast");
            bool productionDeficit = false;
            foreach (var resource in world.OwnEconomy.Forecast)
                if (resource.ProductionDeficit > 0 && resource.PlannedDemandPerTurn > 0) { productionDeficit = true; break; }
            if (productionDeficit && result.Get(AIStrategyPredicate.OperationRoleMissing).State == AITruthValue.True)
                Prove(result, AIStrategyPredicate.ProductionMismatch, world, "own-planned-role-production-deficit");
            if (world.OwnEconomy.HasMandatoryPaymentFailure && HasExpedition(world))
                Prove(result, AIStrategyPredicate.ExpeditionSupplyRisk, world, "own-expedition-upkeep-failure");
        }
        if (actions == null || assess == null) return result;
        var own = new Dictionary<string, AIObservedUnitValue>(StringComparer.Ordinal);
        foreach (var unit in world.OwnUnits)
            if (!string.IsNullOrEmpty(unit.LifeId) && unit.Team == world.ActorTeam
                && unit.Source == AIWorldFactSource.CurrentObservation && unit.ObservedTurn == world.GlobalTurn) own[unit.LifeId] = unit;
        var opponents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in world.ObservedOpponents)
            if (unit.Source == AIWorldFactSource.CurrentObservation && unit.ObservedTurn == world.GlobalTurn
                && unit.Team != world.ActorTeam && unit.HP > 0) opponents.Add(unit.LifeId);
        bool build = false, summon = false;
        int limit = Math.Max(0, Math.Min(128, maxActions));
        for (int i = 0; i < actions.Count && i < limit; i++)
        {
            if (AITurnBudget.Expired) break;
            var action = actions[i]; if (action == null) continue;
            var evidence = assess(action);
            if (!evidence.IsFinite || !evidence.Legal || !evidence.HardSafe || evidence.Confidence <= 0
                || action.APCost < 0 || action.APCost > world.OwnAP) continue;
            // Callback verifies formal legality and visibility before stable
            // identities of the live current candidates are used below.
            bool ownActor = action.Unit != null && own.TryGetValue(action.Unit.ReflectionLifeId, out var actor)
                && actor.HP > 0;
            bool seenTarget = action.TargetUnit != null && opponents.Contains(action.TargetUnit.ReflectionLifeId);
            bool move = ownActor && IsMove(action.ActionType);
            bool attack = ownActor && seenTarget && (action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.SkillUse);
            string key = "formal-action:" + AICognitiveActionKey.For(action);
            if (move)
            {
                result.Set(AIStrategyPredicate.LegalMoveAvailable, AITruthValue.True, key, evidence.Confidence);
                if (action.ActionType == AIActionType.Retreat)
                    result.Set(AIStrategyPredicate.LegalDisengageAvailable, AITruthValue.True, key, evidence.Confidence);
            }
            if (attack)
                result.Set(AIStrategyPredicate.LegalAttackAvailable, AITruthValue.True, key, evidence.Confidence);
            if (evidence.ProvenVictory && attack)
                result.Set(AIStrategyPredicate.WinningLegalSequence, AITruthValue.True, key, evidence.Confidence);
            // A proof of one affordable action is never used to prove
            // SharedAPAffordable, an opened move/attack line, or a flank route.
            build |= action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade;
            summon |= action.ActionType == AIActionType.Summon;
            if (action.ActionType == AIActionType.Summon && action.SummonDefinition != null)
            {
                string role = AIStrategicGovernor.RecruitmentRole(action.SummonDefinition, action.SummonKind);
                if (role != null && CurrentTrue(world, "missing_role:" + role))
                    result.Set(AIStrategyPredicate.AffordableRoleRecruitAvailable, AITruthValue.True, key, evidence.Confidence);
            }
        }
        if (build && summon) Prove(result, AIStrategyPredicate.CompetingInvestmentAvailable, world, "formal-build-and-summon-alternatives");
        // An absent candidate in a truncated/budget-cut scan does not refute
        // movement or attack availability. The corresponding fact stays Unknown.
        return result;
    }
    static bool IsMove(AIActionType action)
        => action == AIActionType.Move || action == AIActionType.Retreat || action == AIActionType.Support
            || action == AIActionType.Surround || action == AIActionType.DefenseRepos;
    static bool HasExpedition(AIObservedWorldModel world)
    {
        foreach (var operation in world.OngoingOperations)
            if ((operation.Status == AIOperationStatus.Active || operation.Status == AIOperationStatus.Preparing)
                && (operation.Goal == AIOperationGoal.DestroyEnemyCrystal || operation.Goal == AIOperationGoal.BreakEnemyDefense
                    || operation.Goal == AIOperationGoal.FlankEnemyForce || operation.Goal == AIOperationGoal.SurroundEnemyForce)) return true;
        return false;
    }
    static bool ValidForecast(AIWorldEconomyValue economy)
    {
        if (!economy.Known || economy.Forecast == null || economy.Forecast.Count == 0
            || !Enum.IsDefined(typeof(EconomicState), economy.State)) return false;
        foreach (var value in economy.Forecast)
            if (!AIWorldFact.Finite(value.ProductionPerTurn) || !AIWorldFact.Finite(value.MandatoryDemandPerTurn)
                || !AIWorldFact.Finite(value.PlannedDemandPerTurn) || !AIWorldFact.Finite(value.ProjectedStock)
                || !AIWorldFact.Finite(value.ProductionDeficit)) return false;
        return true;
    }
    static bool CurrentTrue(AIObservedWorldModel world, string id)
        => world.TryFact(id, out var fact) && fact.Source == AIWorldFactSource.CurrentObservation
            && fact.ObservedTurn == world.GlobalTurn && fact.Truth == AITruthValue.True && fact.Confidence > 0;
    static void Import(AIKnowledgeContext context, AIObservedWorldModel world, string id, AIStrategyPredicate predicate)
    {
        if (world.TryFact(id, out var fact) && fact.Source == AIWorldFactSource.CurrentObservation && fact.ObservedTurn == world.GlobalTurn)
            context.Set(predicate, fact.Truth, fact.Id, fact.Confidence);
    }
    static void Prove(AIKnowledgeContext context, AIStrategyPredicate predicate, AIObservedWorldModel world, string source)
        => context.Set(predicate, AITruthValue.True, world.BattleId + "/" + world.BoardGeneration + "/" + source);
    static void Refute(AIKnowledgeContext context, AIStrategyPredicate predicate, AIObservedWorldModel world, string source)
        => context.Set(predicate, AITruthValue.False, world.BattleId + "/" + world.BoardGeneration + "/" + source);
}

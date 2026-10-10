using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the observed board to value facts. Live objects are cached for one board generation only;
/// neither hidden targets nor the map's undiscovered cells are inspected.
/// </summary>
public sealed class AIOperationFeatureExtractor
{
    readonly AIOperationConfig config;
    readonly List<Status> ownBuildings = new List<Status>();
    readonly Dictionary<string, Status> own = new Dictionary<string, Status>();
    readonly Dictionary<string, Status> observed = new Dictionary<string, Status>();
    readonly Dictionary<string, AIOperationObservedUnit> responseIndex = new Dictionary<string, AIOperationObservedUnit>();
    readonly HashSet<string> initialDefenders = new HashSet<string>();
    AIBoardState cachedBoard;
    int generation = -1;
    AIOperationContext boardFacts;

    public AIOperationFeatureExtractor(AIOperationConfig config)
        => this.config = config != null ? config : AIOperationConfig.Active;

    public void Invalidate() { cachedBoard = null; generation = -1; }

    public void Prepare(AIBoardState board, int ownTurn)
    {
        if (board == null) return;
        if (cachedBoard == board && generation == board.Generation)
        { boardFacts.Turn = ownTurn; return; }
        cachedBoard = board; generation = board.Generation;
        own.Clear(); observed.Clear(); ownBuildings.Clear();
        boardFacts = new AIOperationContext { Turn = ownTurn,
            OwnCrystalHpRatio = Mathf.Clamp01((float)board.EnemyCrystalHP / Mathf.Max(1, board.EnemyCrystalMaxHP)),
            OwnCrystalDestroyed = board.EnemyCrystalMaxHP > 0 && board.EnemyCrystalHP <= 0,
            TerritoryCount = board.OwnTerritoryCount, ExploredTiles = board.OwnExploredCells.Count };
        board.Governor?.Evaluate(board);
        boardFacts.OwnCrystalThreatened = board.Governor?.Mode == StrategicMode.EmergencyDefense;
        var production = board.ProductionDemand;
        boardFacts.EconomyState = production?.State ?? EconomicState.Healthy;
        if (production != null)
            foreach (var resource in production.Resources)
                boardFacts.EconomyProductionValue += Mathf.Max(0, resource.ProductionPerTurn);
        var resources = board.EnemyResources;
        if (resources != null)
        {
            boardFacts.Wood = resources.Wood; boardFacts.Stone = resources.Stone;
            boardFacts.Iron = resources.Iron; boardFacts.Bread = resources.Bread;
            boardFacts.Water = resources.Water; boardFacts.Wheat = resources.Wheat;
            boardFacts.MagicOre = resources.MagicOre; boardFacts.Citizen = resources.Citizen;
        }
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.IsAlive || unit.team != board.ActorTeam) continue;
            AddOwn(unit);
            if (unit.type != Type.Unit) continue;
            boardFacts.OwnUnits++; boardFacts.OwnMilitaryPower += MilitaryPower(unit);
            if (boardFacts.OwnUnitLifeIds.Count < config.AssignmentLimit * 8)
                boardFacts.OwnUnitLifeIds.Add(unit.ReflectionLifeId);
        }
        board.CollectOwnBuildings(ownBuildings);
        foreach (var building in ownBuildings)
            if (building != null && building.IsAlive && building.team == board.ActorTeam) AddOwn(building);
        var crystal = board.OwnCrystalParent?.GetComponentInChildren<Status>();
        if (crystal != null && crystal.team == board.ActorTeam) AddOwn(crystal);
        foreach (var unit in board.AlivePlayerUnits)
        {
            // Visibility is checked before reading position-derived combat, health or identity facts.
            if (unit == null || !board.IsVisibleToEnemy(unit.transform.position) || !unit.IsAlive) continue;
            string life = unit.ReflectionLifeId;
            if (observed.Count >= config.EventLimit) break;
            observed[life] = unit;
            boardFacts.VisibleEnemyLifeIds.Add(life);
            var point = unit.transform.position;
            boardFacts.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = life,
                X = Mathf.RoundToInt(point.x), Z = Mathf.RoundToInt(point.z),
                Power = unit.type == Type.Unit ? MilitaryPower(unit) : 0, Category = Category(unit) });
            if (unit.type == Type.Unit)
            { boardFacts.VisibleEnemyUnits++; boardFacts.EnemyObservedPower += MilitaryPower(unit); }
            else boardFacts.KnownImportantFacilities++;
        }
        boardFacts.EnemyStrengthKnown = boardFacts.VisibleEnemyUnits > 0;
        boardFacts.ForecastRisk = boardFacts.EnemyStrengthKnown
            ? Mathf.Clamp01(boardFacts.EnemyObservedPower / Mathf.Max(1, boardFacts.OwnMilitaryPower)
                * AIOperationConfig.Unit(config.ObservedPowerRiskScale))
            : AIOperationConfig.Unit(config.UnknownOperationRisk);
        foreach (var contact in board.Belief.Contacts)
        {
            boardFacts.BeliefUncertainty += contact.UnknownProbability;
            foreach (float probability in contact.Cells.Values)
                boardFacts.BeliefUncertainty += probability * (1 - probability);
        }
    }

    void AddOwn(Status unit)
    {
        if (own.Count < config.EventLimit) own[unit.ReflectionLifeId] = unit;
    }

    public IEnumerable<Status> OwnUnits(AIBoardState board, int ownTurn)
    { Prepare(board, ownTurn); return own.Values; }
    public IEnumerable<Status> ObservedUnits(AIBoardState board, int ownTurn)
    { Prepare(board, ownTurn); return observed.Values; }
    public bool TryOwn(string life, out Status unit)
    { unit = null; return !string.IsNullOrEmpty(life) && own.TryGetValue(life, out unit); }
    public bool TryObserved(string life, out Status unit)
    { unit = null; return !string.IsNullOrEmpty(life) && observed.TryGetValue(life, out unit); }

    public AIOperationContext Capture(AIBoardState board, AIOperationPlan plan, int ownTurn)
    {
        if (board == null) return plan?.CurrentContext?.Copy() ?? new AIOperationContext { Turn = ownTurn };
        Prepare(board, ownTurn);
        var facts = boardFacts.Copy();
        if (plan == null) return facts;
        var previous = plan.CurrentContext;
        if (previous != null)
        {
            facts.NewEnemyContacts = previous.NewEnemyContacts;
            facts.ArtifactCount = previous.ArtifactCount;
            facts.ConfirmedArtifactAcquisitions = previous.ConfirmedArtifactAcquisitions;
            facts.ConfirmedDungeonCleared = previous.ConfirmedDungeonCleared;
            facts.OwnLosses = previous.OwnLosses; facts.EnemyKills = previous.EnemyKills;
            facts.OwnLossPower = previous.OwnLossPower; facts.EnemyKilledPower = previous.EnemyKilledPower;
            facts.ConfirmedTargetDestroyed = previous.ConfirmedTargetDestroyed;
            facts.TargetForceConfirmedEliminated = previous.TargetForceConfirmedEliminated;
            facts.DiversionConfirmed = previous.DiversionConfirmed; facts.RouteOpened = previous.RouteOpened;
            facts.FlankConfirmed = previous.FlankConfirmed; facts.SurroundConfirmed = previous.SurroundConfirmed;
            facts.EnemyRetreatConfirmed = previous.EnemyRetreatConfirmed; facts.EnemyDelayConfirmed = previous.EnemyDelayConfirmed;
            facts.ObjectiveProgress = previous.ObjectiveProgress; facts.PreparationContribution = previous.PreparationContribution;
            facts.ExploitationProgress = previous.ExploitationProgress; facts.ResourceInvestment = previous.ResourceInvestment;
            facts.ResourceSpendValue = previous.ResourceSpendValue; facts.ApSpent = previous.ApSpent;
        }
        facts.AssignedUnitCount = plan.OriginalAssignedUnitLifeIds.Count;
        facts.UtilizedAssignedUnitCount = plan.UtilizedUnitLifeIds.Count;
        facts.ArmyUtilization = (float)facts.UtilizedAssignedUnitCount / Mathf.Max(1, facts.AssignedUnitCount);
        facts.ForecastRisk = plan.StartContext?.ForecastRisk ?? boardFacts.ForecastRisk;
        if (plan.HasTargetPosition)
        {
            Vector3 target = new Vector3(plan.TargetX, 0, plan.TargetZ);
            facts.ObjectiveKnown = !string.IsNullOrEmpty(plan.TargetLifeId) || board.IsTerrainKnown(target);
            facts.ObjectiveLifeId = plan.TargetLifeId;
            Status actualTarget = null;
            bool isOwn = TryOwn(plan.TargetLifeId, out actualTarget);
            if (!isOwn) TryObserved(plan.TargetLifeId, out actualTarget);
            if (actualTarget != null)
            {
                facts.TargetObserved = true; facts.ObjectiveHpKnown = true;
                facts.ObjectiveHpRatio = Mathf.Clamp01((float)Mathf.Max(0, actualTarget.HP) / Mathf.Max(1, actualTarget.MaxHP));
                // Updating a moving target is permitted only while it is in this observed index.
                plan.TargetX = Mathf.RoundToInt(actualTarget.transform.position.x);
                plan.TargetZ = Mathf.RoundToInt(actualTarget.transform.position.z);
                target = actualTarget.transform.position;
            }
            else if (facts.ConfirmedTargetDestroyed)
            { facts.ObjectiveHpKnown = true; facts.ObjectiveHpRatio = 0; }
            int nearest = int.MaxValue;
            foreach (string id in plan.AssignedUnitLifeIds)
                if (TryOwn(id, out var actor) && actor != null && actor.IsAlive)
                    nearest = Mathf.Min(nearest, Distance(actor.transform.position, target));
            facts.ObjectiveDistance = nearest == int.MaxValue ? -1 : nearest;
        }
        if (plan.StartContext != null)
        {
            int missing = 0;
            foreach (string life in plan.StartContext.OwnUnitLifeIds) if (!own.ContainsKey(life)) missing++;
            facts.OwnLosses = Mathf.Max(facts.OwnLosses, missing);
            ObserveEnemyResponse(plan, facts);
        }
        if (plan.HasTargetPosition && (plan.PrimaryGoal == AIOperationGoal.DefendOwnCrystal
            || plan.PrimaryGoal == AIOperationGoal.DefendSubCrystal))
        {
            var objective = new Vector3(plan.TargetX, 0, plan.TargetZ);
            foreach (var enemy in facts.ObservedEnemies)
                if (TryObserved(enemy.LifeId, out var actor))
                {
                    var position = actor.transform.position;
                    enemy.ThreatensObjective = AttackPatterns.CanAttack(actor, actor.direction,
                        objective.x - position.x, objective.z - position.z);
                    facts.ObjectiveThreatened |= enemy.ThreatensObjective;
                }
        }
        return facts;
    }

    // A moved or invisible contact is not a kill. Only two currently observed identities that
    // moved away from the defended objective can establish a diversion response.
    void ObserveEnemyResponse(AIOperationPlan plan, AIOperationContext facts)
    {
        if (!plan.HasTargetPosition || plan.StartContext.ObservedEnemies == null) return;
        responseIndex.Clear(); initialDefenders.Clear();
        foreach (var current in facts.ObservedEnemies) responseIndex[current.LifeId] = current;
        int diverted = 0; float defenseBefore = 0, defenseNow = 0;
        var target = new Vector3(plan.TargetX, 0, plan.TargetZ);
        int defenseRadius = Mathf.Max(1, config.ObjectiveDefenseRadius);
        foreach (var previous in plan.StartContext.ObservedEnemies)
        {
            int beforeDistance = Distance(new Vector3(previous.X, 0, previous.Z), target);
            if (previous.Power <= 0 || beforeDistance > defenseRadius) continue;
            initialDefenders.Add(previous.LifeId);
            defenseBefore += previous.Power;
            // An unobserved defender remains a possible defender. Its disappearance alone must
            // not imply that a road opened or that the defender died.
            if (responseIndex.TryGetValue(previous.LifeId, out var current))
            {
                int nowDistance = Distance(new Vector3(current.X, 0, current.Z), target);
                if (nowDistance >= beforeDistance + Mathf.Max(1, config.DiversionMinimumDistanceGain)) diverted++;
                if (nowDistance <= defenseRadius) defenseNow += current.Power;
            }
            else defenseNow += previous.Power;
        }
        // A new observed reinforcement closes an opening even when the original defenders moved away.
        foreach (var current in facts.ObservedEnemies)
            if (!initialDefenders.Contains(current.LifeId) && current.Power > 0
                && Distance(new Vector3(current.X, 0, current.Z), target) <= defenseRadius) defenseNow += current.Power;
        if (diverted >= Mathf.Max(1, config.DiversionMinimumEnemies))
        {
            facts.DiversionConfirmed = true;
            // Require verified movement, rather than disappearing contacts, to infer an opening.
            facts.RouteOpened |= defenseBefore > 0 && defenseNow <= defenseBefore * .5f;
        }
    }

    public static string ContextKey(AIOperationPlan plan, AIOperationContext facts)
    {
        float ratio = facts.EnemyStrengthKnown ? facts.OwnMilitaryPower / Mathf.Max(1, facts.EnemyObservedPower) : 0;
        string power = !facts.EnemyStrengthKnown ? "Unknown" : ratio < .85f ? "Inferior" : ratio > 1.2f ? "Advantage" : "Even";
        string distance = facts.ObjectiveDistance < 0 ? "Unknown" : facts.ObjectiveDistance <= 3 ? "Near"
            : facts.ObjectiveDistance <= 8 ? "Medium" : "Far";
        string phase = facts.Turn <= 10 ? "Early" : facts.Turn <= 30 ? "Mid" : "Late";
        string intel = !facts.ObjectiveKnown ? "Unknown" : facts.TargetObserved ? "High" : "Low";
        string defense = !facts.EnemyStrengthKnown ? "Unknown" : facts.EnemyObservedPower <= 40 ? "Low"
            : facts.EnemyObservedPower <= 100 ? "Medium" : "High";
        return "goal=" + plan.PrimaryGoal + "|phase=" + phase + "|power=" + power
            + "|economy=" + facts.EconomyState + "|enemyDefense=" + defense + "|intel=" + intel
            + "|distance=" + distance + "|army=" + (facts.OwnUnits < 4 ? "Small" : "Mixed");
    }

    public static string Category(Status unit) => unit == null ? "None" : unit.kind == Kind.Crystal ? "Crystal"
        : unit.kind == Kind.SubCrystal ? "SubCrystal" : unit.type == Type.Unit ? AIActionFeatureExtractor.Role(unit.kind) : "Building";
    public static float MilitaryPower(Status unit) => unit == null ? 0
        : (Mathf.Max(0, unit.ATK) + Mathf.Max(0, unit.DEF) * .5f) * Mathf.Clamp01((float)unit.HP / Mathf.Max(1, unit.MaxHP));
    public static int Distance(Vector3 first, Vector3 second)
        => Mathf.RoundToInt(GridHelper.ChebyshevDistance(first, second));
}

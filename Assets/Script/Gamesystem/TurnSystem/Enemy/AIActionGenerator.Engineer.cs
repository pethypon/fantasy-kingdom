using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded engineer candidates use official individual visibility and paid placement commands.</summary>
public static partial class AIActionGenerator
{
    const int MaximumEngineerBuildCandidates = 24;
    static readonly List<Vector3Int> engineerBuildCells = new List<Vector3Int>(64);
    static readonly List<Status> engineerOwnBuildings = new List<Status>(32);
    static readonly Dictionary<Status, Vector3> engineerSupplyGoals = new Dictionary<Status, Vector3>();
    static readonly HashSet<Vector3Int> engineerObservedCells = new HashSet<Vector3Int>();
    static AIBoardState engineerBoard;
    static int engineerGeneration = -1, engineerContentRevision = -1;
    static bool engineerOreGap;
    static int plannedIronUpkeep, plannedMagicUpkeep, plannedIronPurchase, plannedMagicPurchase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetEngineerPlanning()
    {
        engineerBuildCells.Clear(); engineerOwnBuildings.Clear(); engineerSupplyGoals.Clear(); engineerObservedCells.Clear();
        engineerBoard = null; engineerGeneration = engineerContentRevision = -1;
    }
    public static bool HasRareOreSupplyGap(AIBoardState board)
    {
        if (board == null || board.EnemyResources == null) return false;
        // Read the already prepared bounded shopping order. Calling ProductionDemand here
        // would recursively prepare the governor while it is constructing that same order.
        int ironUpkeep = 0, magicUpkeep = 0, ironPurchase = 0, magicPurchase = 0;
        var plan = board.Governor?.RecruitmentPlan;
        if (plan != null)
            for (int i = 0; i < plan.Count && i < 3; i++)
            {
                var definition = plan[i].Definition;
                if (definition == null || definition.kind == Kind.Engineer) continue;
                var upkeep = definition.GetFixedUpkeep();
                // Capacity for the next purchase, not three imaginary armies at once.
                ironUpkeep = Mathf.Max(ironUpkeep, upkeep.Iron);
                magicUpkeep = Mathf.Max(magicUpkeep, upkeep.MagicOre);
                ironPurchase = Mathf.Max(ironPurchase, definition.costIron);
                magicPurchase = Mathf.Max(magicPurchase, definition.costMagic);
            }
        if (engineerBoard == board && engineerGeneration == board.Generation && engineerContentRevision == GameContentRevision.Revision
            && plannedIronUpkeep == ironUpkeep && plannedMagicUpkeep == magicUpkeep
            && plannedIronPurchase == ironPurchase && plannedMagicPurchase == magicPurchase) return engineerOreGap;
        engineerBoard = board; engineerGeneration = board.Generation; engineerContentRevision = GameContentRevision.Revision;
        plannedIronUpkeep = ironUpkeep; plannedMagicUpkeep = magicUpkeep;
        plannedIronPurchase = ironPurchase; plannedMagicPurchase = magicPurchase;
        engineerSupplyGoals.Clear(); engineerObservedCells.Clear();
        foreach (var cell in board.OwnVisionCells) engineerObservedCells.Add(GridHelper.ToGridXZ(cell));
        double ironDemand = 0, magicDemand = 0, ironIncome = 0, magicIncome = 0;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam || actor.type != Type.Unit) continue;
            var definition = actor.GrowthData ?? board.ResolveUnitDefinition(actor.kind);
            if (definition == null) continue;
            var upkeep = definition.GetFixedUpkeep(); ironDemand += upkeep.Iron; magicDemand += upkeep.MagicOre;
        }
        board.CollectOwnBuildings(engineerOwnBuildings);
        foreach (var actor in engineerOwnBuildings)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam) continue;
            var recipe = AIConstructionEconomy.ExistingRecipe(actor, actor.Level, board);
            // Guaranteed income only. Probabilistic magic output never funds promised upkeep.
            ironIncome += AIBasicResourceEconomy.NetOutput(recipe, (int)ResourceKind.Iron);
            magicIncome += AIBasicResourceEconomy.NetOutput(recipe, (int)ResourceKind.MagicOre);
        }
        engineerOreGap = ironDemand + ironUpkeep > ironIncome + .001 || magicDemand + magicUpkeep > magicIncome + .001
            || ironPurchase > board.EnemyResources.Iron && ironIncome <= .001
            || magicPurchase > board.EnemyResources.MagicOre && magicIncome <= .001;
        return engineerOreGap;
    }
    public static void GenerateEngineerBuildCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board?.BuildSystem == null || results == null) return;
        int before = results.Count;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (AITurnBudget.Expired || results.Count - before >= MaximumEngineerBuildCandidates) break;
            if (!ConfiguredEngineer(actor, board)) continue;
            foreach (var entry in FacilityData.Table)
            {
                if (AITurnBudget.Expired || results.Count - before >= MaximumEngineerBuildCandidates) break;
                var definition = FacilityAuthoringCatalog.Loaded?.FindStandard(entry.Key);
                AddEngineerFacilityCandidates(actor, board, results, entry.Key, definition, before);
            }
            var catalog = FacilityAuthoringCatalog.Loaded;
            if (catalog?.buildings == null) continue;
            int checkedDefinitions = 0;
            foreach (var definition in catalog.buildings)
            {
                if (AITurnBudget.Expired || results.Count - before >= MaximumEngineerBuildCandidates || checkedDefinitions++ >= MaxAuthoredBuildDefinitions) break;
                if (definition == null || catalog.Find(definition.definitionId) != definition
                    || catalog.FindStandard(definition.behaviourKind) == definition) continue;
                AddEngineerFacilityCandidates(actor, board, results, definition.behaviourKind, definition, before);
            }
        }
    }
    static bool ConfiguredEngineer(Status actor, AIBoardState board) => actor != null && actor.IsAlive
        && actor.type == Type.Unit && actor.kind == Kind.Engineer && actor.team == board.ActorTeam
        && actor.GrowthData != null && actor.GrowthData.IsValidForAuthoring && actor.GrowthData.AvailableFor(board.ActorTeam)
        && !StatusEffectSystem.IsStunned(actor);

    static void AddEngineerFacilityCandidates(Status engineer, AIBoardState board, List<AIAction> results,
        FacilityKind kind, FacilityDefinitionData definition, int initialCount)
    {
        if (FacilityData.IsSubCrystal(kind) || definition != null && (!definition.IsValid || !definition.IsAvailable(board.ActorTeam))) return;
        var builder = board.BuildSystem;
        if ((definition != null ? builder.GetEngineerBuildCostFailure(engineer, definition) : builder.GetEngineerBuildCostFailure(engineer, kind)) != null) return;
        if (definition != null) builder.CollectEngineerBuildablePositions(engineer, engineerBuildCells, definition);
        else builder.CollectEngineerBuildablePositions(engineer, engineerBuildCells, kind);
        int count = 0;
        foreach (var cell in engineerBuildCells)
        {
            if (AITurnBudget.Expired || results.Count - initialCount >= MaximumEngineerBuildCandidates) break;
            if (board.EstimateCounterDamageAt(cell, engineer) >= engineer.HP) continue;
            var action = new AIAction { ActionType = AIActionType.Build, Unit = engineer, Facility = kind,
                FacilityDefinition = definition, TargetPos = cell,
                APCost = definition != null ? builder.GetEngineerBuildAP(definition) : builder.GetEngineerBuildAP(kind) };
            var assessment = board.ProductionDemand.EvaluateBuild(action);
            if (!assessment.ImprovesDeficit && board.Governor?.HousingAssessment(action, board).Needed != true
                && (board.Governor?.Mode != StrategicMode.EmergencyDefense || !AIStrategicGovernor.IsMilitaryConstruction(action))) continue;
            if (board.GetBuildingCount(kind) >= GetMaxBuildingCount(kind) && !assessment.ImprovesDeficit) continue;
            results.Add(action);
            if (++count >= 2) break;
        }
    }
    public static bool IsEngineerSupplyProgress(AIAction action, AIBoardState board)
    {
        if (action?.ActionType != AIActionType.Move || board == null || !ConfiguredEngineer(action.Unit, board)
            || !HasRareOreSupplyGap(board)) return false;
        var actor = action.Unit;
        if (!engineerSupplyGoals.TryGetValue(actor, out var goal))
        {
            var territory = AIConstructionEconomy.Territory(board);
            if (territory == null) return false;
            float best = float.PositiveInfinity; goal = actor.transform.position;
            foreach (var position in board.KnownTerrain())
            {
                if (AITurnBudget.Expired) break;
                var xz = GridHelper.ToGridXZ(position);
                if (territory.IsInTerritory(xz, board.ActorTeam) || board.TerritoryIntel.IsRememberedTerritory(position)) continue;
                if (engineerObservedCells.Contains(xz) && territory.IsInTerritory(xz, board.OpponentTeam)) continue;
                if (board.MapCreate != null && (board.MapCreate.IsHighMountain(xz.x, xz.z) || board.MapCreate.IsRiver(xz.x, xz.z))) continue;
                float distance = GridHelper.ChebyshevDistance(actor.transform.position, position);
                if (distance < 1 || distance >= best || board.EstimateCounterDamageAt(position, actor) >= actor.HP) continue;
                best = distance; goal = position;
            }
            engineerSupplyGoals[actor] = goal;
        }
        return GridHelper.ChebyshevDistance(action.TargetPos, goal) < GridHelper.ChebyshevDistance(actor.transform.position, goal)
            && board.EstimateCounterDamageAt(action.TargetPos, actor) < actor.HP;
    }
}

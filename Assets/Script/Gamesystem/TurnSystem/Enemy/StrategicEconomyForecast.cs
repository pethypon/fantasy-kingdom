using System;
using System.Collections.Generic;
using UnityEngine;

public enum EconomicState { Healthy, Warning, Crisis, Collapse }

public readonly struct EconomyForecastResult
{
    public readonly EconomicState State;
    public readonly float NetBreadPerTurn, BreadCoverageTurns, MinimumBread;
    public readonly bool BreadPaymentFailed, UpkeepPaymentFailed, ImportantProductionStopped;
    public readonly int MandatoryFailureMask, InputFailureMask, FirstTurnMandatoryFailureMask, FirstTurnInputFailureMask;

    public EconomyForecastResult(EconomicState state, float net, float coverage, float minimum, bool bread, bool upkeep,
        bool stopped, int mandatoryMask = 0, int inputMask = 0, int firstMandatoryMask = 0, int firstInputMask = 0)
    {
        State = state; NetBreadPerTurn = net; BreadCoverageTurns = coverage; MinimumBread = minimum;
        BreadPaymentFailed = bread; UpkeepPaymentFailed = upkeep; ImportantProductionStopped = stopped;
        MandatoryFailureMask = mandatoryMask; InputFailureMask = inputMask;
        FirstTurnMandatoryFailureMask = firstMandatoryMask; FirstTurnInputFailureMask = firstInputMask;
    }

    internal EconomyForecastResult WithState(EconomicState state) => new EconomyForecastResult(state, NetBreadPerTurn,
        BreadCoverageTurns, MinimumBread, BreadPaymentFailed, UpkeepPaymentFailed, ImportantProductionStopped,
        MandatoryFailureMask, InputFailureMask, FirstTurnMandatoryFailureMask, FirstTurnInputFailureMask);
}

/// <summary>Own-side, conservative bounded forecast. Private copies never change live resources or actor state.</summary>
public sealed class StrategicEconomyForecast
{
    internal const int ResourceCount = 8;
    const int BreadIndex = (int)ResourceKind.Bread;
    const int CitizenIndex = (int)ResourceKind.Citizen;
    readonly List<Status> buildings = new List<Status>(32);
    readonly List<FacilityData.FacilityLevelData> recipes = new List<FacilityData.FacilityLevelData>(32);
    readonly List<Status> recipeActors = new List<Status>(32);
    readonly List<FacilityData.ProductionBundle> upkeeps = new List<FacilityData.ProductionBundle>(32);
    readonly double[] stock = new double[ResourceCount], produced = new double[ResourceCount], demanded = new double[ResourceCount];
    readonly double[] nominalDemand = new double[ResourceCount];
    readonly double[] temporaryIncome = new double[ResourceCount];
    readonly double[] probableProduction = new double[ResourceCount];
    bool[] operatingRecipes = Array.Empty<bool>();
    readonly float[,] chainEdges = new float[ResourceCount, ResourceCount];
    AIBoardState preparedBoard;
    int generation = -1, reserveOverride, forecastOverride, citizenCapacity, warehouseBonus;
    Snapshot baseline;
    StrategicProductionDemand growthDemand, recoveryDemand;

    sealed class Snapshot
    {
        public readonly float[] Initial = new float[ResourceCount], Production = new float[ResourceCount];
        public readonly float[] Mandatory = new float[ResourceCount], Projected = new float[ResourceCount];
        public readonly float[] PermanentProduction = new float[ResourceCount], TemporaryIncome = new float[ResourceCount];
        public readonly int[] ProducerCounts = new int[ResourceCount];
        public readonly float[] PotentialProduction = new float[ResourceCount];
        public readonly int[] PotentialSourceCounts = new int[ResourceCount];
        public float NaturalCitizenGrowthPerTurn;
        public EconomyForecastResult Result;
    }

    // Preserve the existing assignable API; defaults now come from authorable policy.
    public int ReserveTurns
    {
        get => reserveOverride > 0 ? reserveOverride : AIEconomySettings.Active.BreadReserveTurns;
        set { reserveOverride = Mathf.Clamp(value, 1, 8); InvalidateDiagnosis(); }
    }
    public int ForecastTurns
    {
        get => forecastOverride > 0 ? forecastOverride : Mathf.Clamp(AIEconomySettings.Active.forecastTurns, 1, 8);
        set { forecastOverride = Mathf.Clamp(value, 1, 8); InvalidateDiagnosis(); }
    }
    void InvalidateDiagnosis() { baseline = null; growthDemand = null; recoveryDemand = null; }

    public void Prepare(AIBoardState board)
    {
        if (board == null)
        {
            preparedBoard = null; generation = -1; InvalidateDiagnosis(); recipes.Clear(); recipeActors.Clear(); upkeeps.Clear();
            citizenCapacity = FactionState.BaseCitizenCap; warehouseBonus = 0;
            Array.Clear(nominalDemand, 0, ResourceCount);
            return;
        }
        if (preparedBoard == board && generation == board.Generation) return;
        preparedBoard = board; generation = board.Generation;
        InvalidateDiagnosis(); recipes.Clear(); recipeActors.Clear(); upkeeps.Clear(); Array.Clear(nominalDemand, 0, ResourceCount);
        citizenCapacity = FactionState.BaseCitizenCap; warehouseBonus = 0;
        board.CollectOwnBuildings(buildings);
        foreach (var actor in buildings)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam) continue;
            var recipe = FacilityData.GetLevel(actor, actor.Level);
            var kind = actor.facilityKind;
            bool special = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse || kind == FacilityKind.Warehouse || kind == FacilityKind.Barracks;
            if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) citizenCapacity += recipe.SpecialValue;
            if (kind == FacilityKind.Warehouse) warehouseBonus += recipe.SpecialValue;
            // Legacy special buildings bypass recipes; authored versions may produce or pay upkeep.
            if (special && actor.AuthoredFacility == null) continue;
            recipes.Add(recipe); recipeActors.Add(actor); AddTo(nominalDemand, recipe.Maintenance);
            if (recipe.HasProduction) AddTo(nominalDemand, recipe.Input, 1d / FacilityData.ProductionInterval(recipe));
        }
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit) continue;
            var definition = actor.GrowthData ?? board.ResolveUnitDefinition(actor.kind);
            if (definition == null) continue;
            var upkeep = definition.GetUpkeep(actor.Level);
            upkeeps.Add(upkeep); AddTo(nominalDemand, upkeep);
        }
        nominalDemand[BreadIndex] += Mathf.Max(0, board.EnemyResources?.Citizen ?? 0) * EconomySystem.CitizenBreadCost;
    }

    public EconomyForecastResult Simulate(AIBoardState board, AIAction candidate = null)
    {
        Prepare(board);
        if (board == null || board.EnemyResources == null)
            return new EconomyForecastResult(EconomicState.Healthy, 0, float.PositiveInfinity, 0, false, false, false);
        if (candidate == null && baseline != null) return baseline.Result;
        return Run(board, candidate, candidate == null);
    }

    EconomyForecastResult Run(AIBoardState board, AIAction candidate, bool capture)
    {
        Read(board.EnemyResources, stock);
        Array.Clear(produced, 0, ResourceCount); Array.Clear(demanded, 0, ResourceCount); Array.Clear(temporaryIncome, 0, ResourceCount); Array.Clear(probableProduction, 0, ResourceCount);
        var snapshot = capture ? new Snapshot() : null;
        if (capture) for (int i = 0; i < ResourceCount; i++) snapshot.Initial[i] = (float)stock[i];
        UnitData additionalUnit = null;
        FacilityData.FacilityLevelData additionalRecipe = default;
        bool hasRecipe = false;
        int replacedRecipe = -1;
        int capacity = citizenCapacity, resourceBonus = warehouseBonus;
        if (candidate?.ActionType == AIActionType.Summon)
        {
            additionalUnit = candidate.SummonDefinition ?? board.ResolveUnitDefinition(candidate.SummonKind);
            if (additionalUnit != null) AddTo(stock, UnitCost(additionalUnit), -1);
        }
        if (candidate?.ActionType == AIActionType.Build)
        {
            var info = candidate.FacilityDefinition != null ? candidate.FacilityDefinition.GetInfo()
                : FacilityData.Table.TryGetValue(candidate.Facility, out var value) ? value : default;
            AddTo(stock, BuildCost(info.BuildCost), -1);
            additionalRecipe = candidate.FacilityDefinition != null ? candidate.FacilityDefinition.GetLevel(1) : FacilityData.GetLevel(candidate.Facility, 1);
            var kind = candidate.FacilityDefinition != null ? candidate.FacilityDefinition.behaviourKind : candidate.Facility;
            if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) capacity += additionalRecipe.SpecialValue;
            if (kind == FacilityKind.Warehouse) resourceBonus += additionalRecipe.SpecialValue;
            bool special = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse || kind == FacilityKind.Warehouse || kind == FacilityKind.Barracks;
            hasRecipe = !special || candidate.FacilityDefinition != null;
        }
        if (candidate?.ActionType == AIActionType.Upgrade)
        {
            var actor = candidate.Unit ?? candidate.TargetUnit;
            if (actor != null && actor.IsAlive && actor.team == board.ActorTeam)
            {
                int currentLevel = Mathf.Max(1, actor.Level);
                var previous = FacilityData.GetLevel(actor, currentLevel);
                additionalRecipe = FacilityData.GetLevel(actor, currentLevel + 1);
                AddTo(stock, BuildCost(additionalRecipe.UpgradeCost), -1);
                replacedRecipe = recipeActors.IndexOf(actor);
                var kind = actor.facilityKind;
                if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) capacity += additionalRecipe.SpecialValue - previous.SpecialValue;
                if (kind == FacilityKind.Warehouse) resourceBonus += additionalRecipe.SpecialValue - previous.SpecialValue;
                bool special = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse || kind == FacilityKind.Warehouse || kind == FacilityKind.Barracks;
                hasRecipe = !special || actor.AuthoredFacility != null;
            }
        }
        int mandatoryMask = 0, inputMask = 0, firstMandatory = 0, firstInput = 0;
        for (int i = 0; i < ResourceCount; i++)
        {
            if (stock[i] < 0) mandatoryMask |= 1 << i;
            stock[i] = Math.Max(0, stock[i]);
        }
        bool upkeepFail = mandatoryMask != 0, breadFail = (mandatoryMask & (1 << BreadIndex)) != 0;
        bool stopped = false, firstStopped = false;
        double initialBread = stock[BreadIndex], minimumBread = initialBread;
        int turns = ForecastTurns, starvationCounter = board.NationStarvationCounter;
        float naturalCitizenGrowth = 0;
        int starvationGrace = Mathf.Clamp(GameAuthoringRules.Active?.starvationGraceTurns ?? 10, 1, 100);
        if (operatingRecipes.Length < recipes.Count) Array.Resize(ref operatingRecipes, Mathf.Max(32, recipes.Count));
        for (int t = 0; t < turns; t++)
        {
            // Mandatory payments are resolved before any current-turn income, as in EconomySystem.
            foreach (var upkeep in upkeeps) Pay(upkeep, ref breadFail, ref upkeepFail, ref mandatoryMask);
            if (additionalUnit != null) Pay(additionalUnit.GetUpkeep(1), ref breadFail, ref upkeepFail, ref mandatoryMask);
            for (int r = 0; r < recipes.Count; r++)
                operatingRecipes[r] = PayMaintenance(r == replacedRecipe ? additionalRecipe : recipes[r],
                    ref breadFail, ref upkeepFail, ref stopped, ref mandatoryMask);
            bool additionalOperating = hasRecipe && replacedRecipe < 0 && PayMaintenance(additionalRecipe,
                ref breadFail, ref upkeepFail, ref stopped, ref mandatoryMask);
            int productionTurn = board.NationTurnsAlive + t;
            // Crystal remains a separate unchanged income source.
            if (board.EnemyCrystalHP > 0) AddCrystalIncome(productionTurn);
            for (int r = 0; r < recipes.Count; r++)
                if (operatingRecipes[r]) ProcessProduction(r == replacedRecipe ? additionalRecipe : recipes[r], productionTurn,
                    ref breadFail, ref stopped, ref inputMask);
            if (additionalOperating) ProcessProduction(additionalRecipe, productionTurn, ref breadFail, ref stopped, ref inputMask);
            if (stock[BreadIndex] > 0 && stock[CitizenIndex] < capacity)
            {
                stock[BreadIndex]--; demanded[BreadIndex]++;
                stock[CitizenIndex]++; produced[CitizenIndex]++; naturalCitizenGrowth++;
            }
            if (stock[CitizenIndex] > 0)
            {
                double food = stock[CitizenIndex] * EconomySystem.CitizenBreadCost;
                demanded[BreadIndex] += food;
                if (stock[BreadIndex] < food)
                {
                    int fedCitizens = EconomySystem.CitizenBreadCost > 0 ? (int)(stock[BreadIndex] / EconomySystem.CitizenBreadCost) : (int)stock[CitizenIndex];
                    stock[BreadIndex] = 0; breadFail = true; mandatoryMask |= 1 << BreadIndex; starvationCounter++;
                    if (starvationCounter >= starvationGrace) stock[CitizenIndex] = fedCitizens;
                }
                else { stock[BreadIndex] -= food; starvationCounter = 0; }
            }
            if (resourceBonus > 0)
                for (int i = 0; i < CitizenIndex; i++) stock[i] = Math.Min(stock[i], FactionState.BaseResourceCap + resourceBonus);
            minimumBread = Math.Min(minimumBread, stock[BreadIndex]);
            if (t == 0) { firstMandatory = mandatoryMask; firstInput = inputMask; firstStopped = stopped; }
        }
        // Requested consumption survives failed payments; a zero stock cannot hide a continuing deficit.
        float netBread = (float)((produced[BreadIndex] - demanded[BreadIndex]) / turns);
        float coverage = netBread < 0 ? (float)(initialBread / Math.Max(AIEconomySettings.Active.Epsilon, -netBread)) : float.PositiveInfinity;
        if (breadFail) coverage = Mathf.Min(coverage, turns - 1);
        bool warning = coverage < ReserveTurns + Mathf.Clamp(AIEconomySettings.Active.breadCoverageMarginTurns, 0, 8);
        for (int i = 0; i < ResourceCount; i++) warning |= demanded[i] - produced[i] > AIEconomySettings.Active.Epsilon;
        EconomicState state = firstMandatory != 0 || firstStopped ? EconomicState.Collapse
            : mandatoryMask != 0 || stopped ? EconomicState.Crisis : warning ? EconomicState.Warning : EconomicState.Healthy;
        var result = new EconomyForecastResult(state, netBread, coverage, (float)minimumBread, breadFail, upkeepFail, stopped,
            mandatoryMask, inputMask, firstMandatory, firstInput);
        if (capture)
        {
            for (int i = 0; i < ResourceCount; i++)
            {
                snapshot.Production[i] = (float)(produced[i] / turns);
                snapshot.PermanentProduction[i] = (float)((produced[i] - temporaryIncome[i]) / turns);
                snapshot.PotentialProduction[i] = snapshot.PermanentProduction[i] + (float)(probableProduction[i] / turns);
                snapshot.TemporaryIncome[i] = (float)temporaryIncome[i];
                snapshot.Mandatory[i] = (float)(demanded[i] / turns);
                snapshot.Projected[i] = (float)stock[i];
                foreach (var recipe in recipes)
                {
                    if (AIBasicResourceEconomy.NetOutput(recipe, i) > AIEconomySettings.Active.Epsilon) snapshot.ProducerCounts[i]++;
                    if (AIBasicResourceEconomy.ExpectedNetOutput(recipe, i) > AIEconomySettings.Active.Epsilon) snapshot.PotentialSourceCounts[i]++;
                }
                if (i == CitizenIndex && produced[i] > 0) { snapshot.ProducerCounts[i]++; snapshot.PotentialSourceCounts[i]++; }
            }
            snapshot.NaturalCitizenGrowthPerTurn = naturalCitizenGrowth / turns;
            snapshot.Result = result; baseline = snapshot;
        }
        return result;
    }

    bool PayMaintenance(FacilityData.FacilityLevelData recipe, ref bool breadFail, ref bool upkeepFail,
        ref bool stopped, ref int mandatoryMask)
    {
        AddTo(demanded, recipe.Maintenance);
        int maintenanceMissing = Missing(recipe.Maintenance);
        if (maintenanceMissing != 0)
        {
            mandatoryMask |= maintenanceMissing; upkeepFail = true;
            breadFail |= (maintenanceMissing & (1 << BreadIndex)) != 0;
            stopped |= IsImportant(recipe);
            return false;
        }
        AddTo(stock, recipe.Maintenance, -1);
        return true;
    }

    void ProcessProduction(FacilityData.FacilityLevelData recipe, int turn, ref bool breadFail,
        ref bool stopped, ref int inputMask)
    {
        if (!recipe.HasProduction || !FacilityData.IsProductionTurn(turn, recipe)) return;
        // A due recipe retains requested inputs even when it cannot operate.
        AddTo(demanded, recipe.Input);
        int inputMissing = Missing(recipe.Input);
        if (inputMissing != 0)
        {
            inputMask |= inputMissing;
            breadFail |= (inputMissing & (1 << BreadIndex)) != 0;
            stopped |= IsImportant(recipe); return;
        }
        AddTo(stock, recipe.Input, -1); AddOutput(recipe.Output);
        // Only guaranteed bonuses may finance mandatory obligations.
        if (recipe.BonusChance1 >= 1) AddOutput(recipe.BonusOutput1);
        if (recipe.BonusChance2 >= 1) AddOutput(recipe.BonusOutput2);
        // Expected uncertain supply identifies acquisition paths, never guaranteed funding.
        if (recipe.BonusChance1 > 0 && recipe.BonusChance1 < 1) AddTo(probableProduction, recipe.BonusOutput1, recipe.BonusChance1);
        if (recipe.BonusChance2 > 0 && recipe.BonusChance2 < 1) AddTo(probableProduction, recipe.BonusOutput2, recipe.BonusChance2);
    }

    bool IsImportant(FacilityData.FacilityLevelData recipe)
    {
        for (int i = 0; i < ResourceCount; i++)
            if (GuaranteedOutput(recipe, i) > 0 && (i == BreadIndex || nominalDemand[i] > 0)) return true;
        return recipe.Input.Bread > 0;
    }
    void Pay(FacilityData.ProductionBundle cost, ref bool breadFail, ref bool upkeepFail, ref int mandatoryMask)
    {
        AddTo(demanded, cost);
        int missing = Missing(cost);
        if (missing == 0) AddTo(stock, cost, -1);
        else { mandatoryMask |= missing; upkeepFail = true; breadFail |= (missing & (1 << BreadIndex)) != 0; }
    }
    int Missing(FacilityData.ProductionBundle cost)
    {
        int mask = 0;
        for (int i = 0; i < ResourceCount; i++) if (stock[i] < Amount(cost, i)) mask |= 1 << i;
        return mask;
    }
    void AddOutput(FacilityData.ProductionBundle output) { AddTo(stock, output); AddTo(produced, output); }
    void AddCrystalIncome(int turn)
    {
        float decay = Mathf.Clamp01(1 - turn * .1f);
        var income = new FacilityData.ProductionBundle
        {
            Wood = Mathf.RoundToInt(20 * decay), Stone = Mathf.RoundToInt(20 * decay), Iron = Mathf.RoundToInt(5 * decay),
            Bread = Mathf.RoundToInt(10 * decay), Water = Mathf.RoundToInt(10 * decay)
        };
        AddOutput(income); AddTo(temporaryIncome, income);
    }

    public StrategicProductionDemand Diagnose(AIBoardState board, bool includeGrowthPlans = true)
    {
        Prepare(board);
        var cached = includeGrowthPlans ? growthDemand : recoveryDemand;
        if (cached != null) return cached;
        var result = Simulate(board);
        bool hasResources = board?.EnemyResources != null;
        var data = hasResources && baseline != null ? baseline : new Snapshot();
        var settings = AIEconomySettings.Active;
        int turns = ForecastTurns, criticalMask = result.MandatoryFailureMask;
        if (result.ImportantProductionStopped) criticalMask |= result.InputFailureMask;
        var planned = new double[ResourceCount];
        if (includeGrowthPlans && board?.EnemyResources != null) AddGrowthPlans(board, planned, settings, turns);
        var resources = new ResourceDemandState[ResourceCount];
        bool warning = result.State >= EconomicState.Warning;
        float bottleneck = 0;
        for (int i = 0; i < ResourceCount; i++)
        {
            float production = data.Production[i], mandatory = data.Mandatory[i], net = production - mandatory;
            int reserveTurns = i == BreadIndex ? ReserveTurns : Mathf.Clamp(settings.reserveTurns, 1, 8);
            float reserve = hasResources ? mandatory * reserveTurns + Mathf.Max(0, Amount(settings.minimumOperationalBuffer, i)) : 0;
            float recovery = Mathf.Max(0, reserve - data.Initial[i]) / Mathf.Clamp(settings.recoveryWindowTurns, 1, 8);
            float plan = (float)planned[i], target = mandatory + plan + recovery;
            float projected = Mathf.Max(0, data.Projected[i] - plan * turns);
            float deficit = Mathf.Max(0, target - production);
            bool sustainedDeficit = net < -settings.Epsilon;
            bool critical = mandatory > settings.Epsilon && sustainedDeficit
                && data.Projected[i] <= reserve * Mathf.Clamp01(settings.criticalReserveFraction);
            if (critical) criticalMask |= 1 << i;
            warning |= sustainedDeficit || deficit > settings.Epsilon && projected < reserve - settings.Epsilon;
            float urgency = 0;
            if (deficit > settings.Epsilon)
            {
                urgency = Mathf.Clamp01(settings.sustainedDeficitUrgency);
                if (sustainedDeficit)
                {
                    float resourceCoverage = data.Initial[i] / Mathf.Max(settings.Epsilon, -net);
                    urgency = Mathf.Max(urgency, Mathf.Clamp01((turns + reserveTurns) / Mathf.Max(settings.Epsilon, resourceCoverage)));
                }
                urgency = Mathf.Max(urgency, Mathf.Clamp01((reserve - projected) / Mathf.Max(settings.Epsilon, reserve)));
            }
            if ((criticalMask & (1 << i)) != 0) urgency = 1;
            resources[i] = new ResourceDemandState((ResourceKind)i, data.Initial[i], production, mandatory, plan, reserve, projected, target, urgency);
            if (i != CitizenIndex && warehouseBonus > 0)
                bottleneck = Mathf.Max(bottleneck, reserve - FactionState.BaseResourceCap - warehouseBonus);
        }
        bool hasCritical = criticalMask != 0 || result.ImportantProductionStopped || result.UpkeepPaymentFailed || result.BreadPaymentFailed;
        var state = result.State;
        if (hasCritical && state < EconomicState.Crisis) state = EconomicState.Crisis;
        else if (warning && state < EconomicState.Warning) state = EconomicState.Warning;
        var chainCoverage = new float[ResourceCount];
        var chainUrgency = new float[ResourceCount];
        int criticalChainMask = BuildChainRecovery(resources, criticalMask, chainCoverage, chainUrgency, out int foodChainMask);
        var diagnosis = new StrategicProductionDemand(resources, result.WithState(state), hasCritical, warning, criticalMask,
            chainCoverage, chainUrgency, criticalChainMask, foodChainMask, turns, citizenCapacity, bottleneck, settings,
            data.PermanentProduction, data.ProducerCounts, data.TemporaryIncome, data.PotentialProduction, data.PotentialSourceCounts,
            data.NaturalCitizenGrowthPerTurn);
        if (includeGrowthPlans) growthDemand = diagnosis; else recoveryDemand = diagnosis;
        return diagnosis;
    }

    void AddGrowthPlans(AIBoardState board, double[] planned, AIEconomySettings settings, int turns)
    {
        float weight = Mathf.Clamp01(settings.plannedDemandWeight);
        // Reserve one plausible economic project from its actual definition, rather than a fixed
        // imaginary Wood/Stone bundle that diverges when designers edit the building catalog.
        float best = float.PositiveInfinity;
        FacilityData.ResourceCost nextCost = default;
        foreach (var entry in FacilityData.Table)
            ConsiderGrowthProject(entry.Value.BuildCost, FacilityData.GetLevel(entry.Key, 1), ref best, ref nextCost);
        var catalog = FacilityAuthoringCatalog.Loaded;
        if (catalog?.buildings != null) foreach (var facility in catalog.buildings)
            if (facility != null && facility.IsAvailable(board.ActorTeam) && catalog.Find(facility.definitionId) == facility)
                ConsiderGrowthProject(facility.GetInfo().BuildCost, facility.GetLevel(1), ref best, ref nextCost);
        if (!float.IsInfinity(best)) AddTo(planned, BuildCost(nextCost), weight * AIEconomySettings.NonNegative(settings.plannedBuildActions) / turns);
        bool exploration = board.AlivePlayerUnits.Count == 0;
        var definition = board.ResolveUnitDefinition(exploration ? Kind.Scout : Kind.Knight);
        if (definition == null) return;
        float summonWeight = weight * AIEconomySettings.NonNegative(settings.plannedSummonActions)
            * (exploration ? Mathf.Clamp01(settings.explorationSummonWeight) : 1);
        AddTo(planned, UnitCost(definition), summonWeight / turns);
        AddTo(planned, definition.GetUpkeep(Mathf.Clamp(settings.plannedUnitLevel, 1, 8)), summonWeight);
    }

    void ConsiderGrowthProject(FacilityData.ResourceCost cost, FacilityData.FacilityLevelData recipe,
        ref float best, ref FacilityData.ResourceCost selected)
    {
        if (!recipe.HasProduction || baseline == null) return;
        float useful = 0, investment = 0;
        var bundle = BuildCost(cost);
        for (int i = 0; i < ResourceCount; i++)
        {
            float desired = baseline.Mandatory[i];
            if (i == (int)ResourceKind.Wood || i == (int)ResourceKind.Stone || i == (int)ResourceKind.Water)
                desired += 1; // A small persistent supply supports the next real economic action.
            float deficit = Mathf.Max(0, desired - baseline.PermanentProduction[i]);
            useful += Mathf.Min(deficit, Mathf.Max(0, AIBasicResourceEconomy.NetOutput(recipe, i)));
            investment += Amount(bundle, i);
        }
        if (useful <= .001f) return;
        float value = investment / useful;
        if (value < best) { best = value; selected = cost; }
    }

    int BuildChainRecovery(ResourceDemandState[] resources, int criticalMask, float[] coverage, float[] urgency, out int foodChainMask)
    {
        Array.Clear(chainEdges, 0, chainEdges.Length);
        foreach (var recipe in recipes)
        {
            for (int input = 0; input < ResourceCount; input++)
            {
                float cost = Amount(recipe.Input, input) / (float)FacilityData.ProductionInterval(recipe) + Amount(recipe.Maintenance, input);
                if (cost <= 0 || resources[input].ProductionDeficit <= AIEconomySettings.Active.Epsilon) continue;
                for (int output = 0; output < ResourceCount; output++)
                {
                    if (input == output) continue;
                    float gain = AIBasicResourceEconomy.NetOutput(recipe, output);
                    if (gain > 0) chainEdges[input, output] = Mathf.Max(chainEdges[input, output], gain / cost);
                }
            }
        }
        // Each resource is visited once as an intermediate; authored cycles cannot repeatedly
        // manufacture hypothetical recovery value. This work runs only when a diagnosis is rebuilt.
        for (int via = 0; via < ResourceCount; via++)
            for (int input = 0; input < ResourceCount; input++)
                for (int output = 0; output < ResourceCount; output++)
                    if (input != output && input != via && output != via)
                        chainEdges[input, output] = Mathf.Max(chainEdges[input, output], chainEdges[input, via] * chainEdges[via, output]);
        int criticalChainMask = 0;
        foodChainMask = 0;
        for (int input = 0; input < ResourceCount; input++)
        {
            for (int output = 0; output < ResourceCount; output++)
            {
                float deficit = resources[output].ProductionDeficit;
                if (input == output || deficit <= AIEconomySettings.Active.Epsilon || chainEdges[input, output] <= 0) continue;
                coverage[input] = Mathf.Max(coverage[input], chainEdges[input, output] / deficit);
                urgency[input] = Mathf.Max(urgency[input], resources[output].Urgency01);
                if ((criticalMask & (1 << output)) != 0) criticalChainMask |= 1 << input;
                if (output == BreadIndex) foodChainMask |= 1 << input;
            }
        }
        return criticalChainMask;
    }

    public bool ImprovesFoodSupply(AIAction action, AIBoardState board)
    {
        if (action == null || action.ActionType != AIActionType.Build || board?.EnemyResources == null) return false;
        return Diagnose(board, false).ImprovesFoodSupply(action);
    }

    internal static int Amount(FacilityData.ProductionBundle bundle, int resource)
    {
        switch ((ResourceKind)resource)
        {
            case ResourceKind.Wood: return bundle.Wood;
            case ResourceKind.Stone: return bundle.Stone;
            case ResourceKind.Iron: return bundle.Iron;
            case ResourceKind.MagicOre: return bundle.MagicOre;
            case ResourceKind.Wheat: return bundle.Wheat;
            case ResourceKind.Bread: return bundle.Bread;
            case ResourceKind.Water: return bundle.Water;
            case ResourceKind.Citizen: return bundle.Citizen;
            default: return 0;
        }
    }
    internal static float GuaranteedOutput(FacilityData.FacilityLevelData recipe, int resource) => Amount(recipe.Output, resource)
        + (recipe.BonusChance1 >= 1 ? Amount(recipe.BonusOutput1, resource) : 0)
        + (recipe.BonusChance2 >= 1 ? Amount(recipe.BonusOutput2, resource) : 0);
    internal static float GuaranteedOutputPerTurn(FacilityData.FacilityLevelData recipe, int resource)
        => GuaranteedOutput(recipe, resource) / FacilityData.ProductionInterval(recipe);
    static void AddTo(double[] values, FacilityData.ProductionBundle bundle, double multiplier = 1)
    { for (int i = 0; i < ResourceCount; i++) values[i] += Amount(bundle, i) * multiplier; }
    static FacilityData.ProductionBundle BuildCost(FacilityData.ResourceCost cost) => new FacilityData.ProductionBundle
    { Wood = cost.Wood, Stone = cost.Stone, Iron = cost.Iron, MagicOre = cost.MagicOre, Water = cost.Water, Citizen = cost.Citizen };
    static FacilityData.ProductionBundle UnitCost(UnitData unit) => new FacilityData.ProductionBundle
    { Wood = unit.costWood, Stone = unit.costStone, Iron = unit.costIron, MagicOre = unit.costMagic, Bread = unit.costBread, Water = unit.costWater, Citizen = unit.costCitizen };
    static void Read(FactionState.ResourceData resources, double[] values)
    {
        values[0] = Math.Max(0, resources.Wood); values[1] = Math.Max(0, resources.Stone);
        values[2] = Math.Max(0, resources.Iron); values[3] = Math.Max(0, resources.MagicOre);
        values[4] = Math.Max(0, resources.Wheat); values[5] = Math.Max(0, resources.Bread);
        values[6] = Math.Max(0, resources.Water); values[7] = Math.Max(0, resources.Citizen);
    }
}

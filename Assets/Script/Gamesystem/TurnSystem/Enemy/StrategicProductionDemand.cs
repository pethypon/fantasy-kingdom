using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public readonly struct ResourceDemandState
{
    public readonly ResourceKind Resource;
    public readonly float Stock, ProductionPerTurn, MandatoryDemandPerTurn, PlannedDemandPerTurn;
    public readonly float NetPerTurn, SafetyReserve, ProjectedStock, TargetProductionPerTurn, ProductionDeficit, Urgency01;
    public float Production => ProductionPerTurn;
    public float Mandatory => MandatoryDemandPerTurn;
    public float Planned => PlannedDemandPerTurn;
    public float Net => NetPerTurn;
    public float Reserve => SafetyReserve;
    public float Projected => ProjectedStock;
    public float Target => TargetProductionPerTurn;
    public float Deficit => ProductionDeficit;
    public float Urgency => Urgency01;

    public ResourceDemandState(ResourceKind resource, float stock, float production, float mandatory, float planned,
        float reserve, float projected, float target, float urgency)
    {
        Resource = resource; Stock = stock; ProductionPerTurn = production;
        MandatoryDemandPerTurn = mandatory; PlannedDemandPerTurn = planned;
        NetPerTurn = production - mandatory; SafetyReserve = reserve; ProjectedStock = projected;
        TargetProductionPerTurn = target; ProductionDeficit = Mathf.Max(0, target - production);
        Urgency01 = Mathf.Clamp01(urgency);
    }

    public override string ToString() => $"{Resource} stock={Stock:F1} prod={ProductionPerTurn:F1} demand={MandatoryDemandPerTurn:F1} "
        + $"plan={PlannedDemandPerTurn:F1} net={NetPerTurn:F1} reserve={SafetyReserve:F1} projected={ProjectedStock:F1} "
        + $"deficit={ProductionDeficit:F1} urgency={Urgency01:F2}";
}

/// <summary>Economy-only score additions; tactical, phase and personality scores are applied by the caller.</summary>
public readonly struct ProductionBuildAssessment
{
    public readonly bool IsProduction, ImprovesDeficit, ImprovesCritical;
    public readonly float NeedScore, ChainRecoveryScore, ReserveRecoveryScore, OverstockPenalty, InputPressurePenalty;
    public readonly float FinalScore, DeficitCoverage01;
    public bool ImprovesCriticalProduction => ImprovesCritical;
    public float ProductionNeedScore => NeedScore;

    public ProductionBuildAssessment(bool production, bool improves, bool critical, float need, float chain,
        float reserve, float overstock, float coverage, float inputPressure = 0)
    {
        IsProduction = production; ImprovesDeficit = improves; ImprovesCritical = critical;
        NeedScore = need; ChainRecoveryScore = chain; ReserveRecoveryScore = reserve;
        OverstockPenalty = overstock; DeficitCoverage01 = coverage; InputPressurePenalty = inputPressure;
        FinalScore = need + chain + reserve - overstock - inputPressure;
    }
}

/// <summary>
/// An own-side snapshot, cached by board generation. Candidate evaluation reads eight resources only;
/// it never scans the map, actors, or a candidate's placement.
/// </summary>
public sealed class StrategicProductionDemand
{
    public readonly ResourceDemandState[] Resources;
    public readonly bool HasCriticalDeficit, HasWarningDeficit;
    public readonly FacilityKind[] RecommendedFacilities;
    public readonly EconomyForecastResult Forecast;
    public EconomicState State => Forecast.State;
    public bool HasMandatoryPaymentFailure => Forecast.BreadPaymentFailed || Forecast.UpkeepPaymentFailed;
    public bool ImportantProductionStopped => Forecast.ImportantProductionStopped;

    readonly AIEconomySettings settings;
    readonly float[] chainCoverage, chainUrgency;
    readonly int criticalMask, chainCriticalMask, foodChainMask;
    readonly int turns, citizenCapacity;
    readonly float storageBottleneck;
    readonly float[] permanentProduction, temporaryIncome;
    readonly int[] producerCounts;
    readonly float[] potentialProduction;
    readonly int[] potentialSourceCounts;
    public float NaturalCitizenGrowthPerTurn { get; }
    readonly Dictionary<(FacilityKind, FacilityDefinitionData), ProductionBuildAssessment> assessments
        = new Dictionary<(FacilityKind, FacilityDefinitionData), ProductionBuildAssessment>();

    internal StrategicProductionDemand(ResourceDemandState[] resources, EconomyForecastResult forecast, bool critical,
        bool warning, int criticalResources, float[] chainCoverageByResource, float[] chainUrgencyByResource,
        int criticalChainResources, int foodChainResources, int forecastTurns, int capacity, float bottleneck, AIEconomySettings policy,
        float[] permanentProductionByResource = null, int[] productionSources = null, float[] temporaryIncomeByResource = null,
        float[] potentialProductionByResource = null, int[] potentialSources = null, float naturalCitizenGrowthPerTurn = 0)
    {
        Resources = resources; Forecast = forecast; HasCriticalDeficit = critical; HasWarningDeficit = warning;
        criticalMask = criticalResources; chainCriticalMask = criticalChainResources;
        foodChainMask = foodChainResources;
        chainCoverage = chainCoverageByResource; chainUrgency = chainUrgencyByResource;
        turns = forecastTurns; citizenCapacity = capacity; storageBottleneck = bottleneck; settings = policy;
        permanentProduction = permanentProductionByResource; producerCounts = productionSources; temporaryIncome = temporaryIncomeByResource;
        potentialProduction = potentialProductionByResource; potentialSourceCounts = potentialSources;
        NaturalCitizenGrowthPerTurn = Mathf.Max(0, naturalCitizenGrowthPerTurn);

        var recommendations = new List<(FacilityKind kind, float score)>();
        foreach (var entry in FacilityData.Table)
        {
            var assessment = EvaluateBuild(entry.Key);
            if (assessment.ImprovesDeficit && assessment.FinalScore > settings.Epsilon)
                recommendations.Add((entry.Key, assessment.FinalScore));
        }
        recommendations.Sort((a, b) =>
        {
            int order = b.score.CompareTo(a.score);
            return order != 0 ? order : a.kind.CompareTo(b.kind);
        });
        RecommendedFacilities = new FacilityKind[recommendations.Count];
        for (int i = 0; i < recommendations.Count; i++) RecommendedFacilities[i] = recommendations[i].kind;
    }

    public ResourceDemandState Get(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && index < Resources.Length ? Resources[index] : default;
    }

    public float PermanentProduction(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && permanentProduction != null && index < permanentProduction.Length ? permanentProduction[index] : Get(resource).ProductionPerTurn;
    }
    public int ProducerCount(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && producerCounts != null && index < producerCounts.Length ? producerCounts[index] : PermanentProduction(resource) > .001f ? 1 : 0;
    }
    public float TemporaryIncome(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && temporaryIncome != null && index < temporaryIncome.Length ? temporaryIncome[index] : 0;
    }

    /// <summary>Expected uncertain output identifies real acquisition paths, never guaranteed upkeep funding.</summary>
    public float PotentialProduction(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && potentialProduction != null && index < potentialProduction.Length ? potentialProduction[index] : PermanentProduction(resource);
    }
    public int PotentialSourceCount(ResourceKind resource)
    {
        int index = (int)resource;
        return index >= 0 && potentialSourceCounts != null && index < potentialSourceCounts.Length ? potentialSourceCounts[index] : ProducerCount(resource);
    }

    public ProductionBuildAssessment EvaluateBuild(AIAction action)
    {
        if (action == null || action.ActionType != AIActionType.Build) return default;
        return EvaluateBuild(action.Facility, action.FacilityDefinition);
    }

    internal bool ImprovesFoodSupply(AIAction action)
    {
        if (action == null || action.ActionType != AIActionType.Build || Get(ResourceKind.Bread).ProductionDeficit <= settings.Epsilon)
            return false;
        var assessment = EvaluateBuild(action);
        if (!assessment.ImprovesDeficit) return false;
        var recipe = action.FacilityDefinition != null ? action.FacilityDefinition.GetLevel(1) : FacilityData.GetLevel(action.Facility, 1);
        if (AIBasicResourceEconomy.NetOutput(recipe, (int)ResourceKind.Bread) > settings.Epsilon)
            return true;
        if (assessment.ChainRecoveryScore <= settings.Epsilon) return false;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
            if ((foodChainMask & (1 << i)) != 0 && AIBasicResourceEconomy.NetOutput(recipe, i) > settings.Epsilon) return true;
        return false;
    }

    public ProductionBuildAssessment EvaluateBuild(FacilityKind facility, FacilityDefinitionData definition = null)
    {
        var key = (facility, definition);
        if (assessments.TryGetValue(key, out var cached)) return cached;
        var recipe = definition != null ? definition.GetLevel(1) : FacilityData.GetLevel(facility, 1);
        // Authored identities may use a compatibility kind whose normal recipe is entirely different.
        var kind = definition != null ? definition.behaviourKind : facility;
        bool house = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse;
        bool warehouse = kind == FacilityKind.Warehouse;
        bool production = recipe.HasProduction || house || warehouse;
        if (!production) { assessments[key] = default; return default; }

        float operation = 1f;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            float input = StrategicEconomyForecast.Amount(recipe.Input, i) / (float)FacilityData.ProductionInterval(recipe);
            float maintenance = StrategicEconomyForecast.Amount(recipe.Maintenance, i);
            if (input + maintenance <= settings.Epsilon) continue;
            var resource = Resources[i];
            // Existing obligations get the first claim. Stocks can bridge the forecast window,
            // but the new facility cannot promise production from inputs that do not exist.
            float available = Mathf.Max(0, resource.Stock / turns + resource.NetPerTurn);
            operation = Mathf.Min(operation, available / (input + maintenance));
        }
        operation = Mathf.Clamp01(operation);

        float urgency = 0, coverage = 0, chain = 0, reserve = 0, pressure = 0;
        float outputTotal = 0, unneededOutput = 0;
        bool improves = false, improvesCritical = false;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            var resource = Resources[i];
            float output = StrategicEconomyForecast.GuaranteedOutputPerTurn(recipe, i) * operation;
            float extra = output - StrategicEconomyForecast.Amount(recipe.Input, i) * operation / FacilityData.ProductionInterval(recipe)
                - StrategicEconomyForecast.Amount(recipe.Maintenance, i);
            if (house && i == (int)ResourceKind.Citizen && recipe.SpecialValue > 0)
            {
                var bread = Get(ResourceKind.Bread);
                float breadAvailable = bread.Stock / turns + bread.NetPerTurn;
                bool canFeedGrowth = breadAvailable >= 1 + EconomySystem.CitizenBreadCost;
                if (canFeedGrowth && resource.Stock >= citizenCapacity && resource.ProductionDeficit > settings.Epsilon)
                    extra += Mathf.Min(1, recipe.SpecialValue);
            }
            if (extra < -settings.Epsilon && resource.ProductionDeficit > settings.Epsilon)
                pressure += Mathf.Clamp01(-extra / resource.ProductionDeficit) * resource.Urgency01;
            if (extra <= settings.Epsilon) continue;

            outputTotal += extra;
            float covered = Mathf.Min(extra, resource.ProductionDeficit);
            if (covered > settings.Epsilon)
            {
                improves = true;
                improvesCritical |= (criticalMask & (1 << i)) != 0;
                float fraction = covered / Mathf.Max(settings.Epsilon, resource.ProductionDeficit);
                coverage = Mathf.Max(coverage, fraction);
                urgency = Mathf.Max(urgency, resource.Urgency01);
                float reserveRate = Mathf.Max(0, resource.SafetyReserve - resource.Stock) / Mathf.Max(1, settings.recoveryWindowTurns);
                if (reserveRate > settings.Epsilon)
                    reserve = Mathf.Max(reserve, Mathf.Clamp01(extra / reserveRate) * resource.Urgency01);
            }
            if (resource.Stock >= resource.SafetyReserve && resource.NetPerTurn >= -settings.Epsilon)
                unneededOutput += Mathf.Max(0, extra - covered);

            float restored = Mathf.Clamp01(extra * chainCoverage[i]);
            if (restored > settings.Epsilon)
            {
                chain = Mathf.Max(chain, restored * chainUrgency[i]);
                improves = true;
                improvesCritical |= (chainCriticalMask & (1 << i)) != 0;
            }
        }

        if (warehouse && recipe.SpecialValue > 0 && storageBottleneck > settings.Epsilon)
        {
            improves = true;
            coverage = Mathf.Max(coverage, Mathf.Clamp01(recipe.SpecialValue / storageBottleneck));
            urgency = Mathf.Max(urgency, Mathf.Clamp01(settings.sustainedDeficitUrgency));
        }
        float overstock = !improves ? AIEconomySettings.NonNegative(settings.overstockPenalty)
            : outputTotal > settings.Epsilon ? AIEconomySettings.NonNegative(settings.overstockPenalty) * unneededOutput / outputTotal : 0;
        var assessment = new ProductionBuildAssessment(production, improves, improvesCritical,
            urgency * AIEconomySettings.NonNegative(settings.needWeight) + coverage * AIEconomySettings.NonNegative(settings.coverageWeight),
            chain * AIEconomySettings.NonNegative(settings.chainRecoveryWeight),
            reserve * AIEconomySettings.NonNegative(settings.reserveRecoveryWeight), overstock, coverage,
            Mathf.Clamp01(pressure) * AIEconomySettings.NonNegative(settings.coverageWeight));
        assessments[key] = assessment;
        return assessment;
    }

    public override string ToString()
    {
        var text = new StringBuilder().Append("State=").Append(State);
        foreach (var resource in Resources) text.Append('\n').Append(resource);
        text.Append("\nRecommended=");
        for (int i = 0; i < RecommendedFacilities.Length; i++)
        {
            if (i > 0) text.Append(", ");
            var facility = RecommendedFacilities[i];
            float score = EvaluateBuild(facility).FinalScore;
            text.Append(facility).Append('(').Append(score.ToString("+0;-0;0")).Append(')');
        }
        return text.ToString();
    }
}

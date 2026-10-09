using System;
using UnityEngine;

/// <summary>One opt-in source for production-only overrides, shared by simulation and runtime.</summary>
[Serializable]
public sealed class BuildingProductionBalance
{
    // BALANCE_TODO: Building production reduction values are not finalized.
    // Keep current recipes until the designer sets explicit output and interval values here.
    public bool applyAdjustments;
    public BuildingProductionAdjustment[] adjustments = Array.Empty<BuildingProductionAdjustment>();

    public static FacilityData.FacilityLevelData Resolve(FacilityKind kind, string definitionId,
        int level, FacilityData.FacilityLevelData recipe)
    {
        recipe.ProductionIntervalTurns = FacilityData.ProductionInterval(recipe);
        var balance = GameAuthoringRules.Active?.buildingProduction;
        if (balance == null || !balance.applyAdjustments || balance.adjustments == null
            || kind == FacilityKind.SubCrystal) return recipe;
        foreach (var item in balance.adjustments)
        {
            if (item == null || !item.enabled || item.buildingType != kind || item.level != level) continue;
            // An authored identity is never changed by an unrelated legacy kind override.
            if (!string.Equals(item.definitionId ?? string.Empty, definitionId ?? string.Empty, StringComparison.Ordinal)) continue;
            recipe.Output = NonNegative(item.output);
            recipe.BonusOutput1 = NonNegative(item.bonusOutput1);
            recipe.BonusOutput2 = NonNegative(item.bonusOutput2);
            recipe.ProductionIntervalTurns = Mathf.Clamp(item.intervalTurns, 1, 100);
            break;
        }
        return recipe;
    }

    static FacilityData.ProductionBundle NonNegative(FacilityData.ProductionBundle value)
        => new FacilityData.ProductionBundle
        {
            Wood = Mathf.Max(0, value.Wood), Stone = Mathf.Max(0, value.Stone), Iron = Mathf.Max(0, value.Iron),
            MagicOre = Mathf.Max(0, value.MagicOre), Wheat = Mathf.Max(0, value.Wheat), Bread = Mathf.Max(0, value.Bread),
            Water = Mathf.Max(0, value.Water), Citizen = Mathf.Max(0, value.Citizen)
        };
}

[Serializable]
public sealed class BuildingProductionAdjustment
{
    public bool enabled = true;
    public FacilityKind buildingType;
    [Tooltip("既存の標準建物は空欄。自作建物は登録IDを指定します。")]
    public string definitionId = string.Empty;
    [Min(1)] public int level = 1;
    public FacilityData.ProductionBundle output;
    public FacilityData.ProductionBundle bonusOutput1;
    public FacilityData.ProductionBundle bonusOutput2;
    [Range(1, 100)] public int intervalTurns = 1;
}

public static partial class FacilityData
{
    public static int ProductionInterval(FacilityLevelData recipe)
        => Mathf.Clamp(recipe.ProductionIntervalTurns, 1, 100);

    /// <summary>Nation-relative phase survives save/load. Legacy interval 0 means every turn.</summary>
    public static bool IsProductionTurn(int nationTurnsAlive, FacilityLevelData recipe)
        => Mathf.Max(0, nationTurnsAlive) % ProductionInterval(recipe) == 0;
}

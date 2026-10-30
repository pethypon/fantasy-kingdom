using UnityEngine;

public static partial class FacilityData
{
    /// <summary>Position-dependent output only. Costs, inputs, maintenance and upgrade levels are unchanged.</summary>
    public static FacilityLevelData RestrictProductionAt(FacilityLevelData recipe, Vector3 position, Team owner, TerritorySystem territory)
    {
        if (CanProduceRareOre(position, owner, territory)) return recipe;
        recipe.Output = WithoutRareOre(recipe.Output);
        recipe.BonusOutput1 = WithoutRareOre(recipe.BonusOutput1);
        recipe.BonusOutput2 = WithoutRareOre(recipe.BonusOutput2);
        if (recipe.BonusOutput1.IsEmpty) recipe.BonusChance1 = 0;
        if (recipe.BonusOutput2.IsEmpty) recipe.BonusChance2 = 0;
        return recipe;
    }
    public static bool CanProduceRareOre(Vector3 position, Team owner, TerritorySystem territory)
        => territory != null && (owner == Team.Player || owner == Team.Enemy)
            && !territory.IsInTerritory(GridHelper.ToGridXZ(position), owner);
    public static FacilityLevelData GetProductionLevel(Status building, int level, TerritorySystem territory)
        => RestrictProductionAt(GetLevel(building, level), building != null ? building.transform.position : default,
            building != null ? building.team : Team.None, territory);
    public static bool HasRareOreOutput(FacilityLevelData recipe)
        => recipe.Output.Iron > 0 || recipe.Output.MagicOre > 0
            || recipe.BonusChance1 > 0 && (recipe.BonusOutput1.Iron > 0 || recipe.BonusOutput1.MagicOre > 0)
            || recipe.BonusChance2 > 0 && (recipe.BonusOutput2.Iron > 0 || recipe.BonusOutput2.MagicOre > 0);
    static ProductionBundle WithoutRareOre(ProductionBundle output) { output.Iron = output.MagicOre = 0; return output; }
}

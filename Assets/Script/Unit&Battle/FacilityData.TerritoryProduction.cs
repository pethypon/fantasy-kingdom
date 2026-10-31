using UnityEngine;

public static partial class FacilityData
{
    /// <summary>Position-dependent output only. Costs, inputs, maintenance and upgrade levels are unchanged.</summary>
    public static FacilityLevelData RestrictProductionAt(FacilityLevelData recipe, Vector3 position, Team owner, TerritorySystem territory)
    {
        // Ordinary facilities use their authored production everywhere they
        // already exist. Ownership/territory still controls legal NEW placement.
        return recipe;
    }
    public static bool CanProduceRareOre(Vector3 position, Team owner, TerritorySystem territory)
        => true;
    public static FacilityLevelData GetProductionLevel(Status building, int level, TerritorySystem territory)
        => RestrictProductionAt(GetLevel(building, level), building != null ? building.transform.position : default,
            building != null ? building.team : Team.None, territory);
    public static bool HasRareOreOutput(FacilityLevelData recipe)
        => recipe.Output.Iron > 0 || recipe.Output.MagicOre > 0
            || recipe.BonusChance1 > 0 && (recipe.BonusOutput1.Iron > 0 || recipe.BonusOutput1.MagicOre > 0)
            || recipe.BonusChance2 > 0 && (recipe.BonusOutput2.Iron > 0 || recipe.BonusOutput2.MagicOre > 0);
}

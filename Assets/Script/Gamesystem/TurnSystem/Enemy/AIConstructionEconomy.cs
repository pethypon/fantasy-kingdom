using UnityEngine;

/// <summary>Position-aware own-side construction facts shared by forecasting, reserves and candidate scores.</summary>
public static class AIConstructionEconomy
{
    public static TerritorySystem Territory(AIBoardState board) => board?.TerritorySystem ?? board?.BuildSystem?.Territory
        ?? board?.MapCreate?.GetComponent<TerritorySystem>();
    public static bool IsEngineer(AIAction action) => false;
    public static FacilityData.ResourceCost BuildCost(AIAction action)
    {
        var cost = action?.FacilityDefinition != null ? action.FacilityDefinition.GetInfo().BuildCost
            : action != null && FacilityData.Table.TryGetValue(action.Facility, out var info) ? info.BuildCost : default;
        return cost;
    }
    public static FacilityData.FacilityLevelData WithoutUnlocatedOre(FacilityData.FacilityLevelData recipe)
        => recipe;
    public static bool AllowsOre(AIAction action, AIBoardState board)
    {
        return true;
    }
    public static FacilityData.FacilityLevelData BuildRecipe(AIAction action, AIBoardState board)
    {
        var recipe = action?.FacilityDefinition != null ? action.FacilityDefinition.GetLevel(1)
            : action != null ? FacilityData.GetLevel(action.Facility, 1) : default;
        return AllowsOre(action, board) ? recipe : WithoutUnlocatedOre(recipe);
    }
    public static FacilityData.FacilityLevelData ExistingRecipe(Status actor, int level, AIBoardState board)
        => FacilityData.GetProductionLevel(actor, level, Territory(board));
}

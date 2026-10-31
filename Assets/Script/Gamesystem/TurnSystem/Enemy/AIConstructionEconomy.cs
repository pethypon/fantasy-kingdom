using UnityEngine;

/// <summary>Position-aware own-side construction facts shared by forecasting, reserves and candidate scores.</summary>
public static class AIConstructionEconomy
{
    public static TerritorySystem Territory(AIBoardState board) => board?.TerritorySystem ?? board?.BuildSystem?.Territory
        ?? board?.MapCreate?.GetComponent<TerritorySystem>();
    public static bool IsEngineer(AIAction action) => action?.ActionType == AIActionType.Build
        && action.Unit != null && action.Unit.kind == Kind.Engineer && action.Unit.type == Type.Unit;
    public static FacilityData.ResourceCost BuildCost(AIAction action)
    {
        var cost = action?.FacilityDefinition != null ? action.FacilityDefinition.GetInfo().BuildCost
            : action != null && FacilityData.Table.TryGetValue(action.Facility, out var info) ? info.BuildCost : default;
        return IsEngineer(action) ? R2EconomyRules.EngineerCost(cost) : cost;
    }
    public static FacilityData.FacilityLevelData WithoutUnlocatedOre(FacilityData.FacilityLevelData recipe)
        => FacilityData.RestrictProductionAt(recipe, default, Team.None, null);
    public static bool AllowsOre(AIAction action, AIBoardState board)
    {
        if (board == null || !IsEngineer(action) || !action.Unit.IsAlive || action.Unit.team != board.ActorTeam
            || action.Unit.VisionCell == null || !action.Unit.VisionCell.Contains(GridHelper.ToGrid(action.TargetPos))) return false;
        var territory = Territory(board);
        // Opponent ownership is inspected only at an individually visible placement, never at a hidden goal.
        return FacilityData.CanProduceRareOre(action.TargetPos, action.Unit.team, territory)
            && !territory.IsInTerritory(GridHelper.ToGridXZ(action.TargetPos), board.OpponentTeam);
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

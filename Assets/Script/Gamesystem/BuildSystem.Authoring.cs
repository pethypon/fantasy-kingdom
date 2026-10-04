using UnityEngine;

public partial class BuildSystem
{
    public FacilityDefinitionData SelectedDefinition { get; private set; }

    public void StartBuildMode(FacilityDefinitionData definition)
    {
        if (definition == null || !definition.IsAvailable(Team.Player) || (turnGenerator != null && turnGenerator.DeveloperPlayerAIEnabled)) return;
        StartBuildMode(definition.behaviourKind);
        SelectedDefinition = definition;
    }

    public string GetCostFailure(FacilityDefinitionData definition, Team team)
    {
        if (definition == null || !definition.IsAvailable(team) || factionState == null) return "建築不可：この陣営では使えない建物です";
        if (FacilityData.IsSubCrystal(definition.behaviourKind) && factionState.GetSubCrystals(team) <= 0) return "副晶不足";
        var resources = factionState.GetResources(team); var info = definition.GetInfo(); var cost = info.BuildCost;
        if (resources.Wood < cost.Wood) return "木材不足";
        if (resources.Stone < cost.Stone) return "石材不足";
        if (resources.Iron < cost.Iron) return "鉄不足";
        if (resources.MagicOre < cost.MagicOre) return "魔石不足";
        if (resources.Water < cost.Water) return "水不足";
        if (resources.Citizen < cost.Citizen) return "市民不足";
        return factionState.GetAP(team) < info.APCost ? "AP不足" : null;
    }

    /// <summary>Normal build transaction. Costs are consumed only after a valid instance exists.</summary>
    public bool TryPlaceDefinition(Vector3Int position, FacilityDefinitionData definition, Team team)
    {
        if (definition == null || GetCostFailure(definition, team) != null || mapcreate == null
            || !mapcreate.HasTileAt(position.x, position.z)) return false;
        var kind = definition.behaviourKind;
        bool subCrystal = FacilityData.IsSubCrystal(kind);
        if (subCrystal)
        {
            if (subCrystalSystem == null || !subCrystalSystem.CanPlaceSubCrystal(position, team)) return false;
        }
        else if (!AICheckCanPlace(position, team)) return false;

        var building = InstantiateBuilding(position, kind, team, definition);
        if (building == null) return false;
        apsystem.ConsumeBuild(team, definition, factionState);
        _lastPlacedBuilding = building;
        if (subCrystal)
        {
            factionState.ModifySubCrystals(team, -1);
            subCrystalSystem.ExpandTerritory(building, team);
            turnGenerator?.Systems.DungeonSystem?.ActivateFromSubCrystal(GridHelper.ToGrid(building.transform.position), team);
        }
        turnGenerator?.Systems.VisionGenerator?.MarkVisionDirty();
        turnGenerator?.Systems.RefreshVision();
        return true;
    }
}

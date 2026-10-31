using UnityEngine;

/// <summary>One explicit construction context; cost formatting delegates to the transaction's API.</summary>
public partial class BuildSummonUIBuilder
{
    // Keep a separate flag: a destroyed engineer must never silently turn a 2x order into normal construction.
    Status menuEngineer;
    bool engineerMenuRequested;

    public bool UseNormalConstruction()
    {
        bool changed = engineerMenuRequested;
        engineerMenuRequested = false; menuEngineer = null;
        slidePanel?.SetBuildModeTitle("通常建築 ・ 領土内");
        return changed;
    }

    public bool UseEngineerConstruction(Status engineer)
    {
        if (engineer == null || !engineer.IsAlive || engineer.type != Type.Unit
            || engineer.kind != Kind.Engineer || engineer.team != Team.Player) return false;
        menuEngineer = engineer; engineerMenuRequested = true;
        slidePanel?.SetBuildModeTitle("工兵建築 ・ 資源費2倍");
        return true;
    }

    string BuildMenuFailure(FacilityKind kind)
    {
        if (buildSystem == null) return "建築準備中";
        if (engineerMenuRequested) return buildSystem.GetEngineerBuildCostFailure(menuEngineer, kind);
        return BuildValidator.CostFailure(kind, cachedFactionState);
    }

    string BuildMenuFailure(FacilityDefinitionData definition)
    {
        if (buildSystem == null) return "建築準備中";
        return engineerMenuRequested ? buildSystem.GetEngineerBuildCostFailure(menuEngineer, definition)
            : buildSystem.GetCostFailure(definition, Team.Player);
    }

    FacilityData.ResourceCost BuildCostForMenu(FacilityKind kind)
        => engineerMenuRequested ? buildSystem != null ? buildSystem.GetEngineerBuildCost(kind) : default : FacilityData.Table[kind].BuildCost;
    FacilityData.ResourceCost BuildCostForMenu(FacilityDefinitionData definition)
        => engineerMenuRequested ? buildSystem != null ? buildSystem.GetEngineerBuildCost(definition) : default : definition.GetInfo().BuildCost;
    int BuildAPForMenu(FacilityKind kind)
        => engineerMenuRequested ? buildSystem != null ? buildSystem.GetEngineerBuildAP(kind) : 0 : FacilityData.Table[kind].APCost;
    int BuildAPForMenu(FacilityDefinitionData definition)
        => engineerMenuRequested ? buildSystem != null ? buildSystem.GetEngineerBuildAP(definition) : 0 : definition.GetInfo().APCost;

    void OnAuthoredBuildButtonClicked(FacilityDefinitionData definition)
    {
        if (buildSystem == null || definition == null) return;
        if (engineerMenuRequested)
        {
            if (!buildSystem.StartEngineerBuildMode(menuEngineer, definition))
            {
                ToastMessageUI.Show(BuildMenuFailure(definition) ?? "工兵建築を開始できません", ToastMessageUI.MessageType.Warning);
                RefreshBuildButtons();
                return;
            }
        }
        else buildSystem.StartBuildMode(definition);
        slidePanel?.ClosePanel();
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>Old callbacks fail without spending AP or resources. Ordinary
/// construction is the only active mode; existing buildings are retained.</summary>
public partial class BuildSystem
{
    const string RetiredEngineer = "工兵は廃止されました。通常の建築メニューを使用してください";
    public Status EngineerBuilder { get; private set; }
    public bool IsEngineerBuild => false;
    public TerritorySystem Territory => territorysystem;
    public bool StartEngineerBuildMode(Status engineer, FacilityKind facility) => false;
    public bool StartEngineerBuildMode(Status engineer, FacilityDefinitionData definition) => false;
    public FacilityData.ResourceCost GetEngineerBuildCost(FacilityKind kind)
        => FacilityData.Table.TryGetValue(kind, out var info) ? info.BuildCost : default;
    public FacilityData.ResourceCost GetEngineerBuildCost(FacilityDefinitionData definition)
        => definition != null ? definition.GetInfo().BuildCost : default;
    public int GetEngineerBuildAP(FacilityKind kind)
        => FacilityData.Table.TryGetValue(kind, out var info) ? info.APCost : 0;
    public int GetEngineerBuildAP(FacilityDefinitionData definition) => definition != null ? definition.GetInfo().APCost : 0;
    public string GetEngineerBuildCostFailure(Status engineer, FacilityKind kind) => RetiredEngineer;
    public string GetEngineerBuildCostFailure(Status engineer, FacilityDefinitionData definition) => RetiredEngineer;
    public string GetEngineerPlacementFailure(Status engineer, Vector3Int position, FacilityKind kind) => RetiredEngineer;
    public string GetEngineerPlacementFailure(Status engineer, Vector3Int position, FacilityDefinitionData definition) => RetiredEngineer;
    public bool TryEngineerPlace(Status engineer, Vector3Int position, FacilityKind kind) => false;
    public bool TryEngineerPlace(Status engineer, Vector3Int position, FacilityDefinitionData definition) => false;
    public void CollectEngineerBuildablePositions(Status engineer, List<Vector3Int> result, FacilityKind kind) => result?.Clear();
    public void CollectEngineerBuildablePositions(Status engineer, List<Vector3Int> result, FacilityDefinitionData definition) => result?.Clear();
    void RefreshEngineerRange(bool force = false) { }
    void HideEngineerRange() { }
    void DestroyEngineerRange() { }
}

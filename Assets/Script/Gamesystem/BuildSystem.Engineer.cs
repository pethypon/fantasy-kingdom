using System.Collections.Generic;
using UnityEngine;

/// <summary>Engineer builds use the same creation core and a distinct, revalidated paid command.</summary>
public partial class BuildSystem
{
    public Status EngineerBuilder { get; private set; }
    bool engineerMode;
    public bool IsEngineerBuild => engineerMode;
    public TerritorySystem Territory => territorysystem;
    readonly List<Status> placementActors = new List<Status>(64);
    SkillTargetRangeView engineerRangeView;
    readonly List<Vector3> engineerRange = new List<Vector3>(), engineerLegalRange = new List<Vector3>();
    readonly List<Vector3Int> engineerLegalCells = new List<Vector3Int>();
    int engineerRangeSignature;

    public bool StartEngineerBuildMode(Status engineer, FacilityKind facility)
        => BeginEngineerBuildMode(engineer, facility, null);
    public bool StartEngineerBuildMode(Status engineer, FacilityDefinitionData definition)
        => definition != null && BeginEngineerBuildMode(engineer, definition.behaviourKind, definition);
    bool BeginEngineerBuildMode(Status engineer, FacilityKind facility, FacilityDefinitionData definition)
    {
        if (turnGenerator != null && turnGenerator.DeveloperPlayerAIEnabled || EngineerFailure(engineer) != null
            || !TryEngineerInfo(facility, definition, engineer.team, out _)) return false;
        StartBuildMode(facility);
        EngineerBuilder = engineer;
        engineerMode = true;
        SelectedDefinition = definition;
        RefreshEngineerVision(engineer);
        RefreshEngineerRange(true);
        return IsActive;
    }
    public FacilityData.ResourceCost GetEngineerBuildCost(FacilityKind kind)
        => FacilityData.Table.TryGetValue(kind, out var info) ? R2EconomyRules.EngineerCost(info.BuildCost) : default;
    public FacilityData.ResourceCost GetEngineerBuildCost(FacilityDefinitionData definition)
        => definition != null ? R2EconomyRules.EngineerCost(definition.GetInfo().BuildCost) : default;
    public int GetEngineerBuildAP(FacilityKind kind)
        => FacilityData.Table.TryGetValue(kind, out var info) ? R2EconomyRules.EngineerAP(info.APCost) : 0;
    public int GetEngineerBuildAP(FacilityDefinitionData definition)
        => definition != null ? R2EconomyRules.EngineerAP(definition.GetInfo().APCost) : 0;
    public string GetEngineerBuildCostFailure(Status engineer, FacilityKind kind)
        => EngineerBuildCostFailure(engineer, kind, null);
    public string GetEngineerBuildCostFailure(Status engineer, FacilityDefinitionData definition)
        => definition == null ? "対象外施設" : EngineerBuildCostFailure(engineer, definition.behaviourKind, definition);
    string EngineerBuildCostFailure(Status engineer, FacilityKind kind, FacilityDefinitionData definition)
    {
        string failure = EngineerFailure(engineer);
        if (failure != null) return failure;
        return TryEngineerInfo(kind, definition, engineer.team, out var info) ? EngineerCostFailure(engineer.team, info) : "対象外施設";
    }

    void RefreshEngineerRange(bool force = false)
    {
        if (!IsEngineerBuild || EngineerBuilder == null || !EngineerBuilder.IsAlive) { engineerRangeView?.Hide(); return; }
        var actor = EngineerBuilder;
        int signature = actor.GridPosition.GetHashCode() ^ (int)actor.direction ^ GameContentRevision.Revision;
        unchecked
        {
            signature = signature * 31 + (int)SpecialSkillRuntime.HpRevision;
            signature = signature * 31 + (mapcreate != null ? mapcreate.Environment.Revision : 0);
            signature = signature * 31 + (territorysystem?.PTSetPos?.Count ?? 0);
            signature = signature * 31 + (territorysystem?.ETSetPos?.Count ?? 0);
            signature = signature * 31 + (factionState != null ? factionState.GetAP(actor.team) : 0);
            if (actor.VisionCell != null) foreach (var cell in actor.VisionCell) signature ^= cell.GetHashCode();
        }
        if (!force && signature == engineerRangeSignature) return;
        engineerRangeSignature = signature;
        engineerRange.Clear(); engineerLegalRange.Clear();
        if (!RefreshEngineerVision(actor) || actor.VisionCell == null) return;
        foreach (var cell in actor.VisionCell)
            if (mapcreate.TryGetHeight(cell.x, cell.z, out float height) && Mathf.RoundToInt(height) == cell.y)
                engineerRange.Add(new Vector3(cell.x, height, cell.z));
        CollectEngineerPositions(actor, engineerLegalCells, SelectedFacility, SelectedDefinition);
        foreach (var cell in engineerLegalCells) engineerLegalRange.Add(cell);
        if (engineerRangeView == null) engineerRangeView = new GameObject("Engineer individual build range").AddComponent<SkillTargetRangeView>();
        engineerRangeView.Show(engineerRange, engineerLegalRange);
    }
    void HideEngineerRange() => engineerRangeView?.Hide();
    void DestroyEngineerRange() { if (engineerRangeView != null) Destroy(engineerRangeView.gameObject); }

    public string GetEngineerPlacementFailure(Status engineer, Vector3Int position, FacilityKind kind)
        => EngineerPlacementFailure(engineer, position, kind, null, true);
    public string GetEngineerPlacementFailure(Status engineer, Vector3Int position, FacilityDefinitionData definition)
        => definition == null ? "対象外施設" : EngineerPlacementFailure(engineer, position, definition.behaviourKind, definition, true);
    public bool TryEngineerPlace(Status engineer, Vector3Int position, FacilityKind kind)
        => TryEngineerPlaceCore(engineer, position, kind, null);
    public bool TryEngineerPlace(Status engineer, Vector3Int position, FacilityDefinitionData definition)
        => definition != null && TryEngineerPlaceCore(engineer, position, definition.behaviourKind, definition);
    public void CollectEngineerBuildablePositions(Status engineer, List<Vector3Int> result, FacilityKind kind)
        => CollectEngineerPositions(engineer, result, kind, null);
    public void CollectEngineerBuildablePositions(Status engineer, List<Vector3Int> result, FacilityDefinitionData definition)
    {
        if (definition == null) { result?.Clear(); return; }
        CollectEngineerPositions(engineer, result, definition.behaviourKind, definition);
    }
    void CollectEngineerPositions(Status engineer, List<Vector3Int> result, FacilityKind kind, FacilityDefinitionData definition)
    {
        if (result == null) throw new System.ArgumentNullException(nameof(result));
        result.Clear();
        if (EngineerFailure(engineer) != null || !RefreshEngineerVision(engineer) || engineer.VisionCell == null) return;
        // The actor's finite official mask, never a per-frame full map scan or a shared military vision mask.
        foreach (var cell in engineer.VisionCell)
        {
            if (!mapcreate.TryGetHeight(cell.x, cell.z, out float height)) continue;
            var ground = new Vector3Int(cell.x, Mathf.RoundToInt(height), cell.z);
            if (ground != cell || EngineerPlacementFailure(engineer, ground, kind, definition, false) != null) continue;
            result.Add(ground);
        }
    }
    bool RefreshEngineerVision(Status engineer)
    {
        var vision = turnGenerator?.Systems.VisionGenerator;
        if (vision == null || mapcreate == null) return false;
        vision.RefreshActorVision(engineer, mapcreate, turnGenerator.Systems.CrystalSystem);
        return true;
    }
    string EngineerFailure(Status engineer)
    {
        if (engineer == null || engineer.kind != Kind.Engineer || engineer.type != Type.Unit || !engineer.IsAlive)
            return "工兵を選択してください";
        if (engineer.team != Team.Player && engineer.team != Team.Enemy) return "建築できない陣営です";
        if (turnGenerator == null || turnGenerator.IsGameOver
            || engineer.team == Team.Player && !(turnGenerator.CurrentState is PlayerMove)
            || engineer.team == Team.Enemy && !(turnGenerator.CurrentState is EnemyMove)) return "自軍ターンのみ建築できます";
        if (StatusEffectSystem.IsStunned(engineer) || BoardActionProfile.For(engineer)?.actionType == ActorActionType.Passive)
            return "工兵が行動できない状態です";
        return null;
    }
    static bool TryEngineerInfo(FacilityKind kind, FacilityDefinitionData definition, Team owner, out FacilityData.FacilityInfo info)
    {
        info = default;
        // Provisional R2 policy: territory-generating subcrystals remain on their original construction path.
        if (FacilityData.IsSubCrystal(kind) || !FacilityData.IsKnownKind(kind)) return false;
        if (definition != null)
        {
            if (!definition.IsAvailable(owner) || definition.behaviourKind != kind) return false;
            info = definition.GetInfo(); return true;
        }
        return FacilityData.IsAvailableFor(kind, owner) && FacilityData.Table.TryGetValue(kind, out info);
    }
    string EngineerPlacementFailure(Status engineer, Vector3Int position, FacilityKind kind, FacilityDefinitionData definition,
        bool refreshVision, Status pending = null)
    {
        string failure = EngineerFailure(engineer);
        if (failure != null) return failure;
        if (!TryEngineerInfo(kind, definition, engineer.team, out var info)) return "対象外施設";
        if (mapcreate == null || territorysystem == null || !mapcreate.HasTileAt(position.x, position.z)
            || !mapcreate.TryGetHeight(position.x, position.z, out float height) || Mathf.RoundToInt(height) != position.y)
            return "配置不可：建築できる地面がありません";
        if (refreshVision && !RefreshEngineerVision(engineer)) return "工兵の視界を確認できません";
        if (engineer.VisionCell == null || !engineer.VisionCell.Contains(position)) return "工兵の視界外です";
        // Do not expose unseen hostile ownership through a hover/failed-command reason.
        Team opponent = engineer.team == Team.Player ? Team.Enemy : Team.Player;
        if (territorysystem.IsInTerritory(position, opponent)) return "敵領土内には建築できません";
        if (kind == FacilityKind.Mine && R2EconomyRules.DisallowNewHomeMines && territorysystem.IsInTerritory(position, engineer.team))
            return "鉄・魔石は領土外でのみ採掘可能です";
        if (mapcreate.Environment.BlocksMovement(position.x, position.z)) return "障害物があります";
        if (IsCrystalPosition(position)) return "配置不可：クリスタルのあるマスです";
        if (pending == null && HasBuildingAtXZ(position)) return "既設建物があります";
        CombatRegistry.Collect(placementActors);
        foreach (var actor in placementActors)
            if (actor != pending && GridHelper.MatchXZ(actor.transform.position, position)) return actor.type == Type.Unit ? "配置不可：駒がいるマスです" : "既設建物があります";
        if (pending == null && moveGenerator != null && moveGenerator.IsOccupied(moveGenerator.Cell(position))) return "配置不可：使用中のマスです";
        return EngineerCostFailure(engineer.team, info);
    }
    string EngineerCostFailure(Team team, FacilityData.FacilityInfo info)
    {
        if (factionState == null || apsystem == null) return "建築情報がありません";
        var resources = factionState.GetResources(team); var cost = R2EconomyRules.EngineerCost(info.BuildCost);
        if (resources == null) return "資源情報がありません";
        if (resources.Wood < cost.Wood) return "木材不足";
        if (resources.Stone < cost.Stone) return "石材不足";
        if (resources.Iron < cost.Iron) return "鉄不足";
        if (resources.MagicOre < cost.MagicOre) return "魔石不足";
        if (resources.Water < cost.Water) return "水不足";
        if (resources.Citizen < cost.Citizen) return "市民不足";
        return factionState.GetAP(team) < R2EconomyRules.EngineerAP(info.APCost) ? "AP不足" : null;
    }
    bool TryEngineerPlaceCore(Status engineer, Vector3Int position, FacilityKind kind, FacilityDefinitionData definition)
    {
        if (EngineerPlacementFailure(engineer, position, kind, definition, true) != null
            || !TryEngineerInfo(kind, definition, engineer.team, out var info)) return false;
        var building = InstantiateBuilding(position, kind, engineer.team, definition);
        if (building == null) return false;
        var actor = building.GetComponent<Status>();
        // Prefab Awake/OnEnable can change the world. Validate again, excluding only this unpaid instance.
        if (actor == null || EngineerPlacementFailure(engineer, position, kind, definition, true, actor) != null
            || !apsystem.TryConsumeBuild(engineer.team, R2EconomyRules.EngineerCost(info.BuildCost), R2EconomyRules.EngineerAP(info.APCost), factionState))
        { RollBackEngineerBuilding(building, actor, position); return false; }
        actor.BuiltByEngineer = true;
        _lastPlacedBuilding = building;
        turnGenerator.Systems.MoveUndoSystem?.Clear();
        turnGenerator.Systems.RefreshVision();
        BoardActionFeedback.Build(actor);
        return true;
    }
    void RollBackEngineerBuilding(GameObject building, Status actor, Vector3Int position)
    {
        if (actor != null) { UnitRegistry.Instance?.Unregister(actor); CombatRegistry.Unregister(actor); }
        building.SetActive(false);
        building.transform.SetParent(null);
        RemoveBuildingPosition(position);
        bool occupied = false;
        CombatRegistry.Collect(placementActors);
        foreach (var other in placementActors)
            if (GridHelper.MatchXZ(other.transform.position, position))
            {
                if (other.type == Type.Building || other.type == Type.Wall) buildingPositions.Add(GridHelper.ToGrid(other.transform.position));
                if (other.type == Type.Unit || other.type == Type.Wall) occupied = true;
            }
        if (!occupied && FacilityData.IsWall(actor != null ? actor.facilityKind : default)) moveGenerator?.RemoveOccupied(GridHelper.ToUnitPoint(position));
        Destroy(building);
        EconomySystem.RecalculateCapacity(this, factionState, actor != null ? actor.team : Team.None);
    }
}

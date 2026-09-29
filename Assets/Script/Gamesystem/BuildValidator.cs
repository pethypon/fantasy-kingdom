using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 建築設置可否の判定ロジック（BuildSystem から抽出）。
/// 領地判定・座標スナップ・クランプなどを担当する。
/// </summary>
public class BuildValidator
{
    private readonly TerritorySystem territorysystem;
    private readonly MapCreate mapcreate;
    private readonly MoveGenerator moveGenerator;
    private readonly HashSet<Vector3Int> buildingPositions;

    public BuildValidator(TerritorySystem territorysystem, MapCreate mapcreate,
                          MoveGenerator moveGenerator, HashSet<Vector3Int> buildingPositions)
    {
        this.territorysystem = territorysystem;
        this.mapcreate = mapcreate;
        this.moveGenerator = moveGenerator;
        this.buildingPositions = buildingPositions;
    }

    // ==================================================================
    //  設置可否チェック
    // ==================================================================

    /// <summary>
    /// プレイヤー向け設置可否チェック。
    /// サブクリスタルの場合は SubCrystalSystem に委譲、通常建築は領地・クリスタル・既設チェック。
    /// </summary>
    public bool CheckCanPlace(Vector3Int pos, FacilityKind facility,
                              SubCrystalSystem subCrystalSystem,
                              CrystalSystem crystalsystem)
    {
        return PlacementFailure(pos,facility,subCrystalSystem,crystalsystem) == null;
    }

    public string PlacementFailure(Vector3Int pos, FacilityKind facility, SubCrystalSystem subCrystalSystem, CrystalSystem crystalsystem)
    {
        if (mapcreate == null || !mapcreate.HasTileAt(pos.x,pos.z)) return "配置不可：建築できる地面がありません";
        if (FacilityData.IsSubCrystal(facility))
            return subCrystalSystem != null && subCrystalSystem.CanPlaceSubCrystal(pos,Team.Player)
                ? null : "配置不可：視界内・領地から離れた空き地が必要です";
        if (territorysystem == null || !territorysystem.IsInTerritory(pos,Team.Player)) return "領地外：自分の領地に建築してください";
        if (crystalsystem != null && (GridHelper.ToGrid(crystalsystem.PCP)==pos || GridHelper.ToGrid(crystalsystem.ECP)==pos))
            return "配置不可：クリスタルのあるマスです";
        if (buildingPositions.Contains(pos)) return "配置不可：建物があるマスです";
        return null;
    }

    public static string CostFailure(FacilityKind facility, FactionState faction)
    {
        if (faction == null || !FacilityData.Table.TryGetValue(facility,out var info)) return "配置不可：建築情報がありません";
        if (FacilityData.IsSubCrystal(facility)) return faction.GetSubCrystals(Team.Player)>0 ? null : "副晶不足";
        var r=faction.PlayerResources; var c=info.BuildCost;
        if (r.Wood<c.Wood) return "木材不足";
        if (r.Stone<c.Stone) return "石材不足";
        if (r.Iron<c.Iron) return "鉄不足";
        if (r.MagicOre<c.MagicOre) return "魔石不足";
        if (r.Water<c.Water) return "水不足";
        if (r.Citizen<c.Citizen) return "市民不足";
        return faction.GetAP(Team.Player)<info.APCost ? "AP不足" : null;
    }

    // ==================================================================
    //  領地外の最も近い座標にクランプ（サブクリスタル用）
    // ==================================================================

    /// <summary>指定座標に最も近い領地外座標を返す</summary>
    public Vector3Int ClampToOutsideTerritory(Vector3Int pos)
    {
        if (mapcreate.SetPos == null || mapcreate.SetPos.Count == 0)
            return new Vector3Int(int.MinValue, 0, 0);

        float minDist = float.MaxValue;
        Vector3 closest = Vector3.zero;
        bool found = false;

        foreach (var p in mapcreate.SetPos)
        {
            var cell = GridHelper.ToGridXZ(p);

            // 領地内のマスはスキップ
            if (territorysystem.IsInAnyTerritory(cell.x, cell.z))
                continue;

            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
                found = true;
            }
        }

        if (!found)
            return new Vector3Int(int.MinValue, 0, 0);

        return GridHelper.ToGrid(closest);
    }
}

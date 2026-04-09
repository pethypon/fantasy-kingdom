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
        if (FacilityData.IsSubCrystal(facility))
        {
            if (subCrystalSystem == null) return false;
            return subCrystalSystem.CanPlaceSubCrystal(pos, Team.Player);
        }

        // 通常建築物: 領地内でなければ不可
        if (!territorysystem.IsInTerritory(pos, Team.Player)) return false;

        // クリスタル位置チェック
        Vector3Int pcp = GridHelper.ToGrid(crystalsystem.PCP);
        if (pos == pcp) return false;

        Vector3Int ecp = GridHelper.ToGrid(crystalsystem.ECP);
        if (pos == ecp) return false;

        // 既設の建築物チェック
        if (buildingPositions.Contains(pos)) return false;

        return true;
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
            int px = Mathf.RoundToInt(p.x);
            int pz = Mathf.RoundToInt(p.z);

            // 領地内のマスはスキップ
            if (territorysystem.IsInAnyTerritory(px, pz))
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

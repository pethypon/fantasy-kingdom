using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 建築設置可否の判定ロジック（BuildSystem から抽出）。
/// 領地判定・座標スナップ・クランプなどを担当する。
/// </summary>
public class BuildValidator
{
    private readonly TerritorySystem territorysystem;
    private readonly MapCreate mapcreate;
    private readonly MoveGererater movegenerater;
    private readonly HashSet<Vector3Int> buildingPositions;

    public BuildValidator(TerritorySystem territorysystem, MapCreate mapcreate,
                          MoveGererater movegenerater, HashSet<Vector3Int> buildingPositions)
    {
        this.territorysystem = territorysystem;
        this.mapcreate = mapcreate;
        this.movegenerater = movegenerater;
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
        bool isSubCrystal = FacilityData.IsSubCrystal(facility);

        if (isSubCrystal)
        {
            if (subCrystalSystem == null) return false;
            return subCrystalSystem.CanPlaceSubCrystal(pos, Team.Player);
        }

        // 通常建築物: 領地内でなければ不可
        if (!IsInTerritory(pos)) return false;

        // クリスタル位置チェック
        Vector3 pcpVec = crystalsystem.PCP;
        Vector3Int pcp = new Vector3Int(
            Mathf.RoundToInt(pcpVec.x),
            Mathf.RoundToInt(pcpVec.y),
            Mathf.RoundToInt(pcpVec.z));
        if (pos == pcp) return false;

        Vector3 ecpVec = crystalsystem.ECP;
        Vector3Int ecp = new Vector3Int(
            Mathf.RoundToInt(ecpVec.x),
            Mathf.RoundToInt(ecpVec.y),
            Mathf.RoundToInt(ecpVec.z));
        if (pos == ecp) return false;

        // 既設の建築物チェック
        if (buildingPositions.Contains(pos)) return false;

        return true;
    }

    // ==================================================================
    //  領地判定
    // ==================================================================

    /// <summary>プレイヤー領地内かどうかを判定する</summary>
    public bool IsInTerritory(Vector3Int pos)
    {
        if (territorysystem.PTSetPos == null) return false;
        return territorysystem.PTSetPos.Any(p =>
            Mathf.RoundToInt(p.x) == pos.x && Mathf.RoundToInt(p.z) == pos.z);
    }

    // ==================================================================
    //  領地端へクランプ
    // ==================================================================

    /// <summary>指定座標に最も近い領地内座標を返す</summary>
    public Vector3Int ClampToTerritory(Vector3Int pos)
    {
        if (territorysystem.PTSetPos == null || territorysystem.PTSetPos.Count == 0)
            return new Vector3Int(int.MinValue, 0, 0);

        float minDist = float.MaxValue;
        Vector3 closest = territorysystem.PTSetPos[0];

        foreach (var p in territorysystem.PTSetPos)
        {
            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
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
            bool inTerritory = false;
            if (territorysystem.PTSetPos != null)
                inTerritory |= territorysystem.PTSetPos.Any(t =>
                    Mathf.RoundToInt(t.x) == px && Mathf.RoundToInt(t.z) == pz);
            if (territorysystem.ETSetPos != null)
                inTerritory |= territorysystem.ETSetPos.Any(t =>
                    Mathf.RoundToInt(t.x) == px && Mathf.RoundToInt(t.z) == pz);
            if (inTerritory) continue;

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

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
    }

    // ==================================================================
    //  SetPos 上の最も近い座標にスナップ
    // ==================================================================

    /// <summary>指定グリッド座標を SetPos 上の最も近い座標にスナップする</summary>
    public Vector3Int SnapToSetPos(Vector3Int gridPos)
    {
        if (mapcreate.SetPos == null || mapcreate.SetPos.Count == 0)
            return gridPos;

        float minDist = float.MaxValue;
        Vector3 closest = mapcreate.SetPos[0];

        foreach (var p in mapcreate.SetPos)
        {
            float dx = p.x - gridPos.x;
            float dz = p.z - gridPos.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }

        return new Vector3Int(
            Mathf.RoundToInt(closest.x),
            Mathf.RoundToInt(closest.y),
            Mathf.RoundToInt(closest.z));
    }
}

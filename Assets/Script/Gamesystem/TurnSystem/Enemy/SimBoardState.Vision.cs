using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimBoardState.Vision — 視界推定 (Raycast不要の近似)
// =====================================================================
public partial class SimBoardState
{
    // ---- 視界推定用の簡易視界半径テーブル (Raycast不要な近似) ----
    static readonly Dictionary<Kind, int> VisionRadiusMap = new Dictionary<Kind, int>
    {
        { Kind.Crystal, 3 }, { Kind.SubCrystal, 2 },
        { Kind.King, 2 }, { Kind.Knight, 2 }, { Kind.Archer, 3 },
        { Kind.Magic, 2 }, { Kind.Assassin, 2 }, { Kind.Scout, 2 },
        { Kind.Priest, 1 }, { Kind.Guardian, 2 }, { Kind.Crossbow, 2 },
        { Kind.Magicsniper, 4 }, { Kind.Bomber, 3 }, { Kind.Boss, 3 },
    };

    /// <summary>
    /// あるチームの推定視界セル数を返す（Raycast不要な簡易近似）。
    /// 各ユニットの視界半径内のマップタイルを合算（重複除去）。
    /// </summary>
    public int EstimateVisionCells(Team team)
    {
        var seen = new HashSet<Vector3Int>();
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            if (!u.IsAlive || u.Team != team) continue;
            int radius;
            if (!VisionRadiusMap.TryGetValue(u.Kind, out radius)) radius = 2;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dz) > radius) continue;
                    var cell = new Vector3Int(u.Position.x + dx, 0, u.Position.z + dz);
                    if (MapTiles.Contains(cell)) seen.Add(cell);
                }
            }
        }
        return seen.Count;
    }

    /// <summary>
    /// あるユニットが特定位置に移動した場合に新たに見えるセル数を推定。
    /// </summary>
    public int EstimateNewVisionCellsAt(SimUnit unit, Vector3Int newPos, Team team)
    {
        // 現在のチーム視界を構築
        var currentVision = new HashSet<Vector3Int>();
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            if (!u.IsAlive || u.Team != team) continue;
            if (u.Id == unit.Id) continue; // 移動するユニットは除外
            int r;
            if (!VisionRadiusMap.TryGetValue(u.Kind, out r)) r = 2;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dz) > r) continue;
                    var c = new Vector3Int(u.Position.x + dx, 0, u.Position.z + dz);
                    if (MapTiles.Contains(c)) currentVision.Add(c);
                }
        }

        // 新位置での視界を計算
        int newCells = 0;
        int radius;
        if (!VisionRadiusMap.TryGetValue(unit.Kind, out radius)) radius = 2;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dz) > radius) continue;
                var c = new Vector3Int(newPos.x + dx, 0, newPos.z + dz);
                if (MapTiles.Contains(c) && !currentVision.Contains(c))
                    newCells++;
            }
        return newCells;
    }
}

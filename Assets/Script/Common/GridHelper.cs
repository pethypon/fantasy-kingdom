using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// グリッド座標の変換・検索・距離計算を一元管理するユーティリティクラス。
/// Mathf.RoundToInt によるワールド→グリッド変換の重複を排除する。
/// </summary>
public static class GridHelper
{
    // =====================================================================
    //  座標変換
    // =====================================================================

    /// <summary>ワールド座標をグリッド座標（整数）に変換する</summary>
    public static Vector3Int ToGrid(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(pos.x),
            Mathf.RoundToInt(pos.y),
            Mathf.RoundToInt(pos.z));
    }

    /// <summary>ワールド座標をXZ平面のグリッド座標（Y=0）に変換する</summary>
    public static Vector3Int ToGridXZ(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(pos.x),
            0,
            Mathf.RoundToInt(pos.z));
    }

    /// <summary>Vector3Int を Y=0 の UnitPoint 形式 Vector3 に変換する</summary>
    public static Vector3 ToUnitPoint(Vector3Int pos)
    {
        return new Vector3(pos.x, 0f, pos.z);
    }

    // =====================================================================
    //  XZ 座標比較（Y を無視した比較）
    // =====================================================================

    /// <summary>XZ座標のみで一致するかを判定する</summary>
    public static bool MatchXZ(Vector3 a, int x, int z)
    {
        return Mathf.RoundToInt(a.x) == x && Mathf.RoundToInt(a.z) == z;
    }

    /// <summary>XZ座標のみで一致するかを判定する</summary>
    public static bool MatchXZ(Vector3 a, Vector3Int b)
    {
        return Mathf.RoundToInt(a.x) == b.x && Mathf.RoundToInt(a.z) == b.z;
    }

    /// <summary>2つの Vector3Int がXZ座標で一致するかを判定する</summary>
    public static bool MatchXZ(Vector3Int a, Vector3Int b)
    {
        return a.x == b.x && a.z == b.z;
    }

    // =====================================================================
    //  距離計算
    // =====================================================================

    /// <summary>チェビシェフ距離（8方向移動での距離）を返す</summary>
    public static int ChebyshevDistance(Vector3Int a, Vector3Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));
    }

    /// <summary>ワールド座標間のチェビシェフ距離を返す</summary>
    public static float ChebyshevDistance(Vector3 a, Vector3 b)
    {
        float dx = Mathf.Abs(Mathf.RoundToInt(a.x) - Mathf.RoundToInt(b.x));
        float dz = Mathf.Abs(Mathf.RoundToInt(a.z) - Mathf.RoundToInt(b.z));
        return Mathf.Max(dx, dz);
    }

    /// <summary>2点がチェビシェフ距離で指定範囲内にあるかを判定する</summary>
    public static bool IsWithinRange(Vector3Int a, Vector3Int b, int range)
    {
        return Mathf.Abs(a.x - b.x) <= range && Mathf.Abs(a.z - b.z) <= range;
    }

    // =====================================================================
    //  SetPos 操作（MapCreate.SetPos を対象とした検索・スナップ）
    // =====================================================================

    /// <summary>SetPos リストから指定 XZ 座標の Y 値を検索する</summary>
    public static bool TryGetHeight(List<Vector3> setPos, int x, int z, out float y)
    {
        y = 0f;
        if (setPos == null) return false;

        foreach (var sp in setPos)
        {
            if (Mathf.RoundToInt(sp.x) == x && Mathf.RoundToInt(sp.z) == z)
            {
                y = sp.y;
                return true;
            }
        }
        return false;
    }

    /// <summary>SetPos リストで指定 XZ 座標にタイルが存在するかを判定する</summary>
    public static bool ExistsOnMap(List<Vector3> setPos, int x, int z)
    {
        if (setPos == null) return false;

        foreach (var sp in setPos)
        {
            if (Mathf.RoundToInt(sp.x) == x && Mathf.RoundToInt(sp.z) == z)
                return true;
        }
        return false;
    }

    /// <summary>SetPos リストで最も近い座標にスナップする</summary>
    public static Vector3Int SnapToNearest(List<Vector3> positions, Vector3Int target)
    {
        if (positions == null || positions.Count == 0)
            return target;

        float minDist = float.MaxValue;
        Vector3 closest = positions[0];

        foreach (var p in positions)
        {
            float dx = p.x - target.x;
            float dz = p.z - target.z;
            float dist = dx * dx + dz * dz;
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }

        return ToGrid(closest);
    }

    /// <summary>SetPos リストを XZ→Y のルックアップ辞書に変換する</summary>
    public static Dictionary<(int, int), int> BuildHeightLookup(List<Vector3> setPos)
    {
        var lookup = new Dictionary<(int, int), int>();
        if (setPos == null) return lookup;

        foreach (var sp in setPos)
        {
            var key = (Mathf.RoundToInt(sp.x), Mathf.RoundToInt(sp.z));
            if (!lookup.ContainsKey(key))
                lookup[key] = Mathf.RoundToInt(sp.y);
        }
        return lookup;
    }

    // =====================================================================
    //  リスト検索
    // =====================================================================

    /// <summary>座標リストから指定 XZ に一致する座標があるかを判定する</summary>
    public static bool ContainsXZ(List<Vector3> positions, int x, int z)
    {
        if (positions == null) return false;

        foreach (var p in positions)
        {
            if (Mathf.RoundToInt(p.x) == x && Mathf.RoundToInt(p.z) == z)
                return true;
        }
        return false;
    }

    /// <summary>座標リストから指定 XZ に一致する座標があるかを判定する</summary>
    public static bool ContainsXZ(List<Vector3> positions, Vector3Int target)
    {
        return ContainsXZ(positions, target.x, target.z);
    }
}

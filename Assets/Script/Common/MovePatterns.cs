using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 駒の移動パターンを一元管理する静的クラス。
/// MoveGenerator と SimActionGenerator の両方から参照される。
/// dx = 目標X - 現在X, dz = 目標Z - 現在Z（符号付き）。
/// 方向依存の駒は dz に方向係数（N=+1, S=-1）を掛けてから判定する。
/// </summary>
public static class MovePatterns
{
    /// <summary>
    /// 方向非依存の駒の集合。これらは dz を反転しない。
    /// </summary>
    public static readonly HashSet<Kind> DirectionIndependent = new HashSet<Kind>
    {
        Kind.King, Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin, Kind.Boss
    };

    /// <summary>
    /// 駒の種類ごとの移動判定辞書。
    /// Key: Kind, Value: (dx, dz) => 移動可能か
    /// 方向依存の駒は呼び出し側で dz に DirZ() を掛けること。
    /// </summary>
    public static readonly Dictionary<Kind, Func<float, float, bool>> Map =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // 全方向1マス
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1
                                     && Mathf.Abs(dz) <= 1 },

        // 上下左右1マス
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },

        // 斜め1マス
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // 斜め1マス（Archerと同じ）
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // ナイト跳び（2×1 or 1×2）
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },

        // 左右1＋前後1 or 直進2
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // 前斜め1 or 後方直進1
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },

        // 左右2マス or 前後1マス
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // 前直進1-3マス or 斜め後ろ1マス
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },

        // 右前斜め1 or 左右各3マス
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },

        // 斜め前後2パターン
        { Kind.Bomber,      (dx, dz) => (dx == -1 && dz ==  1) || (dx ==  2 && dz ==  2)
                                     || (dx ==  1 && dz == -1) || (dx == -2 && dz == -2) },

        // BOSS: 周囲1マス移動
        { Kind.Boss,        (dx, dz) => Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1
                                     && !(dx == 0 && dz == 0) },
    };

    /// <summary>
    /// 方向係数を返す。Direction.S なら -1, Direction.N なら +1。
    /// </summary>
    public static int DirZ(Direction d) => d == Direction.S ? -1 : 1;

    /// <summary>
    /// 指定 Kind が移動可能か判定する（方向を考慮）。
    /// </summary>
    public static bool CanMove(Kind kind, Direction dir, float dx, float dz)
    {
        if (!Map.TryGetValue(kind, out var predicate)) return false;
        if (!DirectionIndependent.Contains(kind))
        {
            dz *= DirZ(dir);
        }
        return predicate(dx, dz);
    }
}

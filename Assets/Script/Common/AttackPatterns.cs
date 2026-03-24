using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 駒の攻撃パターンを一元管理する静的クラス。
/// AttackPointt と SimActionGenerator の両方から参照される。
/// dx = 目標X - 現在X, dz = 目標Z - 現在Z（符号付き）。
/// </summary>
public static class AttackPatterns
{
    /// <summary>
    /// 方向非依存の攻撃を持つ駒の集合。
    /// </summary>
    public static readonly HashSet<Kind> DirectionIndependent = new HashSet<Kind>
    {
        Kind.Magic, Kind.Scout, Kind.Magicsniper
    };

    /// <summary>
    /// 駒の種類ごとの通常攻撃範囲判定辞書。
    /// </summary>
    public static readonly Dictionary<Kind, Func<float, float, bool>> NormalMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // 前方3マス（左右1・正面）
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方3マス
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // 前方直進2・3マス
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // 十字方向2マス
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // 前斜め1マス
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // 左右各1マス
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // 前直進1マス
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // 前直進1・2マス
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // 左右各4マス
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // 前直進3マス
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },

        // 隣接1マス（前方3マス）
        { Kind.Priest,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // BOSS: 前方3マス+左右1マス
        { Kind.Boss,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },
    };

    /// <summary>
    /// 駒ごとのスキル攻撃位置（標準）。
    /// Vector2Int(x, z) でオフセットを定義。
    /// </summary>
    public static readonly Dictionary<Kind, Vector2Int[]> SkillAttackPositions =
        new Dictionary<Kind, Vector2Int[]>
    {
        { Kind.King,        new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
        { Kind.Knight,      new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
        { Kind.Archer,      new[] { new Vector2Int(0,2), new Vector2Int(0,3) } },
        { Kind.Magic,       new[] { new Vector2Int(-2,0), new Vector2Int(2,0), new Vector2Int(0,2), new Vector2Int(0,-2) } },
        { Kind.Assassin,    new[] { new Vector2Int(-1,1), new Vector2Int(1,1) } },
        { Kind.Scout,       new[] { new Vector2Int(-1,0), new Vector2Int(1,0) } },
        { Kind.Guardian,    new[] { new Vector2Int(0,1) } },
        { Kind.Crossbow,    new[] { new Vector2Int(0,1), new Vector2Int(0,2) } },
        { Kind.Magicsniper, new[] { new Vector2Int(-4,0), new Vector2Int(4,0) } },
        { Kind.Bomber,      new[] { new Vector2Int(0,3) } },
        { Kind.Priest,      new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
        { Kind.Boss,        new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
    };

    /// <summary>
    /// スキル固有の攻撃位置（スキルIDで特定される特殊パターン）。
    /// </summary>
    public static readonly Dictionary<int, Vector2Int[]> SkillFixedPositions =
        new Dictionary<int, Vector2Int[]>
    {
        // ワイドスイング(7): 周囲8マス
        { 7,  new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1),
                      new Vector2Int(-1,0), new Vector2Int(1,0),
                      new Vector2Int(-1,-1), new Vector2Int(0,-1), new Vector2Int(1,-1) } },
        // ピアシングショット(8): 前方直線3マス
        { 8,  new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3) } },
        // ブレイクランス(22): 前方直線4マス
        { 22, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4) } },
        // ペネトレイトレイン(37): 前方直線5マス
        { 37, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4), new Vector2Int(0,5) } },
        // ワールドエッジ(48): 前方直線7マス
        { 48, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4),
                      new Vector2Int(0,5), new Vector2Int(0,6), new Vector2Int(0,7) } },
    };

    /// <summary>
    /// 通常攻撃の判定（方向を考慮）。
    /// </summary>
    public static bool CanAttack(Kind kind, Direction dir, float dx, float dz)
    {
        if (!NormalMap.TryGetValue(kind, out var predicate)) return false;
        if (!DirectionIndependent.Contains(kind))
        {
            dz *= MovePatterns.DirZ(dir);
        }
        return predicate(dx, dz);
    }
}

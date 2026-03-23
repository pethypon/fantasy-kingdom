using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimActionGenerator — SimBoardState上で候補行動を生成する
//  MoveGererater / AttackPointt の移動・攻撃パターンを純粋関数として再現
//  GameObjectに一切依存しない
// =====================================================================
public static class SimActionGenerator
{
    // ================================================================
    //  移動パターン (MoveGererater.MovePredicateMap と同一)
    // ================================================================
    static readonly Dictionary<Kind, Func<float, float, bool>> MovePredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1
                                     && !(dx == 0 && dz == 0) },
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 1 },
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 1 },
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },
        { Kind.Bomber,      (dx, dz) => (dx == -1 && dz == 1) || (dx == 2 && dz == 2)
                                     || (dx == 1 && dz == -1) || (dx == -2 && dz == -2) },
        { Kind.Boss,        (dx, dz) => Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1
                                     && !(dx == 0 && dz == 0) },
    };

    // ================================================================
    //  攻撃パターン (AttackPointt.AttackPredicateMap と同一)
    // ================================================================
    static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },
        { Kind.Boss,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },
    };

    // ================================================================
    //  候補行動生成: あるチームの全ユニットの全行動を列挙
    // ================================================================
    public static List<SimAction> GenerateAllActions(SimBoardState board, Team team)
    {
        var actions = new List<SimAction>();
        int ap = board.GetAP(team);
        if (ap <= 0) return actions;

        var units = board.GetAliveUnits(team);
        Team enemyTeam = team == Team.Enemy ? Team.Player : Team.Enemy;

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            GenerateMoveActions(board, unit, ap, actions);
            GenerateAttackActions(board, unit, enemyTeam, ap, actions);
            GenerateSkillActions(board, unit, enemyTeam, team, ap, actions);
        }

        // 建築候補 (AIのみ, 探索では重要度が低いが戦略的に必要)
        if (team == Team.Enemy)
        {
            GenerateBuildActions(board, ap, actions);
        }

        return actions;
    }

    // ================================================================
    //  移動候補
    // ================================================================
    static void GenerateMoveActions(SimBoardState board, SimUnit unit, int ap,
        List<SimAction> results)
    {
        if (!MovePredicateMap.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcMoveCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;

        foreach (var tile in board.MapTiles)
        {
            float dx = tile.x - pos.x;
            float dz = tile.z - pos.z;

            if (!predicate(dx, dz)) continue;
            if (board.IsOccupied(tile)) continue;

            results.Add(new SimAction
            {
                Type = SimActionType.Move,
                UnitId = unit.Id,
                TargetPos = tile,
                APCost = cost
            });
        }
    }

    // ================================================================
    //  攻撃候補
    // ================================================================
    static void GenerateAttackActions(SimBoardState board, SimUnit unit, Team enemyTeam,
        int ap, List<SimAction> results)
    {
        if (!AttackPredicateMap.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcAttackCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;
        var enemies = board.GetAliveUnits(enemyTeam);

        // クリスタルも攻撃対象に含める
        var enemyCrystal = board.GetCrystal(enemyTeam);

        for (int i = 0; i < enemies.Count; i++)
        {
            var target = enemies[i];
            float dx = target.Position.x - pos.x;
            float dz = target.Position.z - pos.z;

            if (predicate(dx, dz))
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.Attack,
                    UnitId = unit.Id,
                    TargetUnitId = target.Id,
                    TargetPos = target.Position,
                    APCost = cost
                });
            }
        }

        // クリスタル攻撃
        if (enemyCrystal != null)
        {
            float dx = enemyCrystal.Position.x - pos.x;
            float dz = enemyCrystal.Position.z - pos.z;
            if (predicate(dx, dz))
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.Attack,
                    UnitId = unit.Id,
                    TargetUnitId = enemyCrystal.Id,
                    TargetPos = enemyCrystal.Position,
                    APCost = cost
                });
            }
        }
    }

    // ================================================================
    //  スキル候補 (攻撃スキル・回復スキルのみ)
    // ================================================================
    static void GenerateSkillActions(SimBoardState board, SimUnit unit, Team enemyTeam,
        Team allyTeam, int ap, List<SimAction> results)
    {
        if (unit.AssignedSkillId < 0) return;
        if (unit.SkillCooldown > 0) return;
        if (!SkillData.Table.TryGetValue(unit.AssignedSkillId, out var skill)) return;
        if (skill.APCost > ap) return;

        // 攻撃スキル → 敵に使用
        if (skill.Multiplier > 0)
        {
            var targets = board.GetAliveUnits(enemyTeam);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                float dist = SimUtil.Distance(unit.Position, t.Position);
                // スキル射程を概算 (通常攻撃範囲+1)
                if (dist <= 5f)
                {
                    results.Add(new SimAction
                    {
                        Type = SimActionType.SkillUse,
                        UnitId = unit.Id,
                        TargetUnitId = t.Id,
                        TargetPos = t.Position,
                        APCost = skill.APCost,
                        SkillId = unit.AssignedSkillId
                    });
                }
            }
        }

        // 回復スキル → 味方に使用
        if (skill.FixedHeal > 0)
        {
            var allies = board.GetAliveUnits(allyTeam);
            for (int i = 0; i < allies.Count; i++)
            {
                var a = allies[i];
                if (a.Id == unit.Id) continue;
                if (a.HP >= a.MaxHP) continue; // 満タンの味方には不要
                float dist = SimUtil.Distance(unit.Position, a.Position);
                if (dist <= 4f)
                {
                    results.Add(new SimAction
                    {
                        Type = SimActionType.SkillUse,
                        UnitId = unit.Id,
                        TargetUnitId = a.Id,
                        TargetPos = a.Position,
                        APCost = skill.APCost,
                        SkillId = unit.AssignedSkillId
                    });
                }
            }
        }
    }

    // ================================================================
    //  建築候補 (探索用: 主要施設のみ)
    // ================================================================
    static void GenerateBuildActions(SimBoardState board, int ap, List<SimAction> results)
    {
        // 探索中は詳細な資源チェックができないため、
        // AP余裕がある場合に代表的な建築を候補に加える
        FacilityKind[] priorities = {
            FacilityKind.Well, FacilityKind.LoggingCamp, FacilityKind.Quarry,
            FacilityKind.Field, FacilityKind.House, FacilityKind.Bakery,
            FacilityKind.LumberMill, FacilityKind.StoneWorks,
        };

        foreach (var fk in priorities)
        {
            if (!FacilityData.Table.TryGetValue(fk, out var info)) continue;
            if (info.APCost > ap) continue;

            int existing = 0;
            board.EnemyBuildingCounts.TryGetValue(fk, out existing);
            if (existing >= 3) continue; // 上限超え防止

            // 建築位置はクリスタル近くと仮定
            results.Add(new SimAction
            {
                Type = SimActionType.Build,
                Facility = fk,
                TargetPos = board.EnemyCrystalPos, // 概算位置
                APCost = info.APCost
            });
        }
    }

    // ================================================================
    //  単一行動のスコアリング (探索のノード選択用: 簡易評価)
    //  上位N候補を絞り込むために使用。完全な評価はSimBoardEvaluatorで行う。
    // ================================================================
    public static float QuickScore(SimAction action, SimBoardState board)
    {
        float score = 0f;

        switch (action.Type)
        {
            case SimActionType.Attack:
            {
                var attacker = board.GetUnit(action.UnitId);
                var target = board.GetUnit(action.TargetUnitId);
                if (attacker != null && target != null)
                {
                    int dmg = SimBoardState.CalcDamage(attacker, target);
                    bool wouldKill = dmg >= target.HP;

                    score += dmg * 2f;
                    if (wouldKill)
                    {
                        score += GetPieceValue(target) * 2f;
                        if (target.Kind == Kind.Crystal) score += 200f;
                    }
                }
                break;
            }

            case SimActionType.SkillUse:
            {
                var unit = board.GetUnit(action.UnitId);
                var target = board.GetUnit(action.TargetUnitId);
                if (unit != null && target != null && SkillData.Table.TryGetValue(action.SkillId, out var skill))
                {
                    if (skill.Multiplier > 0)
                    {
                        int dmg = SimBoardState.CalcSkillDamage(unit, target, skill);
                        score += dmg * 1.5f;
                        if (dmg >= target.HP)
                            score += GetPieceValue(target) * 1.5f;
                    }
                    if (skill.FixedHeal > 0)
                    {
                        float healValue = Mathf.Min(skill.FixedHeal, target.MaxHP - target.HP);
                        score += healValue * 0.5f;
                    }
                }
                break;
            }

            case SimActionType.Move:
            {
                var unit = board.GetUnit(action.UnitId);
                if (unit != null)
                {
                    // 敵クリスタルへの接近
                    Vector3Int enemyCrystal = unit.Team == Team.Enemy
                        ? board.PlayerCrystalPos : board.EnemyCrystalPos;
                    float distBefore = SimUtil.Distance(unit.Position, enemyCrystal);
                    float distAfter = SimUtil.Distance(action.TargetPos, enemyCrystal);
                    score += (distBefore - distAfter) * 2f;

                    // 敵ユニットへの接近
                    Team enemyTeam = unit.Team == Team.Enemy ? Team.Player : Team.Enemy;
                    var enemies = board.GetAliveUnits(enemyTeam);
                    float nearestBefore = float.MaxValue;
                    float nearestAfter = float.MaxValue;
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        float db = SimUtil.Distance(unit.Position, enemies[i].Position);
                        float da = SimUtil.Distance(action.TargetPos, enemies[i].Position);
                        if (db < nearestBefore) nearestBefore = db;
                        if (da < nearestAfter) nearestAfter = da;
                    }
                    if (nearestBefore < float.MaxValue)
                        score += (nearestBefore - nearestAfter) * 1.5f;

                    // 自陣クリスタル防衛
                    Vector3Int ownCrystal = unit.Team == Team.Enemy
                        ? board.EnemyCrystalPos : board.PlayerCrystalPos;
                    float crystalDist = SimUtil.Distance(action.TargetPos, ownCrystal);
                    if (crystalDist < 4f) score += 3f;
                }
                break;
            }

            case SimActionType.Build:
                score += 8f; // 建築は基本的に有益
                break;
        }

        // AP効率: コストが高い行動は若干減点
        score -= action.APCost * 0.3f;

        return score;
    }

    // ================================================================
    //  駒の価値 (評価関数共有)
    // ================================================================
    public static float GetPieceValue(SimUnit unit)
    {
        if (unit == null) return 0f;
        switch (unit.Kind)
        {
            case Kind.Crystal:      return 200f;
            case Kind.King:         return 100f;
            case Kind.Boss:         return 80f;
            case Kind.Magicsniper:  return 35f;
            case Kind.Priest:       return 35f;
            case Kind.Bomber:       return 32f;
            case Kind.Magic:        return 30f;
            case Kind.Guardian:     return 30f;
            case Kind.Archer:       return 28f;
            case Kind.Crossbow:     return 28f;
            case Kind.Assassin:     return 26f;
            case Kind.Knight:       return 25f;
            case Kind.Scout:        return 18f;
            default:                return 20f;
        }
    }
}

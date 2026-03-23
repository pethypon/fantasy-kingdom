using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimActionGenerator — SimBoardState上で候補行動を生成する
//  MoveGererater / AttackPointt の移動・攻撃パターンを純粋関数として再現
//  GameObjectに一切依存しない
//
//  改善点:
//  - Direction.S のZ反転を完全再現
//  - スキル攻撃位置をAttackPointt.SkillAttackPositions/SkillFixedPositionsと同一に再現
//  - スタン・凍結・束縛による行動制限を考慮
//  - 召喚候補も生成
//  - 壁建築候補も生成
// =====================================================================
public static class SimActionGenerator
{
    // ================================================================
    //  移動パターン (MoveGererater.MovePredicateMap と同一)
    //  dx, dz は Direction.N 基準。Direction.S の場合は呼び出し側で dz を反転
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

    // 方向非依存の移動パターン（dx, dzの符号反転が不要なKind）
    static readonly HashSet<Kind> DirectionIndependentMove = new HashSet<Kind>
    {
        Kind.King, Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin, Kind.Boss,
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

    // 方向非依存の攻撃パターン
    static readonly HashSet<Kind> DirectionIndependentAttack = new HashSet<Kind>
    {
        Kind.Magic, Kind.Scout, Kind.Magicsniper,
    };

    // ---- スキル攻撃位置 (AttackPointt.SkillAttackPositions と同一) ----
    static readonly Dictionary<Kind, Vector2Int[]> SkillAttackPositions =
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
        { Kind.Boss,        new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1) } },
    };

    // ---- スキル固有の攻撃位置 ----
    static readonly Dictionary<int, Vector2Int[]> SkillFixedPositions =
        new Dictionary<int, Vector2Int[]>
    {
        { 7,  new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(1,1),
                      new Vector2Int(-1,0), new Vector2Int(1,0),
                      new Vector2Int(-1,-1), new Vector2Int(0,-1), new Vector2Int(1,-1) } },
        { 8,  new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3) } },
        { 22, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4) } },
        { 37, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4), new Vector2Int(0,5) } },
        { 48, new[] { new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3), new Vector2Int(0,4),
                      new Vector2Int(0,5), new Vector2Int(0,6), new Vector2Int(0,7) } },
    };

    // ================================================================
    //  方向係数 (Direction.S なら Z 反転)
    // ================================================================
    static int DirZ(Direction d) => d == Direction.S ? -1 : 1;

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
            if (unit.IsStunned) continue; // スタン中は行動不可

            if (!unit.IsMovementBlocked)
                GenerateMoveActions(board, unit, team, ap, actions);
            GenerateAttackActions(board, unit, team, enemyTeam, ap, actions);
            GenerateSkillActions(board, unit, team, enemyTeam, ap, actions);
        }

        // 建築・召喚候補
        GenerateBuildActions(board, team, ap, actions);
        GenerateSummonActions(board, team, ap, actions);

        return actions;
    }

    // ================================================================
    //  移動候補
    // ================================================================
    static void GenerateMoveActions(SimBoardState board, SimUnit unit, Team team,
        int ap, List<SimAction> results)
    {
        if (!MovePredicateMap.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcMoveCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;
        int dirZ = DirZ(unit.Direction);
        bool dirIndep = DirectionIndependentMove.Contains(unit.Kind);

        foreach (var tile in board.MapTiles)
        {
            float dx = tile.x - pos.x;
            float dz = tile.z - pos.z;

            // Direction.S の場合、方向依存パターンは Z を反転
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (!predicate(dx, dzAdj)) continue;
            if (board.IsOccupied(tile)) continue;

            results.Add(new SimAction
            {
                Type = SimActionType.Move,
                UnitId = unit.Id,
                TargetPos = tile,
                APCost = cost,
                ActorTeam = team
            });
        }
    }

    // ================================================================
    //  攻撃候補
    // ================================================================
    static void GenerateAttackActions(SimBoardState board, SimUnit unit, Team team,
        Team enemyTeam, int ap, List<SimAction> results)
    {
        if (!AttackPredicateMap.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcAttackCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;
        int dirZ = DirZ(unit.Direction);
        bool dirIndep = DirectionIndependentAttack.Contains(unit.Kind);

        // 全敵ユニット + 敵クリスタルを攻撃対象
        var targets = new List<SimUnit>();
        for (int i = 0; i < board.Units.Count; i++)
        {
            var t = board.Units[i];
            if (!t.IsAlive) continue;
            if (t.Team != enemyTeam) continue;
            targets.Add(t);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            float dx = target.Position.x - pos.x;
            float dz = target.Position.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (predicate(dx, dzAdj))
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.Attack,
                    UnitId = unit.Id,
                    TargetUnitId = target.Id,
                    TargetPos = target.Position,
                    APCost = cost,
                    ActorTeam = team
                });
            }
        }
    }

    // ================================================================
    //  スキル候補 (正確なスキル攻撃位置を使用)
    // ================================================================
    static void GenerateSkillActions(SimBoardState board, SimUnit unit, Team team,
        Team enemyTeam, int ap, List<SimAction> results)
    {
        if (unit.AssignedSkillId < 0) return;
        if (unit.SkillCooldown > 0) return;
        if (!SkillData.Table.TryGetValue(unit.AssignedSkillId, out var skill)) return;
        if (skill.APCost > ap) return;

        int dirZ = DirZ(unit.Direction);

        // スキル攻撃位置を決定
        Vector2Int[] offsets = null;
        if (SkillFixedPositions.ContainsKey(skill.Id))
            offsets = SkillFixedPositions[skill.Id];
        else if (SkillAttackPositions.ContainsKey(unit.Kind))
            offsets = SkillAttackPositions[unit.Kind];

        if (offsets == null) return;

        // Self / SelfArea スキル
        if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
        {
            // 自分自身にスキル使用（バフ系）
            if (skill.GrantBuff != BuffType.None || skill.FixedHeal > 0)
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = unit.Id,
                    TargetPos = unit.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
            return;
        }

        // オフセットからターゲット位置を計算
        var skillPositions = new HashSet<Vector3Int>();
        for (int i = 0; i < offsets.Length; i++)
        {
            int wx = unit.Position.x + offsets[i].x;
            int wz = unit.Position.z + offsets[i].y * dirZ;
            var sp = new Vector3Int(wx, 0, wz);
            if (board.MapTiles.Contains(sp))
                skillPositions.Add(sp);
        }

        // 攻撃スキル → 敵に使用
        if (skill.Multiplier > 0)
        {
            for (int i = 0; i < board.Units.Count; i++)
            {
                var t = board.Units[i];
                if (!t.IsAlive || t.Team != enemyTeam) continue;
                if (!skillPositions.Contains(t.Position)) continue;

                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = t.Id,
                    TargetPos = t.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
        }

        // 回復スキル → 味方に使用
        if (skill.FixedHeal > 0)
        {
            var allies = board.GetAliveUnits(team);
            for (int i = 0; i < allies.Count; i++)
            {
                var a = allies[i];
                if (a.Id == unit.Id) continue;
                if (a.HP >= a.MaxHP) continue;
                if (!skillPositions.Contains(a.Position)) continue;

                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = a.Id,
                    TargetPos = a.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
        }

        // バフスキル (味方支援)
        if (skill.GrantBuff != BuffType.None && skill.Multiplier <= 0 && skill.FixedHeal <= 0)
        {
            if (skill.Target == SkillTarget.AllySingle)
            {
                var allies = board.GetAliveUnits(team);
                for (int i = 0; i < allies.Count; i++)
                {
                    var a = allies[i];
                    if (a.Id == unit.Id) continue;
                    if (!skillPositions.Contains(a.Position)) continue;

                    results.Add(new SimAction
                    {
                        Type = SimActionType.SkillUse,
                        UnitId = unit.Id,
                        TargetUnitId = a.Id,
                        TargetPos = a.Position,
                        APCost = skill.APCost,
                        SkillId = unit.AssignedSkillId,
                        ActorTeam = team
                    });
                }
            }
        }
    }

    // ================================================================
    //  建築候補
    // ================================================================
    static void GenerateBuildActions(SimBoardState board, Team team, int ap,
        List<SimAction> results)
    {
        var counts = team == Team.Enemy ? board.EnemyBuildingCounts : board.PlayerBuildingCounts;

        FacilityKind[] priorities = {
            FacilityKind.Well, FacilityKind.LoggingCamp, FacilityKind.Quarry,
            FacilityKind.Field, FacilityKind.House, FacilityKind.Bakery,
            FacilityKind.LumberMill, FacilityKind.StoneWorks, FacilityKind.Smelter,
            FacilityKind.Barracks,
        };

        foreach (var fk in priorities)
        {
            if (!FacilityData.Table.TryGetValue(fk, out var info)) continue;
            if (info.APCost > ap) continue;

            int existing = 0;
            counts.TryGetValue(fk, out existing);
            if (existing >= 3) continue;

            var crystalPos = team == Team.Enemy ? board.EnemyCrystalPos : board.PlayerCrystalPos;

            results.Add(new SimAction
            {
                Type = SimActionType.Build,
                Facility = fk,
                TargetPos = crystalPos,
                APCost = info.APCost,
                ActorTeam = team
            });
        }
    }

    // ================================================================
    //  召喚候補
    // ================================================================
    static void GenerateSummonActions(SimBoardState board, Team team, int ap,
        List<SimAction> results)
    {
        Kind[] summonableKinds = {
            Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
            Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        };

        var crystalPos = team == Team.Enemy ? board.EnemyCrystalPos : board.PlayerCrystalPos;

        foreach (var kind in summonableKinds)
        {
            if (!UnitStaticData.Table.TryGetValue(kind, out var data)) continue;
            if (data.CostAP > ap) continue;

            // クリスタル周囲に空きマスを探す
            Vector3Int spawnPos = FindSpawnPosition(board, crystalPos);
            if (spawnPos.x == int.MinValue) continue;

            results.Add(new SimAction
            {
                Type = SimActionType.Summon,
                SummonKind = kind,
                TargetPos = spawnPos,
                APCost = data.CostAP,
                ActorTeam = team
            });
        }
    }

    static Vector3Int FindSpawnPosition(SimBoardState board, Vector3Int center)
    {
        // クリスタル周囲の空きマスを探す
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var pos = new Vector3Int(center.x + dx, 0, center.z + dz);
                if (board.MapTiles.Contains(pos) && !board.IsOccupied(pos))
                    return pos;
            }
        }
        return new Vector3Int(int.MinValue, 0, 0);
    }

    // ================================================================
    //  単一行動のスコアリング (探索のノード選択用: 簡易評価)
    //  上位N候補を絞り込むために使用。完全な評価はSimBoardEvaluatorで行う。
    //
    //  正値 = 行動者にとって有利
    // ================================================================
    public static float QuickScore(SimAction action, SimBoardState board)
    {
        float score = 0f;
        // 行動者のチームを使って敵クリスタル方向を判定
        bool isEnemy = action.ActorTeam == Team.Enemy;

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
                        if (target.Kind == Kind.Crystal) score += 500f;
                        if (target.Kind == Kind.King) score += 300f;
                    }
                    // シールド中のターゲットは低優先
                    if (target.ShieldTurns > 0)
                        score *= 0.1f;
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
                        score += dmg * 1.8f;
                        if (dmg >= target.HP)
                        {
                            score += GetPieceValue(target) * 1.5f;
                            if (target.Kind == Kind.Crystal) score += 400f;
                        }
                        // デバフ付与ボーナス
                        if (skill.InflictDebuff != StatusEffectType.None)
                            score += 5f;
                    }
                    if (skill.FixedHeal > 0)
                    {
                        float healValue = Mathf.Min(skill.FixedHeal, target.MaxHP - target.HP);
                        float hpRatio = target.MaxHP > 0 ? (float)target.HP / target.MaxHP : 1f;
                        score += healValue * (1f - hpRatio); // HPが低いほど回復の価値が高い
                    }
                    if (skill.GrantBuff != BuffType.None)
                        score += 8f;
                }
                break;
            }

            case SimActionType.Move:
            {
                var unit = board.GetUnit(action.UnitId);
                if (unit != null)
                {
                    // 敵クリスタルへの接近
                    Vector3Int targetCrystal = isEnemy
                        ? board.PlayerCrystalPos : board.EnemyCrystalPos;
                    float distBefore = SimUtil.Distance(unit.Position, targetCrystal);
                    float distAfter = SimUtil.Distance(action.TargetPos, targetCrystal);
                    score += (distBefore - distAfter) * 2f;

                    // 敵ユニットへの接近（攻撃射程に入る移動を高評価）
                    Team enemyTeam = isEnemy ? Team.Player : Team.Enemy;
                    var enemies = board.GetAliveUnits(enemyTeam);
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        float da = SimUtil.Distance(action.TargetPos, enemies[i].Position);
                        if (da <= 1.5f) score += 5f; // 攻撃射程に入る
                        else if (da <= 3f) score += 2f;
                    }

                    // 自陣クリスタル防衛
                    Vector3Int ownCrystal = isEnemy
                        ? board.EnemyCrystalPos : board.PlayerCrystalPos;
                    float ownCrystalDist = SimUtil.Distance(action.TargetPos, ownCrystal);
                    if (ownCrystalDist < 4f) score += 3f;
                }
                break;
            }

            case SimActionType.Build:
                score += 8f;
                // 早期の経済施設は特に有益
                if (board.TurnCount <= 15) score += 4f;
                break;

            case SimActionType.Summon:
                score += 15f;
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
            case Kind.SubCrystal:   return 40f;
            case Kind.WoodWall:     return 8f;
            case Kind.StoneWall:    return 12f;
            default:                return 20f;
        }
    }

    // ================================================================
    //  攻撃/移動射程の推定 (評価関数用)
    // ================================================================
    public static float EstimateAttackRange(Kind kind)
    {
        switch (kind)
        {
            case Kind.Magicsniper: return 4f;
            case Kind.Archer:     return 3f;
            case Kind.Bomber:     return 3f;
            case Kind.Magic:      return 2f;
            case Kind.Crossbow:   return 2f;
            default:              return 1.5f;
        }
    }

    public static float EstimateMoveRange(Kind kind)
    {
        switch (kind)
        {
            case Kind.Assassin:    return 2.5f;
            case Kind.Scout:       return 2f;
            case Kind.Crossbow:    return 3f;
            case Kind.Magicsniper: return 3f;
            case Kind.Bomber:      return 3f;
            default:               return 1.5f;
        }
    }

    // ================================================================
    //  あるユニットの有効な移動先の数を数える（モビリティ計算用）
    // ================================================================
    public static int CountMoves(SimBoardState board, SimUnit unit)
    {
        if (!MovePredicateMap.TryGetValue(unit.Kind, out var predicate)) return 0;
        if (unit.IsStunned || unit.IsMovementBlocked) return 0;

        int count = 0;
        var pos = unit.Position;
        int dirZ = DirZ(unit.Direction);
        bool dirIndep = DirectionIndependentMove.Contains(unit.Kind);

        foreach (var tile in board.MapTiles)
        {
            float dx = tile.x - pos.x;
            float dz = tile.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (predicate(dx, dzAdj) && !board.IsOccupied(tile))
                count++;
        }
        return count;
    }

    // ================================================================
    //  あるユニットが攻撃可能なターゲット数を数える
    // ================================================================
    public static int CountAttackTargets(SimBoardState board, SimUnit unit, Team enemyTeam)
    {
        if (!AttackPredicateMap.TryGetValue(unit.Kind, out var predicate)) return 0;
        if (unit.IsStunned) return 0;

        int count = 0;
        var pos = unit.Position;
        int dirZ = DirZ(unit.Direction);
        bool dirIndep = DirectionIndependentAttack.Contains(unit.Kind);

        for (int i = 0; i < board.Units.Count; i++)
        {
            var t = board.Units[i];
            if (!t.IsAlive || t.Team != enemyTeam) continue;
            float dx = t.Position.x - pos.x;
            float dz = t.Position.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;
            if (predicate(dx, dzAdj)) count++;
        }
        return count;
    }
}
